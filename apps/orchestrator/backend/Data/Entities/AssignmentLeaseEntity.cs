namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class AssignmentLeaseEntity
{
    public Guid AssignmentId { get; set; } = Guid.NewGuid();
    public string LeaseId { get; set; } = Guid.NewGuid().ToString("N");
    public Guid JobId { get; set; }
    public JobEntity Job { get; set; } = null!;
    public string AgentId { get; set; } = string.Empty;
    public int TtlSeconds { get; set; } = 90;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public int LastAckedSequence { get; set; }
    public string State { get; set; } = "Assigned";
}
