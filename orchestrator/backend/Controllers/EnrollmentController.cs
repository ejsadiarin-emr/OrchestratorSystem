using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;
using Orchestrator.Services;

namespace Orchestrator.Controllers;

[ApiController]
[Route("api/enrollment")]
public class EnrollmentController : ControllerBase
{
    private readonly IEnrollmentService _enrollmentService;
    private readonly AppDbContext _dbContext;

    public EnrollmentController(IEnrollmentService enrollmentService, AppDbContext dbContext)
    {
        _enrollmentService = enrollmentService;
        _dbContext = dbContext;
    }

    [HttpPost("token")]
    public async Task<ActionResult<object>> GenerateToken()
    {
        var token = await _enrollmentService.GenerateTokenAsync();
        return Ok(new
        {
            token = token.Token,
            expiresAt = token.ExpiresAt
        });
    }

    [HttpGet("tokens")]
    public async Task<ActionResult<IEnumerable<object>>> GetAllTokens()
    {
        var tokens = await _dbContext.EnrollmentTokens
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Token,
                t.CreatedAt,
                t.ExpiresAt,
                t.Used,
                t.UsedAt,
                t.UsedByAgentId
            })
            .ToListAsync();
        return Ok(tokens);
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
                agentSecret = result.AgentSecret
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("unregister")]
    public async Task<ActionResult> Unregister([FromBody] UnregisterRequest request)
    {
        try
        {
            await _enrollmentService.UnregisterAsync(request.AgentId, request.AgentSecret);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class EnrollRequest
{
    public string Token { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

public class UnregisterRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentSecret { get; set; } = string.Empty;
}
