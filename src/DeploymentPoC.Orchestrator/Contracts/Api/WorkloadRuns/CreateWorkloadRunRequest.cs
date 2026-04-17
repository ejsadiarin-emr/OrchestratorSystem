using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api.WorkloadRuns;

public sealed class CreateWorkloadRunRequest
{
    [Required]
    public Guid WorkloadId { get; set; }

    [Required]
    public Guid RevisionId { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(32)]
    public string Mode { get; set; } = "install";

    [Required(AllowEmptyStrings = false)]
    [StringLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<Guid> NodeIds { get; set; } = new();
}
