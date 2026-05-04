using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;
using Orchestrator.Models.DTOs;
using Orchestrator.Services;

namespace Orchestrator.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly IEnrollmentService _enrollmentService;
    private readonly AppDbContext _dbContext;

    public AgentController(IAgentService agentService, IEnrollmentService enrollmentService, AppDbContext dbContext)
    {
        _agentService = agentService;
        _enrollmentService = enrollmentService;
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

    [HttpPost("enroll")]
    public async Task<ActionResult<object>> Enroll([FromBody] EnrollRequest request)
    {
        try
        {
            var result = await _enrollmentService.EnrollAsync(
                request.Token,
                request.Hostname,
                request.IpAddress);

            return Ok(new
            {
                agentId = result.AgentId,
                agentSecret = result.AgentSecret,
                pollingIntervalSeconds = result.PollingIntervalSeconds
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{agentId}/unregister")]
    public async Task<ActionResult> Unregister(string agentId, [FromBody] UnregisterRequest request)
    {
        try
        {
            if (agentId != request.AgentId)
            {
                return BadRequest(new { error = "Agent ID mismatch." });
            }

            await _enrollmentService.UnregisterAsync(request.AgentId, request.AgentSecret);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
