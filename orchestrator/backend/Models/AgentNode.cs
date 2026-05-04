namespace Orchestrator.Models;

public class AgentNode
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string AgentSecret { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public AgentNodeStatus Status { get; set; } = AgentNodeStatus.REGISTERED;
    public string? AssignedWorkloadId { get; set; }
    public string? AssignedWorkloadVersion { get; set; }
    public int PollingIntervalSeconds { get; set; } = 30;
    public Workload? Workload { get; set; }
    public ICollection<AgentPackage> InstalledPackages { get; set; } = [];
    public ICollection<WorkloadRun> Runs { get; set; } = [];
}
