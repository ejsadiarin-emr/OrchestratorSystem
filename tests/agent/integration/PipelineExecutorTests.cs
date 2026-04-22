using System.Net;
using System.Text;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using NUnit.Framework;

namespace DeploymentPoC.Agent.IntegrationTests;

public sealed class PipelineExecutorTests
{
    [Test]
    public async Task PipelineExecutor_ExecutesAllSteps_AndSendsCompletion()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: false);
        using var http = new HttpClient(handler);
        var executor = new PipelineExecutor(new StubHttpClientFactory(http), new Microsoft.Extensions.Logging.Abstractions.NullLogger<PipelineExecutor>());

        var tempFile = Path.Combine(Path.GetTempPath(), $"pipeline-test-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(tempFile, payload);

        try
        {
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
                                Path = tempFile,
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

            var messages = new List<MessageEnvelope>();
            var result = await executor.ExecuteAsync(context, (msg, ct) =>
            {
                messages.Add(msg);
                return Task.CompletedTask;
            });

            Assert.That(result.Success, Is.True);
            Assert.That(result.StepsExecuted, Is.EqualTo(3));
            Assert.That(messages.Count, Is.EqualTo(4)); // 3 step status + 1 complete
            Assert.That(messages.Last().MessageType, Is.EqualTo(MessageTypes.Complete));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task PipelineExecutor_HaltsOnAcquireFailure_AndSendsFail()
    {
        var handler = new StubArtifactHandler([], supportsRange: false, statusCode: HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        var executor = new PipelineExecutor(new StubHttpClientFactory(http), new Microsoft.Extensions.Logging.Abstractions.NullLogger<PipelineExecutor>());

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
                        PackageId = "missing-pkg",
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

        var messages = new List<MessageEnvelope>();
        var result = await executor.ExecuteAsync(context, (msg, ct) =>
        {
            messages.Add(msg);
            return Task.CompletedTask;
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.StepsExecuted, Is.EqualTo(1)); // Only acquire ran
        Assert.That(messages.Count, Is.EqualTo(2)); // 1 step status + 1 fail
        Assert.That(messages.Last().MessageType, Is.EqualTo(MessageTypes.Fail));
    }

    [Test]
    public async Task PipelineExecutor_HaltsOnInstallFailure_AndSendsFail()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: false);
        using var http = new HttpClient(handler);
        var executor = new PipelineExecutor(new StubHttpClientFactory(http), new Microsoft.Extensions.Logging.Abstractions.NullLogger<PipelineExecutor>());

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
                        Version = "1.0.0",
                        InstallAdapter = new InstallAdapterConfig
                        {
                            Type = "exe",
                            Command = "nonexistent-command-12345",
                            Arguments = "",
                            TimeoutSeconds = 5
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

        var messages = new List<MessageEnvelope>();
        var result = await executor.ExecuteAsync(context, (msg, ct) =>
        {
            messages.Add(msg);
            return Task.CompletedTask;
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.StepsExecuted, Is.EqualTo(2)); // Acquire + install
        Assert.That(messages.Count, Is.EqualTo(3)); // 2 step status + 1 fail
        Assert.That(messages.Last().MessageType, Is.EqualTo(MessageTypes.Fail));
    }

    [Test]
    public async Task PipelineExecutor_ExecutesPackagesInOrder()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: false);
        using var http = new HttpClient(handler);
        var executor = new PipelineExecutor(new StubHttpClientFactory(http), new Microsoft.Extensions.Logging.Abstractions.NullLogger<PipelineExecutor>());

        var tempFile = Path.Combine(Path.GetTempPath(), $"pipeline-test-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(tempFile, payload);

        try
        {
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
                            PackageIndex = 1,
                            PackageId = "pkg-b",
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
                                Path = tempFile,
                                ExpectedVersion = null
                            }
                        },
                        new()
                        {
                            PackageIndex = 0,
                            PackageId = "pkg-a",
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
                                Path = tempFile,
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

            var messages = new List<MessageEnvelope>();
            var result = await executor.ExecuteAsync(context, (msg, ct) =>
            {
                messages.Add(msg);
                return Task.CompletedTask;
            });

            Assert.That(result.Success, Is.True);
            Assert.That(result.StepsExecuted, Is.EqualTo(6)); // 2 packages * 3 steps

            var stepStatuses = messages.Where(m => m.MessageType == MessageTypes.StepStatus).ToList();
            var packageOrder = stepStatuses
                .Select(m => (m.Payload as StepStatusPayload)?.PackageId)
                .Distinct()
                .ToList();

            Assert.That(packageOrder[0], Is.EqualTo("pkg-a"));
            Assert.That(packageOrder[1], Is.EqualTo("pkg-b"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}

public sealed class InstallOrUpgradeTests
{
    [Test]
    public async Task InstallOrUpgrade_ExecutesCommandSuccessfully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"install-test-{Guid.NewGuid():N}.txt");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "touch",
                Arguments = tempFile,
                TimeoutSeconds = 5
            };

            // Create a dummy artifact file since InstallOrUpgrade checks for it
            var artifactPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");
            File.WriteAllText(artifactPath, "dummy");

            try
            {
                var result = await InstallOrUpgrade.ExecuteAsync(config, artifactPath, CancellationToken.None);
                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(tempFile), Is.True);
            }
            finally
            {
                if (File.Exists(artifactPath))
                    File.Delete(artifactPath);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task InstallOrUpgrade_FailsWhenArtifactMissing()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "echo",
            Arguments = "hello",
            TimeoutSeconds = 5
        };

        var result = await InstallOrUpgrade.ExecuteAsync(config, "/nonexistent/artifact.bin", CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("artifact_not_found"));
    }

    [Test]
    public async Task InstallOrUpgrade_FailsOnNonZeroExitCode()
    {
        var artifactPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");
        File.WriteAllText(artifactPath, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "false", // exits with 1 on Linux
                Arguments = "",
                TimeoutSeconds = 5
            };

            var result = await InstallOrUpgrade.ExecuteAsync(config, artifactPath, CancellationToken.None);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("exit_code_1"));
        }
        finally
        {
            if (File.Exists(artifactPath))
                File.Delete(artifactPath);
        }
    }

    [Test]
    public async Task InstallOrUpgrade_ExpandsArtifactPathPlaceholder()
    {
        var artifactPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");
        File.WriteAllText(artifactPath, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "echo",
                Arguments = "{artifactPath}",
                TimeoutSeconds = 5
            };

            var result = await InstallOrUpgrade.ExecuteAsync(config, artifactPath, CancellationToken.None);
            Assert.That(result.Success, Is.True);
        }
        finally
        {
            if (File.Exists(artifactPath))
                File.Delete(artifactPath);
        }
    }
}

public sealed class PostInstallVerifyTests
{
    [Test]
    public async Task PostInstallVerify_FileExists_ReturnsSuccess()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"verify-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "test");

        try
        {
            var config = new DetectionConfig
            {
                Type = "file",
                Path = tempFile,
                ExpectedVersion = null
            };

            var result = await PostInstallVerify.ExecuteAsync(config, CancellationToken.None);
            Assert.That(result.Success, Is.True);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task PostInstallVerify_FileMissing_ReturnsFailure()
    {
        var config = new DetectionConfig
        {
            Type = "file",
            Path = "/nonexistent/file.txt",
            ExpectedVersion = null
        };

        var result = await PostInstallVerify.ExecuteAsync(config, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("file_not_found"));
    }

    [Test]
    public async Task PostInstallVerify_Registry_ReturnsSuccessStub()
    {
        var config = new DetectionConfig
        {
            Type = "registry",
            Path = "HKLM\\Software\\Test",
            ExpectedVersion = null
        };

        var result = await PostInstallVerify.ExecuteAsync(config, CancellationToken.None);
        Assert.That(result.Success, Is.True); // Stub behavior for PoC
    }

    [Test]
    public async Task PostInstallVerify_InvalidType_ReturnsFailure()
    {
        var config = new DetectionConfig
        {
            Type = "unknown",
            Path = "/some/path",
            ExpectedVersion = null
        };

        var result = await PostInstallVerify.ExecuteAsync(config, CancellationToken.None);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("unsupported_detection_type"));
    }
}

public sealed class EmitFinalizationTests
{
    [Test]
    public void EmitFinalization_CreateComplete_HasCorrectTypeAndPayload()
    {
        var envelope = EmitFinalization.CreateComplete("run-1", "agent-1", 5, 3);

        Assert.That(envelope.MessageType, Is.EqualTo(MessageTypes.Complete));
        Assert.That(envelope.RunId, Is.EqualTo("run-1"));
        Assert.That(envelope.AgentId, Is.EqualTo("agent-1"));
        Assert.That(envelope.Sequence, Is.EqualTo(5));

        var payload = (FinalizationPayload)envelope.Payload;
        Assert.That(payload.Result, Is.EqualTo("success"));
        Assert.That(payload.StepCount, Is.EqualTo(3));
    }

    [Test]
    public void EmitFinalization_CreateFail_HasCorrectTypeAndPayload()
    {
        var envelope = EmitFinalization.CreateFail("run-1", "agent-1", 5, "install_failed", 2);

        Assert.That(envelope.MessageType, Is.EqualTo(MessageTypes.Fail));
        Assert.That(envelope.RunId, Is.EqualTo("run-1"));
        Assert.That(envelope.AgentId, Is.EqualTo("agent-1"));
        Assert.That(envelope.Sequence, Is.EqualTo(5));

        var payload = (FinalizationPayload)envelope.Payload;
        Assert.That(payload.Result, Is.EqualTo("failure"));
        Assert.That(payload.Error, Is.EqualTo("install_failed"));
        Assert.That(payload.StepCount, Is.EqualTo(2));
    }
}

file sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;

    public StubHttpClientFactory(HttpClient client)
    {
        _client = client;
    }

    public HttpClient CreateClient(string name = "") => _client;
}

file sealed class StubArtifactHandler : HttpMessageHandler
{
    private readonly byte[] _payload;
    private readonly bool _supportsRange;
    private readonly HttpStatusCode _statusCode;

    public StubArtifactHandler(byte[] payload, bool supportsRange, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _payload = payload;
        _supportsRange = supportsRange;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_statusCode != HttpStatusCode.OK)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }

        if (request.Method == HttpMethod.Head)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };
            response.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(response);
        }

        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
    }
}
