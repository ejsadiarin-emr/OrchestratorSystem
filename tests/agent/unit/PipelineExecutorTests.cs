using System.Net;
using System.Text;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DeploymentPoC.Agent.Tests;

public sealed class PipelineExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RespectsCancellationToken()
    {
        // Arrange: handler that delays until cancellation is requested
        var delayHandler = new DelayHttpMessageHandler();
        using var httpClient = new HttpClient(delayHandler) { Timeout = TimeSpan.FromSeconds(100) };

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var loggerMock = new Mock<ILogger<PipelineExecutor>>();
        var configMock = new Mock<IConfiguration>();
        var executor = new PipelineExecutor(httpFactoryMock.Object, loggerMock.Object, configMock.Object);

        var runId = Guid.NewGuid();
        var context = new PipelineContext
        {
            Payload = new AssignRunPayload
            {
                RunId = runId,
                WorkloadName = "test-workload",
                Mode = "install",
                Packages = new List<PackageAssignment>
                {
                    new()
                    {
                        PackageIndex = 0,
                        PackageId = "test-pkg",
                        Name = "test-pkg",
                        Version = "1.0.0",
                        InstallAdapter = new InstallAdapterConfig
                        {
                            Type = "exe",
                            Command = "echo",
                            Arguments = "hello",
                            TimeoutSeconds = 30
                        },
                        Detection = new DetectionConfig
                        {
                            Type = "file",
                            Path = "/nonexistent",
                            ExpectedVersion = null
                        }
                    }
                }
            },
            OrchestratorBaseUrl = "https://unit.test",
            AgentId = "agent-1",
            RunId = runId.ToString(),
            Sequence = 1
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var capturedTokens = new List<CancellationToken>();
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync = (msg, token) =>
        {
            capturedTokens.Add(token);
            return Task.CompletedTask;
        };

        // Act & Assert: AcquireArtifact catches OperationCanceledException and returns failure,
        // so PipelineExecutor returns a failed result instead of throwing.
        var result = await executor.ExecuteAsync(context, sendMessageAsync, cts.Token);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);

        // Verify that SendStepStatusAsync passed a cancellation token linked to the provided CT.
        Assert.True(capturedTokens.Count > 0);
        cts.Cancel();
        Assert.Contains(capturedTokens, t => t.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_PassesExpectedSha256ToAcquireArtifact()
    {
        // Arrange: handler that returns fixed content so hash verification runs
        var content = Encoding.UTF8.GetBytes("hello");
        var fixedHandler = new FixedContentHttpMessageHandler(content);
        using var httpClient = new HttpClient(fixedHandler) { Timeout = TimeSpan.FromSeconds(10) };

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var loggerMock = new Mock<ILogger<PipelineExecutor>>();
        var configMock = new Mock<IConfiguration>();
        var executor = new PipelineExecutor(httpFactoryMock.Object, loggerMock.Object, configMock.Object);

        var runId = Guid.NewGuid();
        var context = new PipelineContext
        {
            Payload = new AssignRunPayload
            {
                RunId = runId,
                WorkloadName = "test-workload",
                Mode = "install",
                Packages = new List<PackageAssignment>
                {
                    new()
                    {
                        PackageIndex = 0,
                        PackageId = "test-pkg",
                        Name = "test-pkg",
                        Version = "1.0.0",
                        ExpectedSha256 = "0000000000000000000000000000000000000000000000000000000000000000",
                        InstallAdapter = new InstallAdapterConfig
                        {
                            Type = "exe",
                            Command = "echo",
                            Arguments = "hello",
                            TimeoutSeconds = 30
                        },
                        Detection = new DetectionConfig
                        {
                            Type = "file",
                            Path = "/nonexistent",
                            ExpectedVersion = null
                        }
                    }
                }
            },
            OrchestratorBaseUrl = "https://unit.test",
            AgentId = "agent-1",
            RunId = runId.ToString(),
            Sequence = 1
        };

        var result = await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        Assert.False(result.Success);
        Assert.Equal("hash_mismatch", result.Error);
    }

    private sealed class DelayHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FixedContentHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        public FixedContentHttpMessageHandler(byte[] content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            });
        }
    }
}
