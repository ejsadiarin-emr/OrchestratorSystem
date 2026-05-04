namespace Agent.Models;

public class AgentConfig
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentSecret { get; set; } = string.Empty;
    public string OrchestratorUrl { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 30;
}
