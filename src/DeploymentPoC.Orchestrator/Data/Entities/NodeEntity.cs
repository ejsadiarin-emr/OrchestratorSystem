namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class NodeEntity
{
    public Guid NodeId { get; set; } = Guid.NewGuid();
    public string? AgentId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public List<ConfigSnapshotEntity> ConfigSnapshots { get; set; } = new();
}
