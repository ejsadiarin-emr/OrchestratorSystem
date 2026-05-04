namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class NodeWorkloadStateEntity
{
    public Guid NodeWorkloadStateId { get; set; } = Guid.NewGuid();
    public Guid NodeId { get; set; }
    public NodeEntity Node { get; set; } = null!;
    public Guid WorkloadId { get; set; }
    public WorkloadDefinitionEntity Workload { get; set; } = null!;
    public Guid? CurrentRevisionId { get; set; }
    public WorkloadRevisionEntity? CurrentRevision { get; set; }
    public string PackageStatesJson { get; set; } = "{}";
    public string Status { get; set; } = "Unknown";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
