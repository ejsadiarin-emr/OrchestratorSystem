using System.Net;
using System.Text;
using System.Text.Json;
using DeploymentPoC.Contracts.Runtime.Probes;
using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

[TestFixture]
public class NodesPreCheckReconciliationTests
{
    private InstallerDbContext _db = null!;
    private SqliteConnection _connection = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private IConfiguration _configuration = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new InstallerDbContext(options);
        _db.Database.EnsureCreated();

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentProbeTimeoutSeconds"] = "10"
            })
            .Build();
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private NodesController CreateController(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var loggerMock = new Mock<ILogger<NodesController>>();
        var controller = new NodesController(
            _db,
            loggerMock.Object,
            _httpClientFactoryMock.Object,
            _configuration);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        return controller;
    }

    private static (Guid workloadId, Guid revisionId, Guid nodeId, Guid packageId, Guid stateId) SeedScenarioBase(
        InstallerDbContext db,
        string detectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"cmd\",\"ExpectedVersion\":\"1.0.0\"}",
        string? packageStatesJson = "{}")
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var stateId = Guid.NewGuid();

        db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0.0",
            IsPublished = true
        });

        db.Packages.Add(new PackageEntity
        {
            PackageId = packageId,
            Name = "test-pkg",
            Version = "1.0.0",
            DetectionConfigJson = detectionConfigJson
        });

        db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = revisionId,
            PackageId = packageId,
            PackageIndex = 0
        });

        db.Nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = "test-node",
            IpAddress = "10.0.0.1",
            OsVersion = "Windows 11",
            AgentVersion = "1.0.0"
        });

        db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = stateId,
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionId,
            PackageStatesJson = packageStatesJson ?? "{}"
        });

        db.SaveChanges();
        return (workloadId, revisionId, nodeId, packageId, stateId);
    }

    private static string BuildAgentResponse(List<PackageDetectionResult> results, long freeBytes = 100_000_000, long totalBytes = 500_000_000)
    {
        var response = new NodeDetectResponse
        {
            Results = results,
            DiskInfo = new DiskInfo
            {
                FreeBytes = freeBytes,
                TotalBytes = totalBytes,
                Drive = "C:\\"
            }
        };
        return JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    [Test]
    public async Task AgentUnreachable_ReturnsErrorAndDbUnchanged()
    {
        var nodeId = Guid.NewGuid();
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "offline-node", IpAddress = "10.0.0.99", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        var throwingHandler = new ThrowingHttpMessageHandler(new HttpRequestException("Connection refused"));
        var controller = CreateController(throwingHandler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId }
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses!.Count, Is.EqualTo(1));
        Assert.That(responses[0].NodeId, Is.EqualTo(nodeId));
        Assert.That(responses[0].Error, Is.Not.Null);
        Assert.That(responses[0].Error, Does.Contain("unreachable"));

        var nodeEntity = await _db.Nodes.Include(n => n.NodeWorkloadStates).SingleAsync(n => n.NodeId == nodeId);
        Assert.That(nodeEntity.NodeWorkloadStates.Count, Is.EqualTo(0), "DB should be unchanged when agent is unreachable");
    }

    [Test]
    public async Task AgentReturnsNon200_ErrorSurfacedAndDbUnchanged()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);
        var stateCount = await _db.NodeWorkloadStates.CountAsync();

        var errorHandler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var controller = CreateController(errorHandler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses!.Count, Is.EqualTo(1));
        Assert.That(responses[0].Error, Does.Contain("500"));

        Assert.That(await _db.NodeWorkloadStates.CountAsync(), Is.EqualTo(stateCount), "DB should be unchanged");
    }

    [Test]
    public async Task EmptyPackageList_ReturnsBasicSummaryWithNoPackageItems()
    {
        var nodeId = Guid.NewGuid();
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "empty-node", IpAddress = "10.0.0.3", OsVersion = "Windows 11", AgentVersion = "2.0" });
        await _db.SaveChangesAsync();

        var responseJson = BuildAgentResponse(new List<PackageDetectionResult>());
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId }
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses!.Count, Is.EqualTo(1));
        Assert.That(responses[0].Error, Is.Null);

        var summary = responses[0].Summary;
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary.Items.Count, Is.EqualTo(3), "Should have os, agent, disk items but no package items");
        Assert.That(summary.Items.All(i => i.Category != "package"), Is.True);
    }

    [Test]
    public async Task ScenarioA_DbMatch_AgentConfirmsAlreadySatisfied()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses![0].Error, Is.Null);

        var summary = responses[0].Summary;
        var pkgItem = summary.Items.FirstOrDefault(i => i.Category == "package" && i.Name == "test-pkg");
        Assert.That(pkgItem, Is.Not.Null);
        Assert.That(pkgItem!.Status, Is.EqualTo("passed"));
    }

    [Test]
    public async Task ScenarioB_DbInstalled_AgentNotPresent_RemovesState()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);
        var stateBefore = await _db.NodeWorkloadStates.CountAsync(s => s.NodeId == nodeId);
        Assert.That(stateBefore, Is.EqualTo(1));

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.NotPresent }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses![0].Error, Is.Null);

        var summary = responses[0].Summary;
        var failedItem = summary.Items.FirstOrDefault(i => i.Status == "failed");
        Assert.That(failedItem, Is.Not.Null);
        Assert.That(failedItem!.Detail, Is.EqualTo("not installed"));

        var stateAfter = await _db.NodeWorkloadStates.CountAsync(s => s.NodeId == nodeId);
        Assert.That(stateAfter, Is.EqualTo(0), "DB state should be removed");
    }

    [Test]
    public async Task ScenarioC_NoDbState_AgentDetectsPackages_CreatesState()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "fresh-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionId, WorkloadId = workloadId, Version = "1.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageId, Name = "fresh-pkg", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"fresh\",\"ExpectedVersion\":\"1.0.0\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageId, PackageIndex = 0 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "fresh-node", IpAddress = "10.0.0.4", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        Assert.That(await _db.NodeWorkloadStates.CountAsync(s => s.NodeId == nodeId), Is.EqualTo(0));

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "fresh-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses![0].Error, Is.Null);

        var stateAfter = await _db.NodeWorkloadStates.CountAsync(s => s.NodeId == nodeId);
        Assert.That(stateAfter, Is.EqualTo(1), "DB state should be created from probe results");

        var createdState = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(createdState.PackageStatesJson, Is.Not.EqualTo("{}"));
    }

    [Test]
    public async Task ScenarioD_DbDrift_SomePackagesDifferent_MarksWarning()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.WrongVersion, ActualVersion = "2.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses![0].Error, Is.Null);

        var summary = responses[0].Summary;
        var driftItem = summary.Items.FirstOrDefault(i => i.Status == "warning" && i.Name.Contains("drift"));
        Assert.That(driftItem, Is.Not.Null);
        Assert.That(driftItem!.Name, Does.Contain("drift"));

        var pkgItem = summary.Items.FirstOrDefault(i => i.Category == "package" && i.Name == "test-pkg");
        Assert.That(pkgItem, Is.Not.Null);
        Assert.That(pkgItem!.Status, Is.EqualTo("warning"));

        var state = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(state.PackageStatesJson, Does.Contain(packageId.ToString()));
    }

    [Test]
    public async Task ScenarioE_DbDrift_SomePackagesMissing_MarksWarning()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);

        var secondPackageId = Guid.NewGuid();
        _db.Packages.Add(new PackageEntity { PackageId = secondPackageId, Name = "pkg-b", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"pkgb\",\"ExpectedVersion\":\"1.0.0\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = secondPackageId, PackageIndex = 1 });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
            new() { PackageId = secondPackageId, Name = "pkg-b", Status = PreCheckStatus.NotPresent }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses![0].Error, Is.Null);

        var summary = responses[0].Summary;
        var driftItem = summary.Items.FirstOrDefault(i => i.Name.Contains("drift"));
        Assert.That(driftItem, Is.Not.Null);
        Assert.That(driftItem!.Status, Is.EqualTo("warning"));
        Assert.That(driftItem.Name, Does.Contain("1/2"));
    }

    [Test]
    public async Task PartialProbeResults_PartialUpdateWithWarning()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);

        var secondPackageId = Guid.NewGuid();
        _db.Packages.Add(new PackageEntity { PackageId = secondPackageId, Name = "pkg-partial", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"partial\",\"ExpectedVersion\":\"1.0.0\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = secondPackageId, PackageIndex = 1 });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses![0].Error, Is.Null);

        var summary = responses[0].Summary;
        var packages = summary.Items.Where(i => i.Category == "package" && i.Name != "pkg-partial" && !i.Name.Contains("drift")).ToList();
        Assert.That(packages.Count, Is.EqualTo(1), "Only test-pkg was probed, pkg-partial not in agent results");

        var driftWarning = summary.Items.FirstOrDefault(i => i.Name.Contains("drift"));
        Assert.That(driftWarning, Is.Not.Null, "Should have drift warning for partial results");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }
}
