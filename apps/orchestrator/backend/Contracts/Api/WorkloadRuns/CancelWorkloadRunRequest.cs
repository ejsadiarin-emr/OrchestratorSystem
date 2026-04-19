using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api.WorkloadRuns;

public sealed class CancelWorkloadRunRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(512, MinimumLength = 2)]
    public string Reason { get; set; } = string.Empty;
}
