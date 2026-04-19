namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class JobDetailResponse
{
    public Guid JobId { get; set; }
    public string State { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int? ReasonCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
