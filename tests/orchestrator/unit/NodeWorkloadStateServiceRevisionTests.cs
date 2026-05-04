using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class NodeWorkloadStateServiceRevisionTests
{
    private ServiceProvider _serviceProvider = null!;
    private SqliteConnection _connection = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<InstallerDbContext>(options => options.UseSqlite(_connection));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    private NodeWorkloadStateService CreateService()
    {
        var loggerMock = new Mock<ILogger<NodeWorkloadStateService>>();
        return new NodeWorkloadStateService(_serviceProvider, loggerMock.Object);
    }

    private async Task<(Guid runId, Guid nodeId, Guid workloadId, Guid revisionId)> SeedRunAndStateAsync(bool withExistingState = true, Guid? currentRevisionId = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "test-workload"
        });

        db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0",
            IsPublished = true
        });

        if (currentRevisionId.HasValue && currentRevisionId.Value != revisionId)
        {
            db.WorkloadRevisions.Add(new WorkloadRevisionEntity
            {
                RevisionId = currentRevisionId.Value,
                WorkloadId = workloadId,
                Version = "0.9",
                IsPublished = true
            });
        }

        db.Nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = "test-node",
            DisplayName = "Test Node"
        });

        db.WorkloadRuns.Add(new WorkloadRunEntity
        {
            WorkloadRunRecordId = Guid.NewGuid(),
            RunId = runId,
            WorkloadId = workloadId,
            RevisionId = revisionId,
            NodeId = nodeId,
            NodeDisplayName = "Test Node",
            Mode = "install",
            State = "Queued",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        if (withExistingState)
        {
            db.NodeWorkloadStates.Add(new NodeWorkloadStateEntity
            {
                NodeWorkloadStateId = Guid.NewGuid(),
                NodeId = nodeId,
                WorkloadId = workloadId,
                CurrentRevisionId = currentRevisionId,
                PackageStatesJson = "{}"
            });
        }

        await db.SaveChangesAsync();
        return (runId, nodeId, workloadId, revisionId);
    }

    [Test]
    public async Task AckClaim_DoesNotUpdateCurrentRevisionId()
    {
        var existingRevisionId = Guid.NewGuid();
        var (runId, nodeId, workloadId, revisionId) = await SeedRunAndStateAsync(withExistingState: true, currentRevisionId: existingRevisionId);

        var service = CreateService();
        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.AckClaim,
            RunId = runId.ToString(),
            AgentId = nodeId.ToString(),
            Sequence = 1
        };

        await service.ProcessMessageAsync(envelope, "conn-1");

        using var assertScope = _serviceProvider.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        var state = await assertDb.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == workloadId);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.CurrentRevisionId, Is.EqualTo(existingRevisionId));
    }

    [Test]
    public async Task Complete_DoesUpdateCurrentRevisionId_WhenWorkWasDone()
    {
        var existingRevisionId = Guid.NewGuid();
        var (runId, nodeId, workloadId, revisionId) = await SeedRunAndStateAsync(withExistingState: true, currentRevisionId: existingRevisionId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        db.WorkloadRunTimelines.Add(new WorkloadRunTimelineEntity
        {
            TimelineId = Guid.NewGuid(),
            RunId = runId,
            NodeId = nodeId,
            MessageType = MessageTypes.StepStatus,
            Sequence = 1,
            StepName = "InstallOrUpgrade",
            Status = "success",
            AtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.Complete,
            RunId = runId.ToString(),
            AgentId = nodeId.ToString(),
            Sequence = 2
        };

        await service.ProcessMessageAsync(envelope, "conn-1");

        using var assertScope = _serviceProvider.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        var state = await assertDb.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == workloadId);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.CurrentRevisionId, Is.EqualTo(revisionId));
        Assert.That(state.Status, Is.EqualTo("Current"));
    }

    [Test]
    public async Task Complete_DoesUpdateCurrentRevisionId_WhenNoWorkWasDone()
    {
        var existingRevisionId = Guid.NewGuid();
        var (runId, nodeId, workloadId, revisionId) = await SeedRunAndStateAsync(withExistingState: true, currentRevisionId: existingRevisionId);

        // No InstallOrUpgrade or UninstallPackage timeline entries — only pre-checks
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        db.WorkloadRunTimelines.Add(new WorkloadRunTimelineEntity
        {
            TimelineId = Guid.NewGuid(),
            RunId = runId,
            NodeId = nodeId,
            MessageType = MessageTypes.StepStatus,
            Sequence = 1,
            StepName = "PreCheckProbe",
            Status = "success",
            AtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService();
        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.Complete,
            RunId = runId.ToString(),
            AgentId = nodeId.ToString(),
            Sequence = 2
        };

        await service.ProcessMessageAsync(envelope, "conn-1");

        using var assertScope = _serviceProvider.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        var state = await assertDb.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == workloadId);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.CurrentRevisionId, Is.EqualTo(revisionId));
        Assert.That(state.Status, Is.EqualTo("Current"));
    }

    [Test]
    public async Task Fail_DoesNotUpdateCurrentRevisionId()
    {
        var existingRevisionId = Guid.NewGuid();
        var (runId, nodeId, workloadId, revisionId) = await SeedRunAndStateAsync(withExistingState: true, currentRevisionId: existingRevisionId);

        var service = CreateService();
        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.Fail,
            RunId = runId.ToString(),
            AgentId = nodeId.ToString(),
            Sequence = 2,
            Payload = new FinalizationPayload { Result = "failed", Error = "something went wrong" }
        };

        await service.ProcessMessageAsync(envelope, "conn-1");

        using var assertScope = _serviceProvider.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        var state = await assertDb.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == workloadId);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.CurrentRevisionId, Is.EqualTo(existingRevisionId));
        Assert.That(state.Status, Is.EqualTo("Drifted"));
    }

    [Test]
    public async Task AckClaim_CreatesStateRecord_WhenMissing()
    {
        var (runId, nodeId, workloadId, revisionId) = await SeedRunAndStateAsync(withExistingState: false);

        var service = CreateService();
        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.AckClaim,
            RunId = runId.ToString(),
            AgentId = nodeId.ToString(),
            Sequence = 1
        };

        await service.ProcessMessageAsync(envelope, "conn-1");

        using var assertScope = _serviceProvider.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        var state = await assertDb.NodeWorkloadStates.FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == workloadId);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.CurrentRevisionId, Is.Null);
        Assert.That(state.Status, Is.EqualTo("Unknown"));
    }
}
