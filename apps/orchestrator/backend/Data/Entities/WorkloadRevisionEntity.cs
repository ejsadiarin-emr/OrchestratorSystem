namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class WorkloadRevisionEntity
{
    public Guid RevisionId { get; set; } = Guid.NewGuid();
    public Guid WorkloadId { get; set; }
    public WorkloadDefinitionEntity Workload { get; set; } = null!;
    public string Version { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string PreWorkloadStepsJson { get; set; } = "[]";
    public string PostWorkloadStepsJson { get; set; } = "[]";
    public string PreUninstallStepsJson { get; set; } = "[]";
    public string PostUninstallStepsJson { get; set; } = "[]";
    public string DefaultShell { get; set; } = "powershell";
    public List<WorkloadPackageEntity> Packages { get; set; } = new();
}
