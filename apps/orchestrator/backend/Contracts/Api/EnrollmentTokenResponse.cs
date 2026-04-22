namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class IssueEnrollmentTokenRequest
{
    public string RequestedBy { get; set; } = string.Empty;
    public string OrchestratorUrl { get; set; } = string.Empty;
    public int TtlMinutes { get; set; } = 20;
}

public sealed class EnrollmentTokenResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string OrchestratorUrl { get; set; } = string.Empty;
    public bool SingleUse { get; set; } = true;
    public bool Used { get; set; } = false;
}

public sealed class EnrollmentTokenListResponse
{
    public List<EnrollmentTokenResponse> Tokens { get; set; } = new();
}

public sealed class ConsumeEnrollmentTokenRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
}
