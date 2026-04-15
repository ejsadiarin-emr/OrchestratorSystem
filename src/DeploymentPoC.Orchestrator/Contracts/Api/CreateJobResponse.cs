namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CreateJobResponse
{
    public Guid JobId { get; set; }
    public string State { get; set; } = string.Empty;
}
