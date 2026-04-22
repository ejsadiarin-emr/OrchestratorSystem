namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class EnrollmentTokenEntity
{
    public Guid TokenId { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string OrchestratorUrl { get; set; } = string.Empty;
    public bool SingleUse { get; set; } = true;
    public bool Used { get; set; } = false;
    public DateTime? ConsumedAtUtc { get; set; }
    public Guid? ConsumedByNodeId { get; set; }
}
