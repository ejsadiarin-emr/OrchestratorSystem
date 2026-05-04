using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;

namespace Orchestrator.Middleware;

public class AgentAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AgentAuthMiddleware> _logger;

    public AgentAuthMiddleware(RequestDelegate next, ILogger<AgentAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        var path = context.Request.Path.Value;

        // only protect agent-specific endpoints (not enroll)
        if (path != null && path.StartsWith("/api/agents/") && !path.StartsWith("/api/agents/enroll"))
        {
            var agentId = context.Request.RouteValues["agentId"] as string;
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                _logger.LogWarning("Agent request missing credentials for {Path}", path);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid Authorization header." });
                return;
            }

            var secret = authHeader.Substring("Bearer ".Length).Trim();
            var agent = await dbContext.AgentNodes
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AgentId == agentId && a.AgentSecret == secret);

            if (agent == null)
            {
                _logger.LogWarning("Agent authentication failed for {AgentId}", agentId);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid agent credentials." });
                return;
            }
        }

        await _next(context);
    }
}
