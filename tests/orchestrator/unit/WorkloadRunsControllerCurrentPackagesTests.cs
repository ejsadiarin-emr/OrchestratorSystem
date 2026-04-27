using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
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
    private Mock<IHubContext<AgentRuntimeHub>> _hubContextMock = null!;
    private Mock<IClientProxy> _clientProxyMock = null!;
    private List<MessageEnvelope> _capturedEnvelopes = null!;
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

        _capturedEnvelopes = new List<MessageEnvelope>();
        _clientProxyMock = new Mock<IClientProxy>();
        _clientProxyMock.Setup(p => p.SendCoreAsync("AssignRun", It.IsAny<object?[]?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[]?, CancellationToken>((_, args, _) =>
            {
                if (args is not null && args.Length > 0 && args[0] is MessageEnvelope env)
                {
                    _capturedEnvelopes.Add(env);
                }
            })
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);

        _hubContextMock = new Mock<IHubContext<AgentRuntimeHub>>();
        _hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
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
        var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configMock.Setup(c => c["ArtifactStore:RootPath"]).Returns(_tempArtifactPath);
        var artifactStore = new DeploymentPoC.Orchestrator.Services.ArtifactStoreService(configMock.Object);
        var dispatcherLoggerMock = new Mock<ILogger<WorkloadRunDispatcher>>();
        var dispatcher = new WorkloadRunDispatcher(_db, _hubContextMock.Object, artifactStore, dispatcherLoggerMock.Object);
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

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0",
            IsPublished = true
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = currentRevisionId,
            WorkloadId = workloadId,
            Version = "0.9",
            IsPublished = true
        });

        _db.Packages.Add(new PackageEntity
        {
            PackageId = packageId,
            Name = "new-package",
            Version = "1.0",
            InstallType = "exe",
            InstallArgs = "/silent",
            UninstallArgs = "/uninstall"
        });

        _db.Packages.Add(new PackageEntity
        {
            PackageId = currentPackageId,
            Name = "current-package",
            Version = "0.9",
            InstallType = "msi",
            InstallArgs = "/qn",
            UninstallArgs = "/x"
        });

        _db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = revisionId,
            PackageId = packageId,
            PackageIndex = 0
        });

        _db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = currentRevisionId,
            PackageId = currentPackageId,
            PackageIndex = 0
        });

        _db.Nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = "test-node",
            DisplayName = "Test Node"
        });

        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = currentRevisionId,
            PackageStatesJson = "{}"
        });

        await _db.SaveChangesAsync();

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
        Assert.That(_capturedEnvelopes, Has.Count.EqualTo(1));
        var envelope = _capturedEnvelopes[0];
        Assert.That(envelope.Payload, Is.TypeOf<AssignRunPayload>());
        var payload = (AssignRunPayload)envelope.Payload;
        Assert.That(payload.CurrentPackages, Has.Count.EqualTo(1));

        var currentPackage = payload.CurrentPackages[0];
        Assert.That(currentPackage.PackageId, Is.EqualTo(currentPackageId.ToString()));
        Assert.That(currentPackage.Name, Is.EqualTo("current-package"));
        Assert.That(currentPackage.Version, Is.EqualTo("0.9"));
        Assert.That(currentPackage.InstallAdapter.Type, Is.EqualTo("msi"));
        Assert.That(currentPackage.InstallAdapter.UninstallArgs, Is.EqualTo("/x"));
    }

    [Test]
    public async Task Create_CurrentPackagesIsEmpty_WhenNodeHasNoCurrentRevisionId()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0",
            IsPublished = true
        });

        _db.Packages.Add(new PackageEntity
        {
            PackageId = packageId,
            Name = "new-package",
            Version = "1.0",
            InstallType = "exe",
            InstallArgs = "/silent",
            UninstallArgs = "/uninstall"
        });

        _db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = revisionId,
            PackageId = packageId,
            PackageIndex = 0
        });

        _db.Nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = "test-node",
            DisplayName = "Test Node"
        });

        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = null,
            PackageStatesJson = "{}"
        });

        await _db.SaveChangesAsync();

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
        Assert.That(_capturedEnvelopes, Has.Count.EqualTo(1));
        var envelope = _capturedEnvelopes[0];
        Assert.That(envelope.Payload, Is.TypeOf<AssignRunPayload>());
        var payload = (AssignRunPayload)envelope.Payload;
        Assert.That(payload.CurrentPackages, Is.Empty);
    }

    [Test]
    public async Task Create_CurrentPackagesIncludesFullInstallAdapterConfig_WithUninstallArgs()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var currentPackageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0",
            IsPublished = true
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = currentRevisionId,
            WorkloadId = workloadId,
            Version = "0.5",
            IsPublished = true
        });

        _db.Packages.Add(new PackageEntity
        {
            PackageId = currentPackageId,
            Name = "app",
            Version = "0.5",
            InstallType = "exe",
            InstallArgs = "/S",
            UninstallArgs = "/S /uninstall",
            ExpectedExitCodesJson = "[0,1]",
            TimeoutSeconds = 120
        });

        _db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = currentRevisionId,
            PackageId = currentPackageId,
            PackageIndex = 0
        });

        _db.Nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = "test-node",
            DisplayName = "Test Node"
        });

        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = currentRevisionId,
            PackageStatesJson = "{}"
        });

        await _db.SaveChangesAsync();

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
        Assert.That(_capturedEnvelopes, Has.Count.EqualTo(1));
        var payload = (AssignRunPayload)_capturedEnvelopes[0].Payload;
        Assert.That(payload.CurrentPackages, Has.Count.EqualTo(1));

        var adapter = payload.CurrentPackages[0].InstallAdapter;
        Assert.That(adapter.Command, Is.EqualTo("{artifactPath}"));
        Assert.That(adapter.Arguments, Is.EqualTo("/S"));
        Assert.That(adapter.UninstallArgs, Is.EqualTo("/S /uninstall"));
        Assert.That(adapter.ExpectedExitCodes, Is.EquivalentTo(new[] { 0, 1 }));
        Assert.That(adapter.TimeoutSeconds, Is.EqualTo(120));
    }

    [Test]
    public async Task Create_CurrentPackages_PackageAssignmentName_FromPackageEntityName()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var currentPackageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0",
            IsPublished = true
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = currentRevisionId,
            WorkloadId = workloadId,
            Version = "0.1",
            IsPublished = true
        });

        _db.Packages.Add(new PackageEntity
        {
            PackageId = currentPackageId,
            Name = "MyApplication",
            Version = "0.1",
            InstallType = "msi",
            InstallArgs = "/quiet",
            UninstallArgs = "/qn"
        });

        _db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = currentRevisionId,
            PackageId = currentPackageId,
            PackageIndex = 0
        });

        _db.Nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = "test-node",
            DisplayName = "Test Node"
        });

        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = currentRevisionId,
            PackageStatesJson = "{}"
        });

        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRunRequest
        {
            WorkloadId = workloadId,
            RevisionId = revisionId,
            Mode = "rollback",
            IdempotencyKey = Guid.NewGuid().ToString(),
            NodeIds = new List<Guid> { nodeId }
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var payload = (AssignRunPayload)_capturedEnvelopes[0].Payload;
        Assert.That(payload.CurrentPackages[0].Name, Is.EqualTo("MyApplication"));
    }

    [Test]
    public async Task Create_CurrentPackages_InstallAdapterUninstallArgs_FromPackageEntityUninstallArgs()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var currentRevisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var currentPackageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0",
            IsPublished = true
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = currentRevisionId,
            WorkloadId = workloadId,
            Version = "0.2",
            IsPublished = true
        });

        _db.Packages.Add(new PackageEntity
        {
            PackageId = currentPackageId,
            Name = "app",
            Version = "0.2",
            InstallType = "exe",
            InstallArgs = "",
            UninstallArgs = "--remove --force"
        });

        _db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = currentRevisionId,
            PackageId = currentPackageId,
            PackageIndex = 0
        });

        _db.Nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = "test-node",
            DisplayName = "Test Node"
        });

        _db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = currentRevisionId,
            PackageStatesJson = "{}"
        });

        await _db.SaveChangesAsync();

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
        var payload = (AssignRunPayload)_capturedEnvelopes[0].Payload;
        Assert.That(payload.CurrentPackages[0].InstallAdapter.UninstallArgs, Is.EqualTo("--remove --force"));
    }
}
