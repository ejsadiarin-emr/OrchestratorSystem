namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class JobEntity
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public string Mode { get; set; } = "install";
    public string State { get; set; } = "Queued";
    public int? ReasonCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string ManifestPackageId { get; set; } = string.Empty;
    public string ManifestTargetVersion { get; set; } = string.Empty;
    public string TargetNodeIdsCsv { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyRequestHash { get; set; }
    public string? CancelReason { get; set; }
    public List<JobStepEntity> Steps { get; set; } = new();
    public List<AssignmentLeaseEntity> AssignmentLeases { get; set; } = new();
    public List<ConfigSnapshotEntity> ConfigSnapshots { get; set; } = new();
}
