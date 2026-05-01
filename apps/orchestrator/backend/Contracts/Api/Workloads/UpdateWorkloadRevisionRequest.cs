using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api.Workloads;

public sealed class UpdateWorkloadRevisionRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
    public string Version { get; set; } = string.Empty;

    [Required]
    public List<WorkloadPackageInput> Packages { get; set; } = new();

    public List<string>? PreWorkloadSteps { get; set; }

    public List<string>? PostWorkloadSteps { get; set; }

    public List<string>? PreUninstallSteps { get; set; }

    public List<string>? PostUninstallSteps { get; set; }

    public string? DefaultShell { get; set; }
}
