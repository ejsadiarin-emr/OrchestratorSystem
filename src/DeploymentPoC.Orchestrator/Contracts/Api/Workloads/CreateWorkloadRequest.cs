using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api.Workloads;

public sealed class CreateWorkloadRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; set; }
}
