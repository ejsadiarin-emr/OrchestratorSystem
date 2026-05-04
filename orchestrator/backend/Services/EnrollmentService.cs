using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orchestrator.Configuration;
using Orchestrator.Models;

namespace Orchestrator.Services;

public class EnrollmentService : IEnrollmentService
{
    private readonly AppDbContext _dbContext;
    private readonly EnrollmentOptions _options;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        AppDbContext dbContext,
        IOptions<EnrollmentOptions> options,
        IOptions<AgentOptions> agentOptions,
        ILogger<EnrollmentService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    public async Task<EnrollmentToken> GenerateTokenAsync()
    {
        var token = new EnrollmentToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddHours(_options.TokenTtlHours),
            Used = false
        };

        _dbContext.EnrollmentTokens.Add(token);
        await _dbContext.SaveChangesAsync();
        return token;
    }

    public async Task<EnrollmentResult> EnrollAsync(string token, string hostname, string ipAddress)
    {
        var enrollmentToken = await _dbContext.EnrollmentTokens
            .FirstOrDefaultAsync(t => t.Token == token && !t.Used && t.ExpiresAt > DateTime.UtcNow);

        if (enrollmentToken == null)
        {
            throw new InvalidOperationException("Invalid or expired enrollment token.");
        }

        var agentId = Guid.NewGuid().ToString("N");
        var agentSecret = Guid.NewGuid().ToString("N");

        var agent = new AgentNode
        {
            AgentId = agentId,
            Hostname = hostname,
            IpAddress = ipAddress,
            AgentSecret = agentSecret,
            LastSeenAt = DateTime.UtcNow,
            Status = AgentNodeStatus.REGISTERED,
            PollingIntervalSeconds = _agentOptions.DefaultPollingIntervalSeconds
        };

        _dbContext.AgentNodes.Add(agent);

        enrollmentToken.Used = true;
        enrollmentToken.UsedAt = DateTime.UtcNow;
        enrollmentToken.UsedByAgentId = agentId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Agent {AgentId} enrolled successfully from host {Hostname}", agentId, hostname);

        return new EnrollmentResult
        {
            AgentId = agentId,
            AgentSecret = agentSecret,
            PollingIntervalSeconds = agent.PollingIntervalSeconds
        };
    }

    public async Task UnregisterAsync(string agentId, string agentSecret)
    {
        var agent = await _dbContext.AgentNodes
            .FirstOrDefaultAsync(a => a.AgentId == agentId && a.AgentSecret == agentSecret);

        if (agent == null)
        {
            throw new InvalidOperationException("Agent not found or invalid credentials.");
        }

        agent.Status = AgentNodeStatus.UNREGISTERED;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Agent {AgentId} unregistered successfully", agentId);
    }
}
