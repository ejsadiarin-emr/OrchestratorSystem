using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api")]
public class EnrollmentController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly ILogger<EnrollmentController> _logger;

    public EnrollmentController(InstallerDbContext db, ILogger<EnrollmentController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("nodes/enroll")]
    public async Task<ActionResult<EnrollmentTokenResponse>> IssueToken([FromBody] IssueEnrollmentTokenRequest request)
    {
        if (request.TtlMinutes < 1 || request.TtlMinutes > 120)
        {
            return BadRequest(new { message = "TTL must be between 1 and 120 minutes." });
        }

        var tokenValue = $"enroll-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        var entity = new EnrollmentTokenEntity
        {
            Token = tokenValue,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(request.TtlMinutes),
            RequestedBy = request.RequestedBy,
            OrchestratorUrl = request.OrchestratorUrl,
            SingleUse = true,
            Used = false
        };

        _db.EnrollmentTokens.Add(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Issued enrollment token {Token} by {RequestedBy}, expires at {ExpiresAt}",
            entity.Token, entity.RequestedBy, entity.ExpiresAtUtc);

        return Ok(MapToResponse(entity));
    }

    [HttpGet("enrollment-tokens")]
    public async Task<ActionResult<List<EnrollmentTokenResponse>>> ListTokens()
    {
        var tokens = await _db.EnrollmentTokens
            .OrderByDescending(t => t.IssuedAtUtc)
            .ToListAsync();

        return Ok(tokens.Select(MapToResponse).ToList());
    }

    [HttpPost("enrollment-tokens/{token}/consume")]
    public async Task<ActionResult<Node>> ConsumeToken(string token, [FromBody] ConsumeEnrollmentTokenRequest? request)
    {
        var entity = await _db.EnrollmentTokens.SingleOrDefaultAsync(t => t.Token == token);
        if (entity is null)
        {
            return NotFound(new { message = "Enrollment token not found." });
        }

        if (entity.Used)
        {
            return Conflict(new { message = "Enrollment token already consumed." });
        }

        if (entity.ExpiresAtUtc < DateTime.UtcNow)
        {
            return StatusCode(StatusCodes.Status410Gone, new { message = "Enrollment token expired." });
        }

        entity.Used = true;
        entity.ConsumedAtUtc = DateTime.UtcNow;

        var hostname = !string.IsNullOrWhiteSpace(request?.Hostname)
            ? request.Hostname
            : $"auto-node-{Guid.NewGuid().ToString("N")[..8]}";

        var displayName = !string.IsNullOrWhiteSpace(request?.DisplayName)
            ? request.DisplayName
            : hostname;

        var ipAddress = !string.IsNullOrWhiteSpace(request?.IpAddress)
            ? request.IpAddress
            : (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0");

        var nodeEntity = new NodeEntity
        {
            NodeId = Guid.NewGuid(),
            Hostname = hostname,
            DisplayName = displayName,
            IpAddress = ipAddress,
            Description = "Enrolled via token",
            Status = "Online",
            LastSeenUtc = DateTime.UtcNow,
            FirstConnectedUtc = DateTime.UtcNow,
            OsVersion = request?.OsVersion ?? "",
            AgentVersion = request?.AgentVersion ?? "",
        };

        _db.Nodes.Add(nodeEntity);
        entity.ConsumedByNodeId = nodeEntity.NodeId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Node {Hostname} enrolled with token {Token}", nodeEntity.Hostname, token);

        return Ok(new Node
        {
            Id = nodeEntity.NodeId,
            Hostname = nodeEntity.Hostname,
            DisplayName = nodeEntity.DisplayName,
            IpAddress = nodeEntity.IpAddress,
            Status = nodeEntity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = nodeEntity.LastSeenUtc,
            FirstConnectedAt = nodeEntity.FirstConnectedUtc,
            Description = nodeEntity.Description,
            OsVersion = nodeEntity.OsVersion,
            AgentVersion = nodeEntity.AgentVersion,
        });
    }

    private static EnrollmentTokenResponse MapToResponse(EnrollmentTokenEntity entity)
    {
        return new EnrollmentTokenResponse
        {
            Token = entity.Token,
            IssuedAt = entity.IssuedAtUtc,
            ExpiresAt = entity.ExpiresAtUtc,
            RequestedBy = entity.RequestedBy,
            OrchestratorUrl = entity.OrchestratorUrl,
            SingleUse = entity.SingleUse,
            Used = entity.Used
        };
    }
}
