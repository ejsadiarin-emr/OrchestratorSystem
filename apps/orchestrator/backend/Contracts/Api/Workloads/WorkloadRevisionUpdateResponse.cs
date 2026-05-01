namespace DeploymentPoC.Orchestrator.Contracts.Api.Workloads;

public sealed class WorkloadRevisionUpdateResponse
{
    public bool Changed { get; set; }
    public WorkloadRevisionDto Revision { get; set; } = null!;
}
