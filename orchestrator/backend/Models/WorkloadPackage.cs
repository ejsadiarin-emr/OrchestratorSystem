namespace Orchestrator.Models;

public class WorkloadPackage
{
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadVersion { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string? PreInitSteps { get; set; }
    public string? PostInitSteps { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Hash { get; set; }
    public string UpdateStrategy { get; set; } = "reinstall";
    public Workload Workload { get; set; } = null!;
    public Artifact? Package { get; set; }
}
