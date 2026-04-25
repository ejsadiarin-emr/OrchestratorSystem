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
    private IHubConnection? _connection;
    private Timer? _heartbeatTimer;
    private Guid? _nodeId;

    public AgentRuntimeService(
        IConfiguration configuration,
        ILogger<AgentRuntimeService> logger,
        PipelineExecutor pipelineExecutor,
        IHubConnectionFactory hubConnectionFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
        _hubConnectionFactory = hubConnectionFactory ?? throw new ArgumentNullException(nameof(hubConnectionFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = _configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5000";
        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/agent";
        var nodeIdString = _configuration["Agent:NodeId"];

        _logger.LogInformation("AgentRuntimeService starting. Connecting to orchestrator at {HubUrl}", hubUrl);

        _connection = _hubConnectionFactory.Create(hubUrl);

        _connection.On<MessageEnvelope>(MessageTypes.AssignRun, async envelope =>
        {
            await HandleAssignRunAsync(envelope);
        });

        _connection.Reconnecting += ex =>
        {
            _logger.LogWarning(ex, "SignalR connection reconnecting");
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

        // Identify with the orchestrator if we have a NodeId
        if (!string.IsNullOrWhiteSpace(nodeIdString) && Guid.TryParse(nodeIdString, out var nodeId))
        {
            _nodeId = nodeId;
            try
            {
                await _connection.InvokeAsync("Identify", nodeId, stoppingToken);
                _logger.LogInformation("Identified with orchestrator as NodeId={NodeId}", nodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to identify with orchestrator");
            }
        }
        else
        {
            _logger.LogWarning("No Agent:NodeId configured; cannot identify with orchestrator for targeted AssignRun delivery");
        }

        if (_nodeId.HasValue)
        {
            var intervalSeconds = _configuration.GetValue<double?>("Agent:HeartbeatIntervalSeconds") ?? 15.0;
            var interval = TimeSpan.FromSeconds(intervalSeconds);
            _heartbeatTimer = new Timer(
                async _ => await SendHeartbeatAsync(),
                null,
                interval,
                interval);
        }

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
                    _logger.LogDebug("Ignoring AssignRun for NodeId={TargetNodeId}, we are {OurNodeId}", payload.NodeId, nodeId);
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
                OrchestratorBaseUrl = baseUrl,
                AgentId = envelope.AgentId ?? "unknown",
                RunId = envelope.RunId ?? payload.RunId.ToString(),
                Sequence = envelope.Sequence
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
            return JsonSerializer.Deserialize<AssignRunPayload>(jsonElement.GetRawText())
                ?? throw new InvalidOperationException("Failed to deserialize AssignRunPayload from JsonElement");
        }

        throw new InvalidOperationException($"Unexpected payload type: {payload.GetType().Name}");
    }
}

public sealed class HeartbeatPayload
{
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
}
