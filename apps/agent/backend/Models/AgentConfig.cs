namespace DeploymentPoC.Agent.Models;

public class AgentConfig
{
    public Guid NodeId { get; set; }
    public string OrchestratorUrl { get; set; } = string.Empty;
}
