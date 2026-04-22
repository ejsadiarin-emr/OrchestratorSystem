using System.Text.Json;
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
    private HubConnection? _connection;

    public AgentRuntimeService(IConfiguration configuration, ILogger<AgentRuntimeService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = _configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5000";
        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/agent";

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
            var payload = ParseAssignRunPayload(envelope.Payload);
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
