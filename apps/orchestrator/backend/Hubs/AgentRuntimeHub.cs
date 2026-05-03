using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Runtime;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Hubs;

// NOTE: SignalR hub remains available, but the active control path is HTTP polling.
public sealed class AgentRuntimeHub : Hub
{
    private readonly NodeWorkloadStateService _stateService;
    private readonly AgentConnectionTracker _connectionTracker;
    private readonly InstallerDbContext _db;
    private readonly WorkloadRunDispatcher _dispatcher;
    private readonly ILogger<AgentRuntimeHub> _logger;

    public AgentRuntimeHub(NodeWorkloadStateService stateService, AgentConnectionTracker connectionTracker, InstallerDbContext db, WorkloadRunDispatcher dispatcher, ILogger<AgentRuntimeHub> logger)
    {
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _connectionTracker = connectionTracker ?? throw new ArgumentNullException(nameof(connectionTracker));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Identify(Guid nodeId)
    {
        _connectionTracker.Register(nodeId, Context.ConnectionId);
        _logger.LogInformation("Agent identified: NodeId={NodeId}, ConnectionId={ConnectionId}", nodeId, Context.ConnectionId);

        var node = await _db.Nodes.FindAsync(nodeId);
        if (node is not null)
        {
            node.Status = "Online";
            node.LastSeenUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"node-{nodeId}");
        _logger.LogInformation("Agent added to group {GroupName}: NodeId={NodeId}, ConnectionId={ConnectionId}", $"node-{nodeId}", nodeId, Context.ConnectionId);

        // Re-send any queued runs for this node now that it is in the group
        await _dispatcher.DispatchQueuedRunsAsync(nodeId, Context.ConnectionAborted);
    }

    public async Task SendMessage(MessageEnvelope envelope)
    {
        if (envelope is null)
        {
            _logger.LogWarning("Received null envelope from connection {ConnectionId}", Context.ConnectionId);
            return;
        }

        _logger.LogDebug(
            "Hub received {MessageType} from agent {AgentId} connection {ConnectionId}",
            envelope.MessageType,
            envelope.AgentId,
            Context.ConnectionId);

        try
        {
            await _stateService.ProcessMessageAsync(envelope, Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process {MessageType} from agent {AgentId}", envelope.MessageType, envelope.AgentId);
        }
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Agent connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
        {
            _logger.LogWarning(exception, "Agent disconnected with error: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("Agent disconnected: {ConnectionId}", Context.ConnectionId);
        }

        if (_connectionTracker.TryGetNodeId(Context.ConnectionId, out var disconnectedNodeId))
        {
            var node = await _db.Nodes.FindAsync(disconnectedNodeId);
            if (node is not null)
            {
                node.Status = "Offline";
                await _db.SaveChangesAsync();
            }
        }

        _connectionTracker.Unregister(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
