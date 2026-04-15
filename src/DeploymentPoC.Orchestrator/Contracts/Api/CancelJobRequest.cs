using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CancelJobRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(512, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}
