namespace Agent.Services;

public class AgentPollingService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // placeholder — implemented in Phase 2
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
