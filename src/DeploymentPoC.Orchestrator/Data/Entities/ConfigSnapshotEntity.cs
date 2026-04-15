namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class ConfigSnapshotEntity
{
    public Guid ConfigSnapshotId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public JobEntity Job { get; set; } = null!;
    public Guid NodeId { get; set; }
    public NodeEntity Node { get; set; } = null!;
    public string PackageId { get; set; } = string.Empty;
    public string SourceSchemaVersion { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string StorageLocation { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;
}
