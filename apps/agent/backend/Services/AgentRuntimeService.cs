using System.Net.Http.Json;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Agent.Services;

public sealed class AgentRuntimeService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentRuntimeService> _logger;
    private readonly PipelineExecutor _pipelineExecutor;
    private readonly IHttpClientFactory _httpClientFactory;
    private Guid? _nodeId;
    private readonly SemaphoreSlim _pipelineLock = new(1, 1);
    private readonly CancellationTokenSource _pipelineWatchdogCts = new();

    public AgentRuntimeService(
        IConfiguration configuration,
        ILogger<AgentRuntimeService> logger,
        PipelineExecutor pipelineExecutor,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = _configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5000";
        var nodeIdString = _configuration["Agent:NodeId"];

        if (!Guid.TryParse(nodeIdString, out var nodeId))
        {
            _logger.LogError("Agent:NodeId is not configured or invalid. Polling loop cannot start.");
            return;
        }

        _nodeId = nodeId;
        _logger.LogInformation("Agent polling loop starting. NodeId={NodeId}, Orchestrator={BaseUrl}", nodeId, baseUrl);

        var http = _httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(baseUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(http, nodeId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Poll cycle failed — will retry");
            }

            var pollIntervalSeconds = _configuration.GetValue<double?>("Agent:PollIntervalSeconds") ?? 10.0;
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollAndProcessAsync(HttpClient http, Guid nodeId, CancellationToken ct)
    {
        var runs = await http.GetFromJsonAsync<List<PendingWorkloadRunResponse>>(
            $"/api/workload-runs/pending?agent_id={nodeId}", ct);

        foreach (var run in runs ?? [])
        {
            // Skip claiming if a pipeline is already running — don't skip polling
            if (!await _pipelineLock.WaitAsync(0))
            {
                _logger.LogInformation("Pipeline already running — deferring run {RunId}", run.RunId);
                break; // Don't claim; next poll cycle will re-fetch this run
            }

            try
            {
                // Atomic claim
                var claimResponse = await http.PatchAsJsonAsync(
                    $"/api/workload-runs/{run.RunId}?agent_id={nodeId}",
                    new { status = "Running" }, ct);

                if (!claimResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to claim run {RunId}. Status={Status}", run.RunId, claimResponse.StatusCode);
                    _pipelineLock.Release(); // Release if claim failed — allow retry on next cycle
                    continue;
                }

                // Build PipelineContext from PendingWorkloadRunResponse
                static PackageAssignment MapPackage(PendingPackageDto p) => new()
                {
                    PackageEntityId = p.PackageEntityId,
                    PackageId = p.PackageEntityId.ToString(),
                    Name = p.Name,
                    Version = p.Version,
                    ArtifactFileName = p.Filename,
                    DownloadUrl = p.DownloadUrl,
                    ExpectedSha256 = p.ExpectedSha256,
                    InstallAdapter = p.InstallAdapter,
                    Detection = p.Detection
                };

                var targetPackages = run.Packages.Select(MapPackage).ToList();
                var currentPackages = run.CurrentPackages.Select(MapPackage).ToList();

                var context = new PipelineContext
                {
                    Payload = new AssignRunPayload
                    {
                        RunId = run.RunId,
                        WorkloadId = run.WorkloadId,
                        WorkloadName = run.WorkloadName,
                        Mode = run.Mode,
                        NodeId = nodeId,
                        Packages = targetPackages,
                        CurrentPackages = currentPackages
                    },
                    CurrentPackages = currentPackages,
                    OrchestratorBaseUrl = http.BaseAddress?.ToString().TrimEnd('/') ?? "",
                    AgentId = nodeId.ToString(),
                    RunId = run.RunId.ToString(),
                    Sequence = 0,
                    ForceInstall = false
                };

                var pipelineTimeoutMinutes = _configuration.GetValue<double?>("Agent:PipelineTimeoutMinutes") ?? 30;
                var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pipelineCts.CancelAfter(TimeSpan.FromMinutes(pipelineTimeoutMinutes));

                // Fire and forget — semaphore is released in finally block
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await _pipelineExecutor.ExecuteAsync(
                            context,
                            async (msg, msgCt) =>
                            {
                                if (msg.Payload is StepStatusPayload stepPayload)
                                {
                                    await http.PostAsJsonAsync(
                                        $"/api/workload-runs/{run.RunId}/timeline?agent_id={nodeId}",
                                        new
                                        {
                                            step = stepPayload.StepName,
                                            status = stepPayload.Status,
                                            message = stepPayload.Error
                                        }, msgCt);
                                }
                                else if (msg.Payload is FinalizationPayload finalPayload)
                                {
                                    await http.PostAsJsonAsync(
                                        $"/api/workload-runs/{run.RunId}/timeline?agent_id={nodeId}",
                                        new
                                        {
                                            step = "Finalization",
                                            status = finalPayload.Result,
                                            message = finalPayload.Error
                                        }, msgCt);
                                }
                            },
                            pipelineCts.Token);

                        _logger.LogInformation("Pipeline completed: RunId={RunId}, Success={Success}, Error={Error}", run.RunId, result.Success, result.Error);

                        // Report final status (same as before)
                        await http.PatchAsJsonAsync(
                            $"/api/workload-runs/{run.RunId}?agent_id={nodeId}",
                            new { status = result.Success ? "Completed" : "Failed", error = result.Error, report = result.Report },
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Pipeline failed for RunId={RunId}", run.RunId);
                        try
                        {
                            await http.PatchAsJsonAsync(
                                $"/api/workload-runs/{run.RunId}?agent_id={nodeId}",
                                new { status = "Failed", error = ex.Message, report = (string?)null }, CancellationToken.None);
                        }
                        catch { /* best effort */ }
                    }
                    finally
                    {
                        _pipelineLock.Release();
                        pipelineCts.Dispose();
                    }
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _pipelineLock.Release(); // Release on unexpected errors before pipeline starts
                throw;
            }
        }
    }

    public override void Dispose()
    {
        _pipelineWatchdogCts.Cancel();
        _pipelineWatchdogCts.Dispose();
        _pipelineLock.Dispose();
        base.Dispose();
    }
}

