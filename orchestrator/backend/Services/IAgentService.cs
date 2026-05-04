using Orchestrator.Models;

public interface IAgentService
{
    Task<IEnumerable<AgentNode>> GetAllAgentsAsync();
    Task<AgentNode?> GetAgentByIdAsync(string agentId);
    Task UpdateLastSeenAsync(string agentId);
}
