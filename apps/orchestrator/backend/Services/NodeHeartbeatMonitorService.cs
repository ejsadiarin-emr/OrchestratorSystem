using DeploymentPoC.Orchestrator.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class NodeHeartbeatMonitorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NodeHeartbeatMonitorService> _logger;

    public NodeHeartbeatMonitorService(IServiceProvider serviceProvider, ILogger<NodeHeartbeatMonitorService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var staleCount = await db.Nodes
            .CountAsync(n => n.LastSeenUtc < cutoff, cancellationToken);

        if (staleCount > 0)
        {
            _logger.LogInformation("Detected {Count} nodes past heartbeat timeout", staleCount);
        }
    }
}
