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
    private HubConnection? _connection;

    public AgentRuntimeService(IConfiguration configuration, ILogger<AgentRuntimeService> logger, PipelineExecutor pipelineExecutor)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = _configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5000";
        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/agent";
        var nodeIdString = _configuration["Agent:NodeId"];

        _logger.LogInformation("AgentRuntimeService starting. Connecting to orchestrator at {HubUrl}", hubUrl);

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult("placeholder-token")!;
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<MessageEnvelope>(MessageTypes.AssignRun, async envelope =>
        {
            await HandleAssignRunAsync(envelope);
        });

        await _connection.StartAsync(stoppingToken);
        _logger.LogInformation("Connected to orchestrator SignalR hub");

        // Identify with the orchestrator if we have a NodeId
        if (!string.IsNullOrWhiteSpace(nodeIdString) && Guid.TryParse(nodeIdString, out var nodeId))
        {
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

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        finally
        {
            await _connection.DisposeAsync();
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
