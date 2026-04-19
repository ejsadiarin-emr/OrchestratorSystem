using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CreateJobRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(128)]
    public string PackageId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
    public string TargetVersion { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(32)]
    public string ExecutionMode { get; set; } = "install";

    [Required(AllowEmptyStrings = false)]
    [StringLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<Guid> Targets { get; set; } = new();
}
