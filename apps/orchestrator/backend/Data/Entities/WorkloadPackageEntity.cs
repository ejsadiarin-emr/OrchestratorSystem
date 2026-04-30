namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class WorkloadPackageEntity
{
    public Guid WorkloadPackageId { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public WorkloadRevisionEntity Revision { get; set; } = null!;
    public Guid PackageId { get; set; }
    public int PackageIndex { get; set; }
    public string PreInitStepsJson { get; set; } = "[]";
    public string PostInitStepsJson { get; set; } = "[]";
}
