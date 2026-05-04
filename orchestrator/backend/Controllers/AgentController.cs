using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;
using Orchestrator.Services;

namespace Orchestrator.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly AppDbContext _dbContext;

    public AgentController(IAgentService agentService, AppDbContext dbContext)
    {
        _agentService = agentService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var agents = await _agentService.GetAllAgentsAsync();
        return Ok(agents.Select(a => new
        {
            a.Id,
            a.AgentId,
            a.Hostname,
            a.IpAddress,
            a.Status,
            a.LastSeenAt,
            a.RegisteredAt,
            a.PollingIntervalSeconds,
            a.AssignedWorkloadId
        }));
    }

    [HttpGet("{agentId}")]
    public async Task<ActionResult<object>> GetById(string agentId)
    {
        var agent = await _agentService.GetAgentByIdAsync(agentId);
        if (agent == null)
            return NotFound();

        return Ok(new
        {
            agent.Id,
            agent.AgentId,
            agent.Hostname,
            agent.IpAddress,
            agent.Status,
            agent.LastSeenAt,
            agent.RegisteredAt,
            agent.PollingIntervalSeconds,
            agent.AssignedWorkloadId
        });
    }

    [HttpPost("{agentId}/heartbeat")]
    public async Task<ActionResult> Heartbeat(string agentId)
    {
        await _agentService.UpdateLastSeenAsync(agentId);
        var agent = await _agentService.GetAgentByIdAsync(agentId);
        if (agent == null)
            return NotFound();

        return Ok(new
        {
            agent.AssignedWorkloadId,
            agent.AssignedWorkloadVersion
        });
    }
}
