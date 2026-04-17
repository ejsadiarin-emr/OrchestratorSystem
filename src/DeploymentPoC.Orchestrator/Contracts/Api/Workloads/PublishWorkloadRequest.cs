using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Contracts.Api.Workloads;

public sealed class PublishWorkloadRequest
{
    [Required]
    public Guid RevisionId { get; set; }
}
