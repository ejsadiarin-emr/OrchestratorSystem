using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api.Workloads;

public sealed class CreateWorkloadRevisionRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
    public string Version { get; set; } = string.Empty;

    [Required]
    public List<WorkloadPackageInput> Packages { get; set; } = new();

    public List<string>? PreWorkloadSteps { get; set; }

    public List<string>? PostWorkloadSteps { get; set; }

    public string? DefaultShell { get; set; }
}

public sealed class WorkloadPackageInput
{
    [Required]
    public Guid PackageId { get; set; }

    [Range(1, int.MaxValue)]
    public int PackageIndex { get; set; }

    public List<string>? PreInitSteps { get; set; }

    public List<string>? PostInitSteps { get; set; }
}
