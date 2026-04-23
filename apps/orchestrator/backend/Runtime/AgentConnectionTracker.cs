using System.Collections.Concurrent;

namespace DeploymentPoC.Orchestrator.Runtime;

public sealed class AgentConnectionTracker
{
    private readonly ConcurrentDictionary<Guid, string> _nodeToConnection = new();
    private readonly ConcurrentDictionary<string, Guid> _connectionToNode = new();

    public void Register(Guid nodeId, string connectionId)
    {
        _nodeToConnection[nodeId] = connectionId;
        _connectionToNode[connectionId] = nodeId;
    }

    public void Unregister(string connectionId)
    {
        if (_connectionToNode.TryRemove(connectionId, out var nodeId))
        {
            _nodeToConnection.TryRemove(nodeId, out _);
        }
    }

    public bool TryGetConnectionId(Guid nodeId, out string? connectionId)
    {
        return _nodeToConnection.TryGetValue(nodeId, out connectionId);
    }

    public bool TryGetNodeId(string connectionId, out Guid nodeId)
    {
        return _connectionToNode.TryGetValue(connectionId, out nodeId);
    }
}
