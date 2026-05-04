using System.Net;
using System.Net.Http.Json;
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

    [Test]
    public async Task FireAndForget_TerminalPatch_Sent_Even_When_StoppingToken_Cancelled()
    {
        var nodeId = Guid.NewGuid();
        var runId = Guid.NewGuid();
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

        var pendingRuns = new List<PendingWorkloadRunResponse>
        {
            new()
            {
                RunId = runId,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "TestWorkload",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            }
        };

        var handler = new CapturingHttpHandler((req, ct) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/pending"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(pendingRuns)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var pipelineExecutor = new DelayingPipelineExecutor(TimeSpan.FromMilliseconds(500));
        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineExecutor, fakeFactory, httpFactoryMock.Object);

        using var cts = new CancellationTokenSource();
        var executeTask = service.StartAsync(cts.Token);

        // Wait until the pipeline executor has started (inside the fire-and-forget task)
        await pipelineExecutor.Started;

        // Cancel the stopping token while the pipeline is still executing
        cts.Cancel();

        // Wait for the background service loop to exit
        try { await service.ExecuteTask!; }
        catch (OperationCanceledException) { }

        // Give the fire-and-forget task time to finish
        await Task.Delay(TimeSpan.FromSeconds(1));

        var patchRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Patch && r.RequestUri!.ToString().Contains($"/api/workload-runs/{runId}"))
            .ToList();

        Assert.That(patchRequests.Count, Is.GreaterThanOrEqualTo(2), "Expected claim PATCH and terminal PATCH");

        var terminalPatch = patchRequests.Last();
        var body = await terminalPatch.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("Completed"));
    }

    [Test]
    public async Task PollAndProcessAsync_PipelineExceedsMaxDuration_GetsCancelled()
    {
        var nodeId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:PipelineTimeoutMinutes"] = "0.05"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();

        var pendingRuns = new List<PendingWorkloadRunResponse>
        {
            new()
            {
                RunId = runId,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "TestWorkload",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            }
        };

        var handler = new CapturingHttpHandler((req, ct) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/pending"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(pendingRuns)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var pipelineExecutor = new CancellablePipelineExecutor();
        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineExecutor, fakeFactory, httpFactoryMock.Object);

        using var cts = new CancellationTokenSource();
        var executeTask = service.StartAsync(cts.Token);

        await pipelineExecutor.Started;

        // Wait for the watchdog to cancel the pipeline
        await pipelineExecutor.Cancelled.WaitAsync(TimeSpan.FromSeconds(10));

        // Stop the background loop
        cts.Cancel();
        try { await service.ExecuteTask!; } catch (OperationCanceledException) { }

        // Give the fire-and-forget pipeline task time to send the terminal patch
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Verify terminal patch indicates failure due to cancellation
        var patchRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Patch && r.RequestUri!.ToString().Contains($"/api/workload-runs/{runId}"))
            .ToList();

        Assert.That(patchRequests.Count, Is.GreaterThanOrEqualTo(2), "Expected claim PATCH and terminal PATCH");

        var terminalPatch = patchRequests.Last();
        var body = await terminalPatch.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("Failed"));
        Assert.That(body, Does.Contain("canceled").IgnoreCase);
    }

    [Test]
    public async Task PollAndProcessAsync_StoppingToken_CancelsPipeline()
    {
        var nodeId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:PipelineTimeoutMinutes"] = "60"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();

        var pendingRuns = new List<PendingWorkloadRunResponse>
        {
            new()
            {
                RunId = runId,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "TestWorkload",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            }
        };

        var handler = new CapturingHttpHandler((req, ct) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/pending"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(pendingRuns)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var pipelineExecutor = new CancellablePipelineExecutor();
        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineExecutor, fakeFactory, httpFactoryMock.Object);

        using var cts = new CancellationTokenSource();
        var executeTask = service.StartAsync(cts.Token);

        await pipelineExecutor.Started;

        // Cancel the stopping token while the pipeline is still executing
        cts.Cancel();

        // Wait for the pipeline to be cancelled
        await pipelineExecutor.Cancelled.WaitAsync(TimeSpan.FromSeconds(10));

        try { await service.ExecuteTask!; } catch (OperationCanceledException) { }

        // Give the fire-and-forget pipeline task time to send the terminal patch
        await Task.Delay(TimeSpan.FromSeconds(1));

        var patchRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Patch && r.RequestUri!.ToString().Contains($"/api/workload-runs/{runId}"))
            .ToList();

        Assert.That(patchRequests.Count, Is.GreaterThanOrEqualTo(2), "Expected claim PATCH and terminal PATCH");

        var terminalPatch = patchRequests.Last();
        var body = await terminalPatch.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("Failed"));
        Assert.That(body, Does.Contain("canceled").IgnoreCase);
    }

    [Test]
    public async Task SemaphoreBlocksConcurrentPipelineExecution_SecondRunDeferred()
    {
        var nodeId = Guid.NewGuid();
        var run1 = Guid.NewGuid();
        var run2 = Guid.NewGuid();

        var claimedRuns = new List<Guid>();
        var pendingRuns = new List<PendingWorkloadRunResponse>
        {
            new()
            {
                RunId = run1,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "Workload1",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            },
            new()
            {
                RunId = run2,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "Workload2",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            }
        };

        var handler = new CapturingHttpHandler((req, ct) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/pending"))
            {
                var unclaimed = pendingRuns.Where(r => !claimedRuns.Contains(r.RunId)).ToList();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(unclaimed)
                };
            }

            if (req.Method == HttpMethod.Patch && req.RequestUri!.ToString().Contains($"/api/workload-runs/{run1}"))
            {
                claimedRuns.Add(run1);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (req.Method == HttpMethod.Patch && req.RequestUri!.ToString().Contains($"/api/workload-runs/{run2}"))
            {
                claimedRuns.Add(run2);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:PollIntervalSeconds"] = "0.5"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var pipelineExecutor = new DelayingPipelineExecutor(TimeSpan.FromMilliseconds(200));
        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineExecutor, fakeFactory, httpFactoryMock.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        var executeTask = service.StartAsync(cts.Token);

        await pipelineExecutor.Started;

        Assert.That(claimedRuns.Contains(run1), Is.True, "Run1 should be claimed immediately");
        Assert.That(claimedRuns.Contains(run2), Is.False, "Run2 should not be claimed during the same poll cycle");

        try { await service.ExecuteTask!; } catch (OperationCanceledException) { }

        Assert.That(claimedRuns.Contains(run2), Is.True, "Run2 should be claimed after the first pipeline completes");
    }

    [Test]
    public async Task PollingContinuesDuringActivePipeline_LivenessSignal()
    {
        var nodeId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var pendingRuns = new List<PendingWorkloadRunResponse>
        {
            new()
            {
                RunId = runId,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "TestWorkload",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            }
        };

        var handler = new CapturingHttpHandler((req, ct) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/pending"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(pendingRuns)
                };
            }

            if (req.Method == HttpMethod.Patch && req.RequestUri!.ToString().Contains($"/api/workload-runs/{runId}"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:PollIntervalSeconds"] = "0.05"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var pipelineExecutor = new DelayingPipelineExecutor(TimeSpan.FromMilliseconds(500));
        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineExecutor, fakeFactory, httpFactoryMock.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        var executeTask = service.StartAsync(cts.Token);

        await pipelineExecutor.Started;

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        cts.Cancel();
        try { await service.ExecuteTask!; } catch (OperationCanceledException) { }

        var getRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains("/pending"))
            .ToList();

        var patchRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Patch && r.RequestUri!.ToString().Contains($"/api/workload-runs/{runId}"))
            .ToList();

        Assert.That(getRequests.Count, Is.GreaterThanOrEqualTo(3), "Polling should continue while pipeline is active");
        Assert.That(patchRequests.Count, Is.EqualTo(1), "Only one run should be claimed at a time");
    }

    [Test]
    public async Task SemaphoreReleased_WhenClaimFails_SubsequentClaimSucceeds()
    {
        var nodeId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var pendingRuns = new List<PendingWorkloadRunResponse>
        {
            new()
            {
                RunId = runId,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "TestWorkload",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            }
        };

        var claimAttemptCount = 0;
        var runClaimed = false;

        var handler = new CapturingHttpHandler((req, ct) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/pending"))
            {
                var runs = runClaimed ? new List<PendingWorkloadRunResponse>() : pendingRuns;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(runs)
                };
            }

            if (req.Method == HttpMethod.Patch && req.RequestUri!.ToString().Contains($"/api/workload-runs/{runId}"))
            {
                claimAttemptCount++;
                if (claimAttemptCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                }

                runClaimed = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:PollIntervalSeconds"] = "0.1"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var pipelineExecutor = new FakePipelineExecutor();
        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineExecutor, fakeFactory, httpFactoryMock.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        var executeTask = service.StartAsync(cts.Token);

        try { await service.ExecuteTask!; } catch (OperationCanceledException) { }

        var getRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains("/pending"))
            .ToList();

        var patchRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Patch && r.RequestUri!.ToString().Contains($"/api/workload-runs/{runId}"))
            .ToList();

        Assert.That(getRequests.Count, Is.GreaterThanOrEqualTo(2), "At least two poll cycles should occur");
        Assert.That(patchRequests.Count, Is.GreaterThanOrEqualTo(2), "A second claim attempt should be possible after the first fails");
        Assert.That(runClaimed, Is.True, "The run should be successfully claimed in a subsequent poll cycle");
    }

    [Test]
    public async Task NoConcurrentPipelineExecution_OnlyOneClaimAtATime()
    {
        var nodeId = Guid.NewGuid();
        var run1 = Guid.NewGuid();
        var run2 = Guid.NewGuid();

        var pendingRuns = new List<PendingWorkloadRunResponse>
        {
            new()
            {
                RunId = run1,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "Workload1",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            },
            new()
            {
                RunId = run2,
                WorkloadId = Guid.NewGuid(),
                WorkloadName = "Workload2",
                Mode = "Install",
                Packages = new(),
                CurrentPackages = new()
            }
        };

        var handler = new CapturingHttpHandler((req, ct) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/pending"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(pendingRuns)
                };
            }

            if (req.Method == HttpMethod.Patch)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:NodeId"] = nodeId.ToString(),
                ["Orchestrator:BaseUrl"] = "http://localhost:5000",
                ["Agent:PollIntervalSeconds"] = "0.1"
            })
            .Build();

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var fakeConnection = new FakeHubConnection();
        var fakeFactory = new FakeHubConnectionFactory(fakeConnection);

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var pipelineExecutor = new DelayingPipelineExecutor(TimeSpan.FromMilliseconds(500));
        var service = new AgentRuntimeService(config, loggerMock.Object, pipelineExecutor, fakeFactory, httpFactoryMock.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        var executeTask = service.StartAsync(cts.Token);

        await pipelineExecutor.Started;

        await Task.Delay(TimeSpan.FromMilliseconds(250));

        cts.Cancel();
        try { await service.ExecuteTask!; } catch (OperationCanceledException) { }

        var patchRequests = handler.Requests
            .Where(r => r.Method == HttpMethod.Patch)
            .ToList();

        Assert.That(patchRequests.Count, Is.EqualTo(1), "Only one pipeline should be claimed at any given time");
    }
}
