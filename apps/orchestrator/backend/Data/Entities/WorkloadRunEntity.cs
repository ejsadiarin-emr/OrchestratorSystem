namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class WorkloadRunEntity
{
    public Guid WorkloadRunRecordId { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; } = Guid.NewGuid();
    public Guid WorkloadId { get; set; }
    public WorkloadDefinitionEntity Workload { get; set; } = null!;
    public Guid RevisionId { get; set; }
    public WorkloadRevisionEntity Revision { get; set; } = null!;
    public Guid? NodeId { get; set; }
    public NodeEntity? Node { get; set; } = null!;
    public string NodeDisplayName { get; set; } = string.Empty;
    public string Mode { get; set; } = "install";
    public string State { get; set; } = "Queued";
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyRequestHash { get; set; }
    public string? CancelReason { get; set; }
    public string? RiskLevel { get; set; }
    public string? RevisionSnapshotJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public bool ForceInstall { get; set; }
    public string? ReportText { get; set; }
}
