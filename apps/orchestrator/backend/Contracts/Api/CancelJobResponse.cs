namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CancelJobResponse
{
    public Guid JobId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CancelledAtUtc { get; set; }
}
