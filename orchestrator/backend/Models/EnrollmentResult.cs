namespace Orchestrator.Models;

public class EnrollmentResult
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentSecret { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; }
}
