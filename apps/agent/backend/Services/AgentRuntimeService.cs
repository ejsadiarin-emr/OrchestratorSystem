using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Agent.Services;

public sealed class AgentRuntimeService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentRuntimeService> _logger;
    private readonly PipelineExecutor _pipelineExecutor;
    private readonly IHubConnectionFactory _hubConnectionFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private IHubConnection? _connection;
    private Timer? _heartbeatTimer;
    private Guid? _nodeId;
    private readonly SemaphoreSlim _pipelineLock = new(1, 1);
    private readonly CancellationTokenSource _pipelineWatchdogCts = new();

    public AgentRuntimeService(
        IConfiguration configuration,
        ILogger<AgentRuntimeService> logger,
        PipelineExecutor pipelineExecutor,
        IHubConnectionFactory hubConnectionFactory,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
        _hubConnectionFactory = hubConnectionFactory ?? throw new ArgumentNullException(nameof(hubConnectionFactory));
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

        // TODO: Remove SignalR connection code after polling is validated
        /*
        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/agent";
        _logger.LogInformation("AgentRuntimeService starting. Connecting to orchestrator at {HubUrl}", hubUrl);

        _connection = _hubConnectionFactory.Create(hubUrl);

        _connection.On<MessageEnvelope>(MessageTypes.AssignRun, async envelope =>
        {
            _logger.LogInformation("[INSTRUMENTATION] Received AssignRun envelope: RunId={RunId}, AgentId={AgentId}, MessageType={MessageType}", envelope.RunId, envelope.AgentId, envelope.MessageType);
            await HandleAssignRunAsync(envelope);
        });

        _connection.Reconnecting += ex =>
        {
            _logger.LogWarning(ex, "SignalR connection reconnecting");
            return Task.CompletedTask;
        };

        _connection.Closed += ex =>
        {
            if (ex is not null)
            {
                _logger.LogError(ex, "SignalR connection closed with error");
            }
            else
            {
                _logger.LogWarning("SignalR connection closed");
            }
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR connection reconnected. ConnectionId={ConnectionId}", connectionId);
            if (_nodeId.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _connection!.InvokeAsync("Identify", _nodeId.Value, stoppingToken);
                        _logger.LogInformation("Re-identified with orchestrator as NodeId={NodeId}", _nodeId.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to re-identify with orchestrator after reconnect");
                    }
                });
            }
            return Task.CompletedTask;
        };

        await _connection.StartAsync(stoppingToken);
        _logger.LogInformation("Connected to orchestrator SignalR hub");

        try
        {
            await _connection.InvokeAsync("Identify", nodeId, stoppingToken);
            _logger.LogInformation("Identified with orchestrator as NodeId={NodeId}", nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to identify with orchestrator");
        }

        var intervalSeconds = _configuration.GetValue<double?>("Agent:HeartbeatIntervalSeconds") ?? 15.0;
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        _heartbeatTimer = new Timer(
            async _ => await SendHeartbeatAsync(),
            null,
            interval,
            interval);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        finally
        {
            _heartbeatTimer?.Dispose();
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
        */

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
                            new { status = result.Success ? "Completed" : "Failed", error = result.Error },
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Pipeline failed for RunId={RunId}", run.RunId);
                        try
                        {
                            await http.PatchAsJsonAsync(
                                $"/api/workload-runs/{run.RunId}?agent_id={nodeId}",
                                new { status = "Failed", error = ex.Message }, CancellationToken.None);
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

    private async Task SendHeartbeatAsync()
    {
        if (_connection?.State != HubConnectionState.Connected || !_nodeId.HasValue)
        {
            return;
        }

        var heartbeat = new MessageEnvelope
        {
            MessageType = MessageTypes.LeaseHeartbeat,
            ProtocolVersion = "1.0",
            MessageId = Guid.NewGuid().ToString(),
            TimestampUtc = DateTime.UtcNow,
            AgentId = _nodeId.Value.ToString(),
            Sequence = 0,
            Payload = new HeartbeatPayload
            {
                OsVersion = RuntimeInformation.OSDescription,
                AgentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
            }
        };

        try
        {
            await _connection.InvokeAsync("SendMessage", heartbeat, CancellationToken.None);
            _logger.LogDebug("Sent LeaseHeartbeat");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send heartbeat");
        }
    }

    private async Task HandleAssignRunAsync(MessageEnvelope envelope)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(envelope.RunId))
            {
                throw new InvalidOperationException("AssignRun message missing required RunId");
            }

            var payload = ParseAssignRunPayload(envelope.Payload);

            // Filter: only process runs assigned to this node
            var nodeIdString = _configuration["Agent:NodeId"];
            if (!string.IsNullOrWhiteSpace(nodeIdString) && Guid.TryParse(nodeIdString, out var nodeId))
            {
                if (payload.NodeId != nodeId)
                {
                    _logger.LogWarning("Ignoring AssignRun for NodeId={TargetNodeId}, we are {OurNodeId}", payload.NodeId, nodeId);
                    return;
                }
            }

            _logger.LogInformation(
                "Received AssignRun: Workload={WorkloadName}, Packages={PackageCount}, RunId={RunId}",
                payload.WorkloadName,
                payload.Packages.Count,
                envelope.RunId);

            var ack = new MessageEnvelope
            {
                MessageType = MessageTypes.AckClaim,
                RunId = envelope.RunId,
                AgentId = envelope.AgentId,
                Sequence = envelope.Sequence
            };

            if (_connection?.State == HubConnectionState.Connected)
            {
                await _connection.InvokeAsync("SendMessage", ack, CancellationToken.None);
                _logger.LogInformation("Sent AckClaim for RunId={RunId}", envelope.RunId);
            }

            // Execute workload pipeline
            var baseUrl = _configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5000";
            var context = new PipelineContext
            {
                Payload = payload,
                CurrentPackages = payload.CurrentPackages,
                OrchestratorBaseUrl = baseUrl,
                AgentId = envelope.AgentId ?? "unknown",
                RunId = envelope.RunId ?? payload.RunId.ToString(),
                Sequence = envelope.Sequence,
                ForceInstall = payload.ForceInstall
            };

            var result = await _pipelineExecutor.ExecuteAsync(
                context,
                async (msg, ct) =>
                {
                    if (_connection?.State == HubConnectionState.Connected)
                    {
                        await _connection.InvokeAsync("SendMessage", msg, ct);
                    }
                },
                CancellationToken.None);

            _logger.LogInformation(
                "Pipeline completed: RunId={RunId}, Success={Success}, StepsExecuted={StepsExecuted}",
                envelope.RunId,
                result.Success,
                result.StepsExecuted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle AssignRun for RunId={RunId}", envelope.RunId);
        }
    }

    public static AssignRunPayload ParseAssignRunPayload(object payload)
    {
        if (payload is AssignRunPayload typedPayload)
            return typedPayload;

        if (payload is JsonElement jsonElement)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<AssignRunPayload>(jsonElement.GetRawText(), options)
                ?? throw new InvalidOperationException("Failed to deserialize AssignRunPayload from JsonElement");
        }

        throw new InvalidOperationException($"Unexpected payload type: {payload.GetType().Name}");
    }

    public override void Dispose()
    {
        _pipelineWatchdogCts.Cancel();
        _pipelineWatchdogCts.Dispose();
        _pipelineLock.Dispose();
        base.Dispose();
    }
}

public sealed class HeartbeatPayload
{
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
}
