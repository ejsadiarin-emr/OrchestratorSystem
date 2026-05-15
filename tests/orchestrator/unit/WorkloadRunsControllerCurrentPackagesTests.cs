using System.Text.Json;
using DeploymentPoC.Orchestrator.Contracts.Api.WorkloadRuns;
using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Hubs;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

[TestFixture]
public class WorkloadRunsControllerCurrentPackagesTests
{
    private InstallerDbContext _db = null!;
    private PolicyEvaluationService _policyEvaluation = null!;
    private SqliteConnection _connection = null!;
    private string _tempArtifactPath = null!;

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

        _tempArtifactPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempArtifactPath);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ArtifactStore:RootPath", _tempArtifactPath } })
            .Build();
        var artifactStore = new ArtifactStoreService(config);
        _policyEvaluation = new PolicyEvaluationService(artifactStore);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_tempArtifactPath))
        {
            Directory.Delete(_tempArtifactPath, recursive: true);
        }
    }

    private WorkloadRunsController CreateController()
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ArtifactStore:RootPath"]).Returns(_tempArtifactPath);
        var artifactStore = new ArtifactStoreService(configMock.Object);
        var dispatcherLoggerMock = new Mock<ILogger<WorkloadRunDispatcher>>();
        var hubContextMock = new Mock<IHubContext<AgentRuntimeHub>>();
        hubContextMock.Setup(h => h.Clients).Returns(new Mock<IHubClients>().Object);
        var dispatcher = new WorkloadRunDispatcher(_db, hubContextMock.Object, artifactStore, dispatcherLoggerMock.Object);
        var loggerMock = new Mock<ILogger<WorkloadRunsController>>();
        var controller = new WorkloadRunsController(_db, _policyEvaluation, dispatcher, artifactStore, loggerMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Test]
    public async Task Create_PopulatesCurrentPackages_WhenNodeHasCurrentRevisionId()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var currentPackageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0")
            .WithRevision(currentRevisionId, workloadId, "0.9")
            .WithPackage(packageId, "new-package", "1.0", installType: "exe", installArgs: "/silent", uninstallArgs: "/uninstall")
            .WithPackage(currentPackageId, "current-package", "0.9", installType: "msi", installArgs: "/qn", uninstallArgs: "/x")
            .WithWorkloadPackage(revisionId, packageId)
            .WithWorkloadPackage(currentRevisionId, currentPackageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: currentRevisionId)
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "update",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());

        var createdRun = await _db.WorkloadRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorkloadId == workloadId && r.NodeId == nodeId);
        Assert.That(createdRun, Is.Not.Null);
        Assert.That(createdRun!.RevisionId, Is.EqualTo(revisionId));
        Assert.That(createdRun.State, Is.EqualTo("Queued"));
        Assert.That(createdRun.Mode, Is.EqualTo("update"));

        var nodeState = await _db.NodeWorkloadStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == workloadId);
        Assert.That(nodeState!.CurrentRevisionId, Is.EqualTo(currentRevisionId));

        var currentPkg = await _db.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageId == currentPackageId);
        Assert.That(currentPkg!.Name, Is.EqualTo("current-package"));
        Assert.That(currentPkg.Version, Is.EqualTo("0.9"));
        Assert.That(currentPkg.InstallType, Is.EqualTo("msi"));
        Assert.That(currentPkg.UninstallArgs, Is.EqualTo("/x"));
    }

    [Test]
    public async Task Create_CurrentPackagesIsEmpty_WhenNodeHasNoCurrentRevisionId()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0")
            .WithPackage(packageId, "new-package", "1.0", installType: "exe", installArgs: "/silent", uninstallArgs: "/uninstall")
            .WithWorkloadPackage(revisionId, packageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: null)
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "install",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());

        var createdRun = await _db.WorkloadRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorkloadId == workloadId && r.NodeId == nodeId);
        Assert.That(createdRun, Is.Not.Null);
        Assert.That(createdRun!.RevisionId, Is.EqualTo(revisionId));
        Assert.That(createdRun.State, Is.EqualTo("Queued"));
        Assert.That(createdRun.Mode, Is.EqualTo("install"));

        var nodeState = await _db.NodeWorkloadStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == workloadId);
        Assert.That(nodeState!.CurrentRevisionId, Is.Null);
    }

    [Test]
    public async Task Create_CurrentPackagesIncludesFullInstallAdapterConfig_WithUninstallArgs()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var currentPackageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0")
            .WithRevision(currentRevisionId, workloadId, "0.5")
            .WithPackage(currentPackageId, "app", "0.5", installType: "exe", installArgs: "/S", uninstallArgs: "/S /uninstall",
                expectedExitCodesJson: "[0,1]", timeoutSeconds: 120)
            .WithWorkloadPackage(currentRevisionId, currentPackageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: currentRevisionId)
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "update",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());

        var createdRun = await _db.WorkloadRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorkloadId == workloadId && r.NodeId == nodeId);
        Assert.That(createdRun, Is.Not.Null);
        Assert.That(createdRun!.State, Is.EqualTo("Queued"));
        Assert.That(createdRun.Mode, Is.EqualTo("update"));

        var currentPkg = await _db.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageId == currentPackageId);
        Assert.That(currentPkg!.InstallArgs, Is.EqualTo("/S"));
        Assert.That(currentPkg.UninstallArgs, Is.EqualTo("/S /uninstall"));
        Assert.That(currentPkg.ExpectedExitCodesJson, Is.EqualTo("[0,1]"));
        Assert.That(currentPkg.TimeoutSeconds, Is.EqualTo(120));
        Assert.That(currentPkg.InstallType, Is.EqualTo("exe"));
    }

    [Test]
    public async Task Create_CurrentPackages_PackageAssignmentName_FromPackageEntityName()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var currentPackageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0")
            .WithRevision(currentRevisionId, workloadId, "0.1")
            .WithPackage(currentPackageId, "MyApplication", "0.1", installType: "msi", installArgs: "/quiet", uninstallArgs: "/qn")
            .WithWorkloadPackage(currentRevisionId, currentPackageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: currentRevisionId)
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "uninstall",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());

        var createdRun = await _db.WorkloadRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorkloadId == workloadId && r.NodeId == nodeId);
        Assert.That(createdRun, Is.Not.Null);
        Assert.That(createdRun!.Mode, Is.EqualTo("uninstall"));
        Assert.That(createdRun.State, Is.EqualTo("Queued"));

        var currentPkg = await _db.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageId == currentPackageId);
        Assert.That(currentPkg!.Name, Is.EqualTo("MyApplication"));
        Assert.That(currentPkg.Version, Is.EqualTo("0.1"));
        Assert.That(currentPkg.InstallType, Is.EqualTo("msi"));
    }

    [Test]
    public async Task Create_CurrentPackages_InstallAdapterUninstallArgs_FromPackageEntityUninstallArgs()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var currentPackageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0")
            .WithRevision(currentRevisionId, workloadId, "0.2")
            .WithPackage(currentPackageId, "app", "0.2", installType: "exe", installArgs: "", uninstallArgs: "--remove --force")
            .WithWorkloadPackage(currentRevisionId, currentPackageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: currentRevisionId)
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "update",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());

        var createdRun = await _db.WorkloadRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorkloadId == workloadId && r.NodeId == nodeId);
        Assert.That(createdRun, Is.Not.Null);
        Assert.That(createdRun!.State, Is.EqualTo("Queued"));

        var currentPkg = await _db.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageId == currentPackageId);
        Assert.That(currentPkg!.UninstallArgs, Is.EqualTo("--remove --force"));
    }

    [Test]
    public async Task Create_AllowsUpdate_WhenNodeHasDriftOnDifferentRevision()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "2.0")
            .WithRevision(currentRevisionId, workloadId, "1.0")
            .WithPackage(packageId, "new-package", "2.0", installType: "exe", installArgs: "/silent", uninstallArgs: "/uninstall")
            .WithWorkloadPackage(revisionId, packageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: currentRevisionId,
                packageStatesJson: "{\"pkg1\":{\"status\":\"NotPresent\"}}")
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "install",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task Create_BlocksInstall_WhenNodeHasDriftOnSameRevisionAndReinstallIsFalse()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0")
            .WithPackage(packageId, "package", "1.0", installType: "exe", installArgs: "/silent", uninstallArgs: "/uninstall")
            .WithWorkloadPackage(revisionId, packageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: revisionId,
                packageStatesJson: "{\"pkg1\":{\"status\":\"NotPresent\"}}")
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "install",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId },
            Reinstall = false
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        var conflictResult = (ConflictObjectResult)result.Result!;
        var json = JsonSerializer.Serialize(conflictResult.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("message", out var msgProp), Is.True);
        Assert.That(msgProp.GetString(), Does.Contain("package drift"));
    }

    [Test]
    public async Task Create_AllowsInstall_WhenNodeHasDriftOnSameRevisionAndReinstallIsTrue()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0")
            .WithPackage(packageId, "package", "1.0", installType: "exe", installArgs: "/silent", uninstallArgs: "/uninstall")
            .WithWorkloadPackage(revisionId, packageId)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: revisionId,
                packageStatesJson: "{\"pkg1\":{\"status\":\"NotPresent\"}}")
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "install",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId },
            Reinstall = true
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task Create_ReturnsUnprocessableEntity_WhenVersionJumpDetected()
    {
        var workloadId = Guid.NewGuid();
        var oldRevisionId = Guid.NewGuid();
        var newRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(oldRevisionId, workloadId, "1.0.0")
            .WithRevision(newRevisionId, workloadId, "2.0.0")
            .WithPackage(packageId, "test-pkg", "1.0.0", installType: "archive", installArgs: "", sourcePath: "/tmp/test")
            .WithWorkloadPackage(oldRevisionId, packageId, packageIndex: 1)
            .WithWorkloadPackage(newRevisionId, packageId, packageIndex: 1)
            .WithNode(nodeId)
            .WithNodeWorkloadState(nodeId, workloadId, currentRevisionId: newRevisionId)
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = oldRevisionId,
            Mode = "install",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<UnprocessableEntityObjectResult>());
        var unprocessable = (UnprocessableEntityObjectResult)result.Result!;
        var json = JsonSerializer.Serialize(unprocessable.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("code", out var codeProp), Is.True);
        Assert.That(codeProp.GetString(), Is.EqualTo("VERSION_JUMP_BLOCKED"));
        Assert.That(doc.RootElement.TryGetProperty("message", out var msgProp), Is.True);
        Assert.That(msgProp.GetString(), Does.Contain("skip"));
    }

    [Test]
    public async Task Create_ReturnsConflict_WithConflictingRunDetails_WhenActiveRunExists()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var existingRunId = Guid.NewGuid();

        await new TestSeedBuilder(_db)
            .WithWorkload(workloadId)
            .WithRevision(revisionId, workloadId, "1.0.0")
            .WithPackage(packageId, "test-pkg", "1.0.0", installType: "archive", installArgs: "", sourcePath: "/tmp/test")
            .WithWorkloadPackage(revisionId, packageId, packageIndex: 1)
            .WithNode(nodeId)
            .WithWorkloadRun(existingRunId, workloadId, revisionId, nodeId, state: "Queued", idempotencyKey: "existing-key")
            .SeedAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "install",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        var conflict = (ConflictObjectResult)result.Result!;
        var json = JsonSerializer.Serialize(conflict.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.That(root.TryGetProperty("code", out var codeProp), Is.True);
        Assert.That(codeProp.GetString(), Is.EqualTo("ACTIVE_RUN_CONFLICT"));
        Assert.That(root.TryGetProperty("conflictingRunId", out var runIdProp), Is.True);
        Assert.That(runIdProp.GetGuid(), Is.EqualTo(existingRunId));
        Assert.That(root.TryGetProperty("conflictingRunState", out var stateProp), Is.True);
        Assert.That(stateProp.GetString(), Is.EqualTo("Queued"));
    }
}
