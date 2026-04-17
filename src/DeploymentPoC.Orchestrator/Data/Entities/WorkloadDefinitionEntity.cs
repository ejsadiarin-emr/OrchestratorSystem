namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class WorkloadDefinitionEntity
{
    public Guid WorkloadId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public WorkloadRevisionEntity? PublishedRevision { get; set; }
    public List<WorkloadRevisionEntity> Revisions { get; set; } = new();
    public List<WorkloadRunEntity> Runs { get; set; } = new();
    public List<NodeWorkloadStateEntity> NodeStates { get; set; } = new();
}
