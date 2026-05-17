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
public class WorkloadRunsControllerReportTests
{
    private InstallerDbContext _db = null!;
    private PolicyEvaluationService _policyEvaluation = null!;
    private Mock<IHubContext<AgentRuntimeHub>> _hubContextMock = null!;
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

        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock.Setup(p => p.SendCoreAsync("AssignRun", It.IsAny<object?[]?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

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
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ArtifactStore:RootPath", _tempArtifactPath }, { "Heartbeat:StaleThresholdSeconds", "15" } })
            .Build();
        var artifactStore = new ArtifactStoreService(config);
        var dispatcherLoggerMock = new Mock<ILogger<WorkloadRunDispatcher>>();
        var dispatcher = new WorkloadRunDispatcher(_db, _hubContextMock.Object, artifactStore, dispatcherLoggerMock.Object);
        var loggerMock = new Mock<ILogger<WorkloadRunsController>>();
        var controller = new WorkloadRunsController(_db, _policyEvaluation, dispatcher, artifactStore, loggerMock.Object, config);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private async Task<(Guid WorkloadId, Guid RevisionId)> SeedWorkloadAndRevision()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0.0",
            IsPublished = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (workloadId, revisionId);
    }

    [Test]
    public async Task GetReport_ReturnsReport_WhenReportStored()
    {
        var (workloadId, revisionId) = await SeedWorkloadAndRevision();
        var runId = Guid.NewGuid();
        var reportText = "Package installed successfully\nVersion: 1.0.0";

        _db.WorkloadRuns.Add(new WorkloadRunEntity
        {
            RunId = runId,
            WorkloadId = workloadId,
            RevisionId = revisionId,
            State = "Completed",
            ReportText = reportText,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetReport(runId);

        Assert.That(result, Is.TypeOf<ContentResult>());
        var content = (ContentResult)result;
        Assert.That(content.Content, Is.EqualTo(reportText));
        Assert.That(content.ContentType, Is.EqualTo("text/plain"));
        Assert.That(content.StatusCode, Is.Null.Or.EqualTo(200));
    }

    [Test]
    public async Task GetReport_Returns404_WhenRunHasNoReport()
    {
        var (workloadId, revisionId) = await SeedWorkloadAndRevision();
        var runId = Guid.NewGuid();

        _db.WorkloadRuns.Add(new WorkloadRunEntity
        {
            RunId = runId,
            WorkloadId = workloadId,
            RevisionId = revisionId,
            State = "Completed",
            ReportText = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetReport(runId);

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task GetReport_Returns404_WhenRunNotFound()
    {
        var controller = CreateController();
        var result = await controller.GetReport(Guid.NewGuid());

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task UpdateStatus_StoresReport_OnComplete()
    {
        var (workloadId, revisionId) = await SeedWorkloadAndRevision();
        var runId = Guid.NewGuid();
        var reportText = "All packages installed successfully";

        _db.WorkloadRuns.Add(new WorkloadRunEntity
        {
            RunId = runId,
            WorkloadId = workloadId,
            RevisionId = revisionId,
            State = "Queued",
            ReportText = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new RunStatusUpdateRequest
        {
            Status = "Completed",
            Report = reportText
        };

        var result = await controller.UpdateStatus(runId, request, agentId: null);

        Assert.That(result, Is.TypeOf<NoContentResult>());

        var run = await _db.WorkloadRuns.SingleAsync(r => r.RunId == runId);
        Assert.That(run.State, Is.EqualTo("Completed"));
        Assert.That(run.ReportText, Is.EqualTo(reportText));
    }

    [Test]
    public async Task UpdateStatus_StoresReport_OnFail()
    {
        var (workloadId, revisionId) = await SeedWorkloadAndRevision();
        var runId = Guid.NewGuid();
        var reportText = "Install failed: disk full";

        _db.WorkloadRuns.Add(new WorkloadRunEntity
        {
            RunId = runId,
            WorkloadId = workloadId,
            RevisionId = revisionId,
            State = "Running",
            ReportText = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new RunStatusUpdateRequest
        {
            Status = "Failed",
            Report = reportText
        };

        var result = await controller.UpdateStatus(runId, request, agentId: null);

        Assert.That(result, Is.TypeOf<NoContentResult>());

        var run = await _db.WorkloadRuns.SingleAsync(r => r.RunId == runId);
        Assert.That(run.State, Is.EqualTo("Failed"));
        Assert.That(run.ReportText, Is.EqualTo(reportText));
    }

    [Test]
    public async Task UpdateStatus_DoesNotStoreNullReport()
    {
        var (workloadId, revisionId) = await SeedWorkloadAndRevision();
        var runId = Guid.NewGuid();

        _db.WorkloadRuns.Add(new WorkloadRunEntity
        {
            RunId = runId,
            WorkloadId = workloadId,
            RevisionId = revisionId,
            State = "Queued",
            ReportText = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new RunStatusUpdateRequest
        {
            Status = "Completed",
            Report = null
        };

        var result = await controller.UpdateStatus(runId, request, agentId: null);

        Assert.That(result, Is.TypeOf<NoContentResult>());

        var run = await _db.WorkloadRuns.SingleAsync(r => r.RunId == runId);
        Assert.That(run.State, Is.EqualTo("Completed"));
        Assert.That(run.ReportText, Is.Null);
    }
}
