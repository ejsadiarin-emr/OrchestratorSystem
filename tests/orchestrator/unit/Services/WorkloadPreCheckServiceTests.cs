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

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class WorkloadPreCheckServiceTests
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

    private static string BuildAgentResponse(List<PackageDetectionResult> results,
        long freeBytes = 100_000_000, long totalBytes = 500_000_000)
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

    private static (Guid workloadId, Guid revisionId, Guid nodeId, Guid packageId) SeedBaseScenario(
        InstallerDbContext db,
        string? nodeStatus = null,
        string? detectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"cmd\"}")
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

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

        if (nodeStatus is not null)
        {
            db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
            {
                NodeWorkloadStateId = Guid.NewGuid(),
                NodeId = nodeId,
                WorkloadId = workloadId,
                CurrentRevisionId = revisionId,
                PackageStatesJson = "{}",
                Status = nodeStatus
            });
        }

        db.SaveChanges();
        return (workloadId, revisionId, nodeId, packageId);
    }

    [Test]
    public async Task EmptyNodeList_RunPreChecks_ReturnsBadRequest()
    {
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var controller = CreateController(handler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid>()
        });

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result!;
        Assert.That(badRequest.Value, Is.Not.Null);
    }

    [Test]
    public async Task EmptyNodeList_RunPreCheckSummary_ReturnsBadRequest()
    {
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid>(),
            WorkloadId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid()
        });

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result!;
        Assert.That(badRequest.Value, Is.Not.Null);
    }

    [Test]
    public async Task AllAlreadySatisfied_NoExistingState_ReconcilesToSkip()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db);

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes.Count, Is.EqualTo(1));
        Assert.That(response.Nodes[0].NodeId, Is.EqualTo(nodeId));
        Assert.That(response.Nodes[0].Action, Is.EqualTo("Skip"));
        Assert.That(response.Nodes[0].WorkloadStatus, Is.EqualTo("Current"));

        Assert.That(response.Nodes[0].Packages.Count, Is.EqualTo(1));
        Assert.That(response.Nodes[0].Packages[0].Status, Is.EqualTo("AlreadySatisfied"));
        Assert.That(response.Nodes[0].Packages[0].Comparison, Is.EqualTo("same"));
        Assert.That(response.Nodes[0].Packages[0].ActualVersion, Is.EqualTo("1.0.0"));

        var state = await _db.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId);
        Assert.That(state, Is.Not.Null, "DB state should be created on probe with detected packages");
    }

    [Test]
    public async Task AllNotPresent_NoExistingState_ActionFreshInstall()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db);

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.NotPresent }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes[0].Action, Is.EqualTo("FreshInstall"));
        Assert.That(response.Nodes[0].WorkloadStatus, Is.EqualTo("Absent"));
        Assert.That(response.Nodes[0].Packages[0].Status, Is.EqualTo("NotPresent"));
    }

    [Test]
    public async Task AllAlreadySatisfied_ExistingCurrentState_ActionSkip()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db, nodeStatus: "Current");

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes[0].Action, Is.EqualTo("Skip"));
        Assert.That(response.Nodes[0].WorkloadStatus, Is.EqualTo("Current"));

        var state = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(state.Status, Is.EqualTo("Current"));
    }

    [Test]
    public async Task WrongVersion_OlderActualVersion_ActionUpdate()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db, nodeStatus: "Unknown");

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.WrongVersion, ActualVersion = "0.9.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes[0].Action, Is.EqualTo("Update"));

        Assert.That(response.Nodes[0].Packages[0].Status, Is.EqualTo("WrongVersion"));
        Assert.That(response.Nodes[0].Packages[0].Comparison, Is.EqualTo("older"));
    }

    [Test]
    public async Task NotPresent_ActionInstallMissing()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db, nodeStatus: "Unknown");

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.NotPresent }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes[0].Action, Is.EqualTo("InstallMissing"));
        Assert.That(response.Nodes[0].Packages[0].Status, Is.EqualTo("NotPresent"));
    }

    [Test]
    public async Task MixedResults_SomeSatisfiedSomeNotPresent_ActionInstallMissing()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageIdA = Guid.NewGuid();
        var packageIdB = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "mixed-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionId, WorkloadId = workloadId, Version = "1.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdA, Name = "pkg-a", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"a\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdB, Name = "pkg-b", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"b\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageIdA, PackageIndex = 0 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageIdB, PackageIndex = 1 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "mixed-node", IpAddress = "10.0.0.2", OsVersion = "Windows", AgentVersion = "1.0" });
        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionId,
            PackageStatesJson = "{}",
            Status = "Unknown"
        });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageIdA, Name = "pkg-a", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
            new() { PackageId = packageIdB, Name = "pkg-b", Status = PreCheckStatus.NotPresent }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes[0].Action, Is.EqualTo("InstallMissing"));

        var foundPkgA = response.Nodes[0].Packages.FirstOrDefault(p => p.Name == "pkg-a");
        var foundPkgB = response.Nodes[0].Packages.FirstOrDefault(p => p.Name == "pkg-b");
        Assert.That(foundPkgA, Is.Not.Null);
        Assert.That(foundPkgB, Is.Not.Null);
        Assert.That(foundPkgA!.Status, Is.EqualTo("AlreadySatisfied"));
        Assert.That(foundPkgB!.Status, Is.EqualTo("NotPresent"));
    }

    [Test]
    public async Task WrongVersionNewer_ActionBlockedDowngrade()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db, nodeStatus: "Unknown");

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.WrongVersion, ActualVersion = "2.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes[0].Action, Is.EqualTo("BlockedDowngrade"));
        Assert.That(response.Nodes[0].Packages[0].Comparison, Is.EqualTo("newer"));
    }

    [Test]
    public async Task AgentUnreachable_SummaryActionUnknown_WithDetail()
    {
        var nodeId = Guid.NewGuid();
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "offline-node", IpAddress = "10.0.0.99", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        var throwingHandler = new ThrowingHttpMessageHandler(new HttpRequestException("Connection refused"));
        var controller = CreateController(throwingHandler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid()
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes.Count, Is.EqualTo(1));
        Assert.That(response.Nodes[0].Action, Is.EqualTo("Unknown"));
        Assert.That(response.Nodes[0].ActionDetail, Is.Not.Null);
        Assert.That(response.Nodes[0].ActionDetail, Does.Contain("Connection refused"));
    }

    [Test]
    public async Task AgentUnreachable_RunPreChecks_ReturnsError()
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
        Assert.That(responses[0].Error, Is.Not.Null);
        Assert.That(responses[0].Error, Does.Contain("unreachable"));
        Assert.That(responses[0].Error, Does.Contain("Connection refused"));
    }

    [Test]
    public async Task MultipleNodes_DifferentStates_CorrectActionsPerNode()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId1 = Guid.NewGuid();
        var nodeId2 = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "multi-node-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionId, WorkloadId = workloadId, Version = "1.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageId, Name = "shared-pkg", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"shared\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageId, PackageIndex = 0 });

        _db.Nodes.Add(new NodeEntity { NodeId = nodeId1, Hostname = "node-with-state", IpAddress = "10.0.0.10", OsVersion = "Windows", AgentVersion = "1.0" });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId2, Hostname = "node-no-state", IpAddress = "10.0.0.11", OsVersion = "Windows", AgentVersion = "1.0" });

        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId1,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionId,
            PackageStatesJson = "{}",
            Status = "Current"
        });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "shared-pkg", Status = PreCheckStatus.NotPresent }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId1, nodeId2 },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes.Count, Is.EqualTo(2));

        var node1Result = response.Nodes.First(n => n.NodeId == nodeId1);
        Assert.That(node1Result.Action, Is.EqualTo("InstallMissing"));
        Assert.That(node1Result.WorkloadStatus, Is.EqualTo("Drifted"));

        var node2Result = response.Nodes.First(n => n.NodeId == nodeId2);
        Assert.That(node2Result.Action, Is.EqualTo("FreshInstall"));
        Assert.That(node2Result.WorkloadStatus, Is.EqualTo("Absent"));
    }

    [Test]
    public async Task MultipleNodes_OneUnreachable_OneSuccessful()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId1 = Guid.NewGuid();
        var nodeId2 = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "partial-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionId, WorkloadId = workloadId, Version = "1.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageId, Name = "shared-pkg", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"shared\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageId, PackageIndex = 0 });

        _db.Nodes.Add(new NodeEntity { NodeId = nodeId1, Hostname = "reachable-node", IpAddress = "10.0.0.20", OsVersion = "Windows", AgentVersion = "1.0" });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId2, Hostname = "unreachable-node", IpAddress = "10.0.0.21", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "shared-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" }
        });

        var selectiveHandler = new SelectiveHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString().Contains("10.0.0.21"))
            {
                throw new HttpRequestException("Connection refused");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(agentResponse, Encoding.UTF8, "application/json")
            };
        });
        var controller = CreateController(selectiveHandler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId1, nodeId2 },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes.Count, Is.EqualTo(2));

        var reachableNode = response.Nodes.First(n => n.NodeId == nodeId1);
        Assert.That(reachableNode.Action, Is.EqualTo("Skip"));

        var unreachableNode = response.Nodes.First(n => n.NodeId == nodeId2);
        Assert.That(unreachableNode.Action, Is.EqualTo("Unknown"));
        Assert.That(unreachableNode.ActionDetail, Is.Not.Null);
    }

    [Test]
    public async Task Timeout_ReturnsErrorWithoutCrashing()
    {
        var nodeId = Guid.NewGuid();
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "slow-node", IpAddress = "10.0.0.99", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        var timeoutHandler = new TimeoutHttpMessageHandler();
        var controller = CreateController(timeoutHandler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId }
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var responses = okResult.Value as List<NodePreCheckResponse>;
        Assert.That(responses, Is.Not.Null);
        Assert.That(responses!.Count, Is.EqualTo(1));
        Assert.That(responses[0].Error, Is.Not.Null);
        Assert.That(responses[0].Error, Does.Contain("timed out"));
    }

    [Test]
    public async Task StaleNodeWorkloadState_UpdatedOnProbe()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db, nodeStatus: "Drifted");
        var originalUpdatedAt = await _db.NodeWorkloadStates
            .Where(s => s.NodeId == nodeId)
            .Select(s => s.UpdatedAtUtc)
            .FirstAsync();

        await Task.Delay(10);

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        var state = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(state.UpdatedAtUtc, Is.GreaterThan(originalUpdatedAt), "UpdatedAtUtc should be refreshed");
        Assert.That(state.Status, Is.EqualTo("Current"), "Status should be reconciled to Current");
        Assert.That(state.LastProbedAtUtc, Is.Not.Null);
    }

    [Test]
    public async Task NoDetectionConfigs_AgentResponds_SummaryCreated()
    {
        var workloadId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "no-detection-workload" });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "no-detection-node", IpAddress = "10.0.0.30", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>());
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = Guid.NewGuid()
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes.Count, Is.EqualTo(1));
        Assert.That(response.Nodes[0].Action, Is.EqualTo("FreshInstall"));
        Assert.That(response.Nodes[0].Packages, Is.Empty);
    }

    [Test]
    public async Task DriftedWorkloadStatus_WrongVersion_ActionUpdate()
    {
        var (workloadId, revisionId, nodeId, packageId) = SeedBaseScenario(_db, nodeStatus: "Drifted");

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "test-pkg", Status = PreCheckStatus.WrongVersion, ActualVersion = "0.9.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunPreCheckSummary(new RunPreCheckSummaryRequest
        {
            NodeIds = new List<Guid> { nodeId },
            WorkloadId = workloadId,
            RevisionId = revisionId
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as PreCheckSummaryResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes[0].Action, Is.EqualTo("Update"));
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

    private sealed class TimeoutHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException();
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class SelectiveHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public SelectiveHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
