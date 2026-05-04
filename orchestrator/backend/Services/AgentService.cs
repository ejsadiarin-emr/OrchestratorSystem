using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;

namespace Orchestrator.Services;

public class AgentService : IAgentService
{
    private readonly AppDbContext _dbContext;

    public AgentService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<AgentNode>> GetAllAgentsAsync()
    {
        return await _dbContext.AgentNodes
            .Include(a => a.InstalledPackages)
            .OrderByDescending(a => a.RegisteredAt)
            .ToListAsync();
    }

    public async Task<AgentNode?> GetAgentByIdAsync(string agentId)
    {
        return await _dbContext.AgentNodes
            .Include(a => a.InstalledPackages)
            .FirstOrDefaultAsync(a => a.AgentId == agentId);
    }

    public async Task UpdateLastSeenAsync(string agentId)
    {
        var agent = await _dbContext.AgentNodes
            .FirstOrDefaultAsync(a => a.AgentId == agentId);

        if (agent != null)
        {
            agent.LastSeenAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }
}
