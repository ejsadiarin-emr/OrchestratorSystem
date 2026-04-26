using System.Text.Json;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Runtime;

public sealed class NodeWorkloadStateService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NodeWorkloadStateService> _logger;

    public NodeWorkloadStateService(IServiceProvider serviceProvider, ILogger<NodeWorkloadStateService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessMessageAsync(MessageEnvelope envelope, string connectionId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

        switch (envelope.MessageType)
        {
            case MessageTypes.AckClaim:
                await HandleAckClaimAsync(db, envelope);
                break;
            case MessageTypes.StepStatus:
                await HandleStepStatusAsync(db, envelope);
                break;
            case MessageTypes.Complete:
                await HandleCompleteAsync(db, envelope);
                break;
            case MessageTypes.Fail:
                await HandleFailAsync(db, envelope);
                break;
            case MessageTypes.LeaseHeartbeat:
                await HandleLeaseHeartbeatAsync(db, envelope);
                break;
            default:
                _logger.LogWarning("Unknown message type {MessageType} from agent {AgentId}", envelope.MessageType, envelope.AgentId);
                break;
        }

        await db.SaveChangesAsync();
    }

    private async Task HandleAckClaimAsync(InstallerDbContext db, MessageEnvelope envelope)
    {
        if (!TryParseRunId(envelope.RunId, out var runId) || !TryParseAgentId(envelope.AgentId, out var nodeId))
        {
            _logger.LogWarning("AckClaim missing runId or agentId");
            return;
        }

        var run = await db.WorkloadRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.NodeId == nodeId);
        if (run is not null && run.State == "Queued")
        {
            run.State = "Running";
            run.UpdatedAtUtc = DateTime.UtcNow;
        }

        await UpsertNodeWorkloadStateAsync(db, nodeId, run);
        await AddTimelineEntryAsync(db, runId, nodeId, MessageTypes.AckClaim, envelope.Sequence, detail: "Run claimed by agent");

        _logger.LogInformation("AckClaim processed: RunId={RunId}, NodeId={NodeId}", runId, nodeId);
    }

    private async Task HandleStepStatusAsync(InstallerDbContext db, MessageEnvelope envelope)
    {
        if (!TryParseRunId(envelope.RunId, out var runId) || !TryParseAgentId(envelope.AgentId, out var nodeId))
        {
            _logger.LogWarning("StepStatus missing runId or agentId");
            return;
        }

        var payload = TryDeserializePayload<StepStatusPayload>(envelope.Payload);
        if (payload is null)
        {
            _logger.LogWarning("StepStatus payload deserialization failed");
            return;
        }

        await UpdatePackageStateAsync(db, nodeId, runId, payload);
        await AddTimelineEntryAsync(db, runId, nodeId, MessageTypes.StepStatus, envelope.Sequence,
            packageId: payload.PackageId,
            packageIndex: payload.PackageIndex,
            stepName: payload.StepName,
            status: payload.Status,
            detail: payload.Error);

        _logger.LogInformation(
            "StepStatus processed: RunId={RunId}, NodeId={NodeId}, Step={StepName}, Package={PackageId}, Status={Status}",
            runId, nodeId, payload.StepName, payload.PackageId, payload.Status);
    }

    private async Task HandleCompleteAsync(InstallerDbContext db, MessageEnvelope envelope)
    {
        if (!TryParseRunId(envelope.RunId, out var runId) || !TryParseAgentId(envelope.AgentId, out var nodeId))
        {
            _logger.LogWarning("Complete missing runId or agentId");
            return;
        }

        var run = await db.WorkloadRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.NodeId == nodeId);
        if (run is not null)
        {
            run.State = "Completed";
            run.CompletedAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
        }

        await UpdateNodeWorkloadStateStatusAsync(db, nodeId, runId, "completed", setCurrentRevision: true);
        await AddTimelineEntryAsync(db, runId, nodeId, MessageTypes.Complete, envelope.Sequence, detail: "Run completed successfully");

        _logger.LogInformation("Complete processed: RunId={RunId}, NodeId={NodeId}", runId, nodeId);
    }

    private async Task HandleFailAsync(InstallerDbContext db, MessageEnvelope envelope)
    {
        if (!TryParseRunId(envelope.RunId, out var runId) || !TryParseAgentId(envelope.AgentId, out var nodeId))
        {
            _logger.LogWarning("Fail missing runId or agentId");
            return;
        }

        var run = await db.WorkloadRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.NodeId == nodeId);
        if (run is not null)
        {
            run.State = "Failed";
            run.CompletedAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
        }

        var payload = TryDeserializePayload<FinalizationPayload>(envelope.Payload);
        await UpdateNodeWorkloadStateStatusAsync(db, nodeId, runId, "failed");
        await AddTimelineEntryAsync(db, runId, nodeId, MessageTypes.Fail, envelope.Sequence, detail: payload?.Error ?? "Run failed");

        _logger.LogInformation("Fail processed: RunId={RunId}, NodeId={NodeId}, Error={Error}", runId, nodeId, payload?.Error);
    }

    private async Task HandleLeaseHeartbeatAsync(InstallerDbContext db, MessageEnvelope envelope)
    {
        if (!TryParseAgentId(envelope.AgentId, out var nodeId))
        {
            return;
        }

        var node = await db.Nodes.FindAsync(nodeId);
        if (node is not null)
        {
            node.LastSeenUtc = DateTime.UtcNow;

            var payload = TryDeserializePayload<HeartbeatPayload>(envelope.Payload);
            if (payload is not null)
            {
                if (!string.IsNullOrWhiteSpace(payload.OsVersion))
                {
                    node.OsVersion = payload.OsVersion;
                }
                if (!string.IsNullOrWhiteSpace(payload.AgentVersion))
                {
                    node.AgentVersion = payload.AgentVersion;
                }
            }
        }
    }

    private static async Task UpsertNodeWorkloadStateAsync(InstallerDbContext db, Guid nodeId, WorkloadRunEntity? run)
    {
        if (run is null)
        {
            return;
        }

        var state = await db.NodeWorkloadStates
            .FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == run.WorkloadId);

        if (state is null)
        {
            state = new NodeWorkloadStateEntity
            {
                NodeId = nodeId,
                WorkloadId = run.WorkloadId,
                PackageStatesJson = "{}",
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.NodeWorkloadStates.Add(state);
        }
        else
        {
            state.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static async Task UpdatePackageStateAsync(InstallerDbContext db, Guid nodeId, Guid runId, StepStatusPayload payload)
    {
        var run = await db.WorkloadRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.NodeId == nodeId);
        if (run is null)
        {
            return;
        }

        var state = await db.NodeWorkloadStates
            .FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == run.WorkloadId);

        if (state is null)
        {
            return;
        }

        var packageStates = JsonSerializer.Deserialize<Dictionary<string, PackageState>>(state.PackageStatesJson) ?? new();
        packageStates[payload.PackageId] = new PackageState
        {
            StepName = payload.StepName,
            Status = payload.Status,
            Error = payload.Error,
            UpdatedAt = DateTime.UtcNow
        };
        state.PackageStatesJson = JsonSerializer.Serialize(packageStates);
        state.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static async Task UpdateNodeWorkloadStateStatusAsync(InstallerDbContext db, Guid nodeId, Guid runId, string status, bool setCurrentRevision = false)
    {
        var run = await db.WorkloadRuns.FirstOrDefaultAsync(r => r.RunId == runId && r.NodeId == nodeId);
        if (run is null)
        {
            return;
        }

        var state = await db.NodeWorkloadStates
            .FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == run.WorkloadId);

        if (state is not null)
        {
            if (setCurrentRevision)
            {
                state.CurrentRevisionId = run.RevisionId;
            }
            state.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static async Task AddTimelineEntryAsync(
        InstallerDbContext db,
        Guid runId,
        Guid nodeId,
        string messageType,
        int sequence,
        string? packageId = null,
        int? packageIndex = null,
        string? stepName = null,
        string? status = null,
        string? detail = null)
    {
        db.WorkloadRunTimelines.Add(new WorkloadRunTimelineEntity
        {
            RunId = runId,
            NodeId = nodeId,
            MessageType = messageType,
            Sequence = sequence,
            PackageId = packageId,
            PackageIndex = packageIndex,
            StepName = stepName,
            Status = status,
            Detail = detail,
            AtUtc = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }

    private static bool TryParseRunId(string? runIdString, out Guid runId)
    {
        runId = Guid.Empty;
        return !string.IsNullOrWhiteSpace(runIdString) && Guid.TryParse(runIdString, out runId);
    }

    private static bool TryParseAgentId(string? agentIdString, out Guid agentId)
    {
        agentId = Guid.Empty;
        return !string.IsNullOrWhiteSpace(agentIdString) && Guid.TryParse(agentIdString, out agentId);
    }

    private static T? TryDeserializePayload<T>(object payload) where T : class
    {
        if (payload is T typed)
        {
            return typed;
        }

        if (payload is JsonElement jsonElement)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), options);
        }

        return null;
    }
}

public sealed class PackageState
{
    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StepStatusPayload
{
    public string StepName { get; set; } = string.Empty;
    public int PackageIndex { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class FinalizationPayload
{
    public string Result { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int StepCount { get; set; }
}

public sealed class HeartbeatPayload
{
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
}
