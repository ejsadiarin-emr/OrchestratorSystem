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

    [HttpPost("tokens")]
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
}
