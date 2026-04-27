using System.Text.Json;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Services;
using DeploymentPoC.Contracts.Runtime;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class AgentRuntimeServiceTests
{
    [Test]
    [Ignore("SignalR replaced by HTTP polling")]
    public async Task ExecuteAsync_SendsLeaseHeartbeat_WithCorrectEnvelopeFields()
    {
        var nodeId = Guid.NewGuid();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:HeartbeatIntervalSeconds"] = "0.05"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var pipelineFake = new FakePipelineExecutor();
        var httpFactory = new FakeHttpClientFactory();

        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineFake, fakeFactory, httpFactory);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(0.2));

        try
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        var heartbeatInvocations = fakeConnection.Invocations
            .Where(i => i.Method == "SendMessage" && i.Arg is MessageEnvelope { MessageType: MessageTypes.LeaseHeartbeat })
            .ToList();

        Assert.That(heartbeatInvocations, Has.Count.GreaterThanOrEqualTo(1));

        var envelope = (MessageEnvelope)heartbeatInvocations[0].Arg!;
        Assert.That(envelope.MessageType, Is.EqualTo(MessageTypes.LeaseHeartbeat));
        Assert.That(envelope.ProtocolVersion, Is.EqualTo("1.0"));
        Assert.That(Guid.TryParse(envelope.MessageId, out _), Is.True);
        Assert.That(envelope.AgentId, Is.EqualTo(nodeId.ToString()));
        Assert.That(envelope.Sequence, Is.EqualTo(0));
        Assert.That(envelope.TimestampUtc, Is.GreaterThan(DateTime.UtcNow.AddSeconds(-5)));
    }

    [Test]
    [Ignore("SignalR replaced by HTTP polling")]
    public async Task ExecuteAsync_Reconnect_RaisesIdentify_AfterReconnectedEvent()
    {
        var nodeId = Guid.NewGuid();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:HeartbeatIntervalSeconds"] = "60"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var pipelineFake = new FakePipelineExecutor();
        var httpFactory = new FakeHttpClientFactory();

        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineFake, fakeFactory, httpFactory);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        var executeTask = service.StartAsync(cts.Token);

        await Task.Delay(50);
        await fakeConnection.RaiseReconnectedAsync("conn-123");
        await Task.Delay(200);

        try
        {
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        var identifyInvocations = fakeConnection.Invocations
            .Where(i => i.Method == "Identify")
            .ToList();

        Assert.That(identifyInvocations, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(identifyInvocations.Any(i => i.Arg is Guid g && g == nodeId), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotSendHeartbeat_WhenNodeIdIsMissing()
    {
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:HeartbeatIntervalSeconds"] = "0.05"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var pipelineFake = new FakePipelineExecutor();
        var httpFactory = new FakeHttpClientFactory();

        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineFake, fakeFactory, httpFactory);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(0.2));

        try
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        var heartbeatInvocations = fakeConnection.Invocations
            .Where(i => i.Arg is MessageEnvelope { MessageType: MessageTypes.LeaseHeartbeat })
            .ToList();

        Assert.That(heartbeatInvocations, Is.Empty);
    }

    [Test]
    [Ignore("SignalR replaced by HTTP polling")]
    public async Task ExecuteAsync_LogsReconnecting_WhenReconnectingEventFires()
    {
        var nodeId = Guid.NewGuid();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:HeartbeatIntervalSeconds"] = "60"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var pipelineFake = new FakePipelineExecutor();
        var httpFactory = new FakeHttpClientFactory();

        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineFake, fakeFactory, httpFactory);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        var executeTask = service.StartAsync(cts.Token);
        await Task.Delay(50);

        var testEx = new InvalidOperationException("test reconnect");
        await fakeConnection.RaiseReconnectingAsync(testEx);
        await Task.Delay(100);

        try
        {
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("reconnecting")),
                It.Is<Exception?>(ex => ex == testEx),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
