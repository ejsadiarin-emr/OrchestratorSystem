namespace Orchestrator.Models;

public class WorkloadPackageDto
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<string>? PreInitSteps { get; set; }
    public List<string>? PostInitSteps { get; set; }
}

public class WorkloadDto
{
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<WorkloadPackageDto> Packages { get; set; } = [];
}
