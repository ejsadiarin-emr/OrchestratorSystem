using Microsoft.Extensions.Options;
using Orchestrator.Configuration;
using Orchestrator.Models;
using System.Text.Json;

namespace Orchestrator.Services;

public class WorkloadWatcherService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkloadOptions _options;
    private readonly ILogger<WorkloadWatcherService> _logger;
    private FileSystemWatcher? _watcher;

    public WorkloadWatcherService(
        IServiceProvider serviceProvider,
        IOptions<WorkloadOptions> options,
        ILogger<WorkloadWatcherService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = Path.GetFullPath(_options.Path);
        Directory.CreateDirectory(path);

        // Initial scan: process all existing .json files
        foreach (var file in Directory.GetFiles(path, "*.json"))
        {
            await ProcessFileAsync(file);
        }

        if (!_options.WatchForChanges)
        {
            _logger.LogInformation("Workload file watching is disabled.");
            return;
        }

        _watcher = new FileSystemWatcher(path, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += async (sender, e) => await OnFileChangedAsync(e.FullPath);
        _watcher.Changed += async (sender, e) => await OnFileChangedAsync(e.FullPath);

        _logger.LogInformation("Watching for workload definition changes at {Path}", path);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnFileChangedAsync(string filePath)
    {
        // Debounce: wait a moment to ensure file is fully written
        await Task.Delay(500);
        await ProcessFileAsync(filePath);
    }

    private async Task ProcessFileAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("Processing workload definition: {FilePath}", filePath);
            var json = await File.ReadAllTextAsync(filePath);
            var dto = JsonSerializer.Deserialize<WorkloadDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null)
            {
                _logger.LogWarning("Failed to deserialize workload definition: {FilePath}", filePath);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var workloadService = scope.ServiceProvider.GetRequiredService<IWorkloadService>();
            await workloadService.UpsertAsync(dto);

            _logger.LogInformation("Upserted workload: {WorkloadId} v{Version}", dto.WorkloadId, dto.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing workload definition: {FilePath}", filePath);
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
