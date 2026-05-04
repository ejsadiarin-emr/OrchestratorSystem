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
        string detectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"cmd\"}",
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

        var state = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(state.Status, Is.EqualTo("Current"));
        Assert.That(state.PackageStatesJson, Does.Contain("\"comparison\":\"same\""));
        Assert.That(state.PackageStatesJson, Does.Contain("\"expectedVersion\":\"1.0.0\""));
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
    public async Task UnassignedWorkload_AllMatch_ReportsPassed()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "fresh-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionId, WorkloadId = workloadId, Version = "1.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageId, Name = "fresh-pkg", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"fresh\"}" });
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

        var summary = responses[0].Summary;
        var pkgItem = summary.Items.FirstOrDefault(i => i.Category == "package" && i.Name == "fresh-pkg");
        Assert.That(pkgItem, Is.Not.Null);
        Assert.That(pkgItem!.Status, Is.EqualTo("passed"));

        var driftItem = summary.Items.FirstOrDefault(i => i.Name.Contains("packages:"));
        Assert.That(driftItem, Is.Not.Null);
        Assert.That(driftItem!.Status, Is.EqualTo("passed"));
        Assert.That(driftItem.Name, Does.Contain("1/1"));

        var stateAfter = await _db.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId);
        Assert.That(stateAfter, Is.Not.Null, "DB state should be created for unassigned workloads with detected packages");
        Assert.That(stateAfter!.Status, Is.EqualTo("Current"));
    }

    [Test]
    public async Task UnassignedWorkload_PartialMatch_ReportsDrift()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageIdA = Guid.NewGuid();
        var packageIdB = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "partial-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionId, WorkloadId = workloadId, Version = "1.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdA, Name = "pkg-a", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"a\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdB, Name = "pkg-b", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"b\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageIdA, PackageIndex = 0 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageIdB, PackageIndex = 1 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "partial-node", IpAddress = "10.0.0.5", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        Assert.That(await _db.NodeWorkloadStates.CountAsync(s => s.NodeId == nodeId), Is.EqualTo(0));

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageIdA, Name = "pkg-a", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
            new() { PackageId = packageIdB, Name = "pkg-b", Status = PreCheckStatus.NotPresent }
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
        var pkgA = summary.Items.FirstOrDefault(i => i.Name == "pkg-a");
        Assert.That(pkgA, Is.Not.Null);
        Assert.That(pkgA!.Status, Is.EqualTo("passed"));
        var pkgB = summary.Items.FirstOrDefault(i => i.Name == "pkg-b");
        Assert.That(pkgB, Is.Not.Null);
        Assert.That(pkgB!.Status, Is.EqualTo("info"));

        var driftItem = summary.Items.FirstOrDefault(i => i.Name.Contains("packages:"));
        Assert.That(driftItem, Is.Not.Null);
        Assert.That(driftItem!.Status, Is.EqualTo("warning"));
        Assert.That(driftItem.Name, Does.Contain("1/2"));

        var stateAfter = await _db.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId);
        Assert.That(stateAfter, Is.Not.Null, "DB state should be created for unassigned workloads with detected packages");
        Assert.That(stateAfter!.Status, Is.EqualTo("Drifted"));
    }

    [Test]
    public async Task UnassignedWorkload_NoneMatch_ReportsMissing()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "empty-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionId, WorkloadId = workloadId, Version = "1.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageId, Name = "missing-pkg", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"missing\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionId, PackageId = packageId, PackageIndex = 0 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "empty-node", IpAddress = "10.0.0.6", OsVersion = "Windows", AgentVersion = "1.0" });
        await _db.SaveChangesAsync();

        Assert.That(await _db.NodeWorkloadStates.CountAsync(s => s.NodeId == nodeId), Is.EqualTo(0));

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageId, Name = "missing-pkg", Status = PreCheckStatus.NotPresent }
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
        var pkgItem = summary.Items.FirstOrDefault(i => i.Name == "missing-pkg");
        Assert.That(pkgItem, Is.Not.Null);
        Assert.That(pkgItem!.Status, Is.EqualTo("info"));
        Assert.That(pkgItem.Detail, Is.EqualTo("not installed"));

        var driftItem = summary.Items.FirstOrDefault(i => i.Name.Contains("packages:"));
        Assert.That(driftItem, Is.Not.Null);
        Assert.That(driftItem!.Status, Is.EqualTo("info"));
        Assert.That(driftItem.Name, Does.Contain("0/1"));

        var stateAfter = await _db.NodeWorkloadStates.CountAsync(s => s.NodeId == nodeId);
        Assert.That(stateAfter, Is.EqualTo(0), "No DB state should be created for unassigned workloads");
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
        Assert.That(pkgItem.Detail, Does.Contain("newer"));

        var state = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(state.PackageStatesJson, Does.Contain(packageId.ToString()));
        Assert.That(state.PackageStatesJson, Does.Contain("\"comparison\":\"newer\""));
        Assert.That(state.Status, Is.EqualTo("Drifted"));
    }

    [Test]
    public async Task ScenarioE_DbDrift_SomePackagesMissing_MarksWarning()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);

        var secondPackageId = Guid.NewGuid();
        _db.Packages.Add(new PackageEntity { PackageId = secondPackageId, Name = "pkg-b", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"pkgb\"}" });
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

        var state = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(state.Status, Is.EqualTo("Drifted"));
    }

    [Test]
    public async Task PartialProbeResults_PartialUpdateWithWarning()
    {
        var (workloadId, revisionId, nodeId, packageId, _) = SeedScenarioBase(_db);

        var secondPackageId = Guid.NewGuid();
        _db.Packages.Add(new PackageEntity { PackageId = secondPackageId, Name = "pkg-partial", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"version_manifest\",\"Path\":\"partial\"}" });
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

    [Test]
    public async Task AssignedRevision_AllPackagesInstalled_ReportsPassed()
    {
        var workloadId = Guid.NewGuid();
        var revisionIdV1 = Guid.NewGuid();
        var revisionIdV2 = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageIdV1 = Guid.NewGuid();
        var packageIdV2 = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "amazing-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV1, WorkloadId = workloadId, Version = "1.0.0", IsPublished = false });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV2, WorkloadId = workloadId, Version = "2.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdV1, Name = "dbeaver", Version = "24.3.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"dbeaver\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdV2, Name = "dbeaver", Version = "26.0.3", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"dbeaver26\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV1, PackageId = packageIdV1, PackageIndex = 0 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV2, PackageId = packageIdV2, PackageIndex = 0 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "assigned-node", IpAddress = "10.0.0.7", OsVersion = "Windows 11", AgentVersion = "1.0" });
        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionIdV1,
            PackageStatesJson = "{}"
        });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageIdV1, Name = "dbeaver", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "24.3.0" }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunSinglePreCheck(nodeId, null);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var summary = okResult.Value as NodePreCheckSummary;
        Assert.That(summary, Is.Not.Null);

        var pkgItem = summary!.Items.FirstOrDefault(i => i.Category == "package" && i.Name == "dbeaver");
        Assert.That(pkgItem, Is.Not.Null);
        Assert.That(pkgItem!.Status, Is.EqualTo("passed"));
        Assert.That(pkgItem.ActualVersion, Is.EqualTo("24.3.0"));

        Assert.That(summary.Items.Any(i => i.Status == "failed"), Is.False,
            "No items should have failed status");

        var state = await _db.NodeWorkloadStates.FirstAsync(s => s.NodeId == nodeId);
        Assert.That(state.PackageStatesJson, Does.Contain("AlreadySatisfied"));
        Assert.That(state.PackageStatesJson, Does.Not.Contain("NotPresent"));
    }

    [Test]
    public async Task AssignedRevision_SomeMissing_ReportsDrift()
    {
        var workloadId = Guid.NewGuid();
        var revisionIdV1 = Guid.NewGuid();
        var revisionIdV2 = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageIdA = Guid.NewGuid();
        var packageIdB = Guid.NewGuid();
        var packageIdC = Guid.NewGuid();
        var packageIdV2 = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "drift-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV1, WorkloadId = workloadId, Version = "1.0.0", IsPublished = false });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV2, WorkloadId = workloadId, Version = "2.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdA, Name = "pkg-a", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"a\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdB, Name = "pkg-b", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"b\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdC, Name = "pkg-c", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"c\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdV2, Name = "pkg-v2", Version = "2.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"v2\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV1, PackageId = packageIdA, PackageIndex = 0 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV1, PackageId = packageIdB, PackageIndex = 1 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV1, PackageId = packageIdC, PackageIndex = 2 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV2, PackageId = packageIdV2, PackageIndex = 0 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "drift-node", IpAddress = "10.0.0.8", OsVersion = "Windows 11", AgentVersion = "1.0" });
        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionIdV1,
            PackageStatesJson = "{}"
        });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageIdA, Name = "pkg-a", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
            new() { PackageId = packageIdB, Name = "pkg-b", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
            new() { PackageId = packageIdC, Name = "pkg-c", Status = PreCheckStatus.NotPresent }
        });
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(handler);

        var result = await controller.RunSinglePreCheck(nodeId, null);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var summary = okResult.Value as NodePreCheckSummary;
        Assert.That(summary, Is.Not.Null);

        var passedItems = summary!.Items.Where(i => i.Status == "passed" && i.Category == "package").ToList();
        Assert.That(passedItems.Count, Is.EqualTo(2));
        var failedItems = summary.Items.Where(i => i.Status == "failed" && i.Category == "package").ToList();
        Assert.That(failedItems.Count, Is.EqualTo(1));

        var driftItem = summary.Items.FirstOrDefault(i => i.Name.Contains("drift"));
        Assert.That(driftItem, Is.Not.Null);
        Assert.That(driftItem!.Status, Is.EqualTo("warning"));
        Assert.That(driftItem.Name, Does.Contain("2/3"));
    }

    [Test]
    public async Task ExplicitWorkloadId_UsesPublishedRevision()
    {
        var workloadId = Guid.NewGuid();
        var revisionIdV1 = Guid.NewGuid();
        var revisionIdV2 = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageIdV1 = Guid.NewGuid();
        var packageIdV2 = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "explicit-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV1, WorkloadId = workloadId, Version = "1.0.0", IsPublished = false });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV2, WorkloadId = workloadId, Version = "2.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdV1, Name = "pkg-a", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"pkg-a\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdV2, Name = "pkg-b", Version = "2.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"pkg-b\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV1, PackageId = packageIdV1, PackageIndex = 0 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV2, PackageId = packageIdV2, PackageIndex = 0 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = "explicit-node", IpAddress = "10.0.0.9", OsVersion = "Windows 11", AgentVersion = "1.0" });
        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionIdV1,
            PackageStatesJson = "{}"
        });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageIdV2, Name = "pkg-b", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "2.0.0" }
        });
        var captureHandler = new CaptureHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(captureHandler);

        await controller.RunSinglePreCheck(nodeId, workloadId);

        Assert.That(captureHandler.CapturedBodies.Count, Is.EqualTo(1));
        var capturedBody = captureHandler.CapturedBodies[0];
        var detectRequest = JsonSerializer.Deserialize<DetectRequest>(
            capturedBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.That(detectRequest, Is.Not.Null);
        Assert.That(detectRequest!.Packages.Count, Is.EqualTo(1));
        Assert.That(detectRequest.Packages[0].PackageId, Is.EqualTo(packageIdV2));
        Assert.That(detectRequest.Packages[0].Version, Is.EqualTo("2.0.0"));
    }

    [Test]
    public async Task RunPreChecks_WithoutWorkloadId_ProbesAssignedRevisions()
    {
        var workloadId = Guid.NewGuid();
        var revisionIdV1 = Guid.NewGuid();
        var revisionIdV2 = Guid.NewGuid();
        var nodeId1 = Guid.NewGuid();
        var nodeId2 = Guid.NewGuid();
        var packageIdV1 = Guid.NewGuid();
        var packageIdV2 = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity { WorkloadId = workloadId, Name = "multi-node-workload" });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV1, WorkloadId = workloadId, Version = "1.0.0", IsPublished = false });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity { RevisionId = revisionIdV2, WorkloadId = workloadId, Version = "2.0.0", IsPublished = true });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdV1, Name = "pkg-v1", Version = "1.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"v1\"}" });
        _db.Packages.Add(new PackageEntity { PackageId = packageIdV2, Name = "pkg-v2", Version = "2.0.0", DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"v2\"}" });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV1, PackageId = packageIdV1, PackageIndex = 0 });
        _db.WorkloadPackages.Add(new WorkloadPackageEntity { WorkloadPackageId = Guid.NewGuid(), RevisionId = revisionIdV2, PackageId = packageIdV2, PackageIndex = 0 });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId1, Hostname = "node-v1", IpAddress = "10.0.0.10", OsVersion = "Windows 11", AgentVersion = "1.0" });
        _db.Nodes.Add(new NodeEntity { NodeId = nodeId2, Hostname = "node-v2", IpAddress = "10.0.0.11", OsVersion = "Windows 11", AgentVersion = "1.0" });
        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId1,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionIdV1,
            PackageStatesJson = "{}"
        });
        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId2,
            WorkloadId = workloadId,
            CurrentRevisionId = revisionIdV2,
            PackageStatesJson = "{}"
        });
        await _db.SaveChangesAsync();

        var agentResponse = BuildAgentResponse(new List<PackageDetectionResult>
        {
            new() { PackageId = packageIdV1, Name = "pkg-v1", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
            new() { PackageId = packageIdV2, Name = "pkg-v2", Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "2.0.0" }
        });
        var captureHandler = new CaptureHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(agentResponse, Encoding.UTF8, "application/json") });
        var controller = CreateController(captureHandler);

        var result = await controller.RunPreChecks(new RunPreCheckRequest
        {
            NodeIds = new List<Guid> { nodeId1, nodeId2 }
        });

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());

        Assert.That(captureHandler.CapturedBodies.Count, Is.EqualTo(2), "Should probe each node separately");

        var allRequests = captureHandler.CapturedBodies
            .Select(b => JsonSerializer.Deserialize<DetectRequest>(
                b, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
            .ToList();

        var allProbedPackageIds = allRequests
            .SelectMany(r => r.Packages.Select(p => p.PackageId))
            .ToList();

        Assert.That(allRequests.Select(r => r.Packages.Count), Has.All.EqualTo(1),
            "Each node should be probed with only its assigned revision's packages");
        Assert.That(allProbedPackageIds, Is.EquivalentTo(new[] { packageIdV1, packageIdV2 }),
            "Each node should get only its own revision's packages");
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

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        public List<string> CapturedBodies { get; } = new();
        private readonly HttpResponseMessage _response;

        public CaptureHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync();
                CapturedBodies.Add(body);
            }
            return _response;
        }
    }
}
