namespace Orchestrator.Configuration;

public class AgentOptions
{
    public int DefaultPollingIntervalSeconds { get; set; } = 30;
    public int LostThresholdMultiplier { get; set; } = 3;
}
