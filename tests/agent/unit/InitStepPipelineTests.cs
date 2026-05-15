using System.Net;
using System.Text;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class InitStepPipelineTests
{
    private static (PipelineExecutor, List<MessageEnvelope>) CreateExecutor(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? respond = null)
    {
        var handler = new CapturingHttpHandler(respond ?? ((req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
            };
        }));
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var loggerMock = new Mock<ILogger<PipelineExecutor>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:UseChunkedDownload"] = "false"
            })
            .Build();
        var executor = new PipelineExecutor(httpFactoryMock.Object, loggerMock.Object, configuration);

        var messages = new List<MessageEnvelope>();
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync = (msg, ct) =>
        {
            messages.Add(msg);
            return Task.CompletedTask;
        };

        return (executor, messages);
    }

    private static PipelineContext CreateContext(
        string mode = "install",
        bool forceInstall = false,
        List<PackageAssignment>? targetPackages = null,
        List<PackageAssignment>? currentPackages = null,
        List<string>? preWorkloadSteps = null,
        List<string>? postWorkloadSteps = null,
        List<string>? preUninstallSteps = null,
        List<string>? postUninstallSteps = null,
        string defaultShell = "cmd")
    {
        var runId = Guid.NewGuid();
        return new PipelineContext
        {
            Payload = new AssignRunPayload
            {
                RunId = runId,
                WorkloadName = "test-workload",
                Mode = mode,
                Packages = targetPackages ?? new List<PackageAssignment>(),
                PreWorkloadSteps = preWorkloadSteps ?? new List<string>(),
                PostWorkloadSteps = postWorkloadSteps ?? new List<string>(),
                PreUninstallSteps = preUninstallSteps ?? new List<string>(),
                PostUninstallSteps = postUninstallSteps ?? new List<string>(),
                DefaultShell = defaultShell,
                CurrentPackages = currentPackages ?? new List<PackageAssignment>(),
                ForceInstall = forceInstall
            },
            OrchestratorBaseUrl = "https://unit.test",
            AgentId = "agent-1",
            RunId = runId.ToString(),
            Sequence = 1,
            ForceInstall = forceInstall,
            CurrentPackages = currentPackages ?? new List<PackageAssignment>()
        };
    }

    private static PackageAssignment CreatePackage(int index, string name, string version,
        List<string>? preInitSteps = null, List<string>? postInitSteps = null)
    {
        return new PackageAssignment
        {
            PackageIndex = index,
            PackageId = name,
            Name = name,
            Version = version,
            PreInitSteps = preInitSteps ?? new List<string>(),
            PostInitSteps = postInitSteps ?? new List<string>(),
            InstallAdapter = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "cmd",
                Arguments = "/C exit 0",
                TimeoutSeconds = 10
            },
            Detection = new DetectionConfig
            {
                Type = "version_manifest",
                Path = "cmd"
            }
        };
    }

    private static List<string> StepHistoryNames(PipelineContext context)
        => context.StepHistory.Select(s => s.StepName).ToList();

    [Test]
    public async Task PreWorkload_steps_execute_before_packages()
    {
        var (executor, _) = CreateExecutor();
        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>(),
            preWorkloadSteps: new List<string> { "exit 0" },
            postWorkloadSteps: new List<string> { "exit 0" });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        var preWorkloadIdx = names.IndexOf("PreWorkload_0");
        var postWorkloadIdx = names.IndexOf("PostWorkload_0");
        Assert.That(preWorkloadIdx >= 0, Is.True, "PreWorkload_0 should be in step history");
        Assert.That(postWorkloadIdx >= 0, Is.True, "PostWorkload_0 should be in step history");
        Assert.That(preWorkloadIdx < postWorkloadIdx, Is.True, "PreWorkload should run before PostWorkload");
    }

    [Test]
    public async Task PreWorkload_failure_aborts_entire_run()
    {
        var (executor, _) = CreateExecutor();
        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0")
            },
            preWorkloadSteps: new List<string> { "exit 1" });

        var result = await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        Assert.That(result.Success, Is.False);
        var names = StepHistoryNames(context);
        Assert.That(names, Does.Contain("PreWorkload_0"));
        Assert.That(names, Does.Not.Contain("AcquireArtifact"));
        Assert.That(names, Does.Not.Contain("InstallOrUpgrade"));
        Assert.That(names, Does.Not.Contain("PostInstallVerify"));
    }

    [Test]
    public async Task PreInit_steps_execute_before_AcquireArtifact()
    {
        var (executor, _) = CreateExecutor(respond: (req, ct) =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0", preInitSteps: new List<string> { "exit 0" })
            });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        var preInitIdx = names.IndexOf("PreInit_0_0");
        var acquireIdx = names.IndexOf("AcquireArtifact");
        Assert.That(preInitIdx >= 0, Is.True, "PreInit_0_0 should be in step history");
        Assert.That(acquireIdx >= 0, Is.True, "AcquireArtifact should be in step history");
        Assert.That(preInitIdx < acquireIdx, Is.True, "PreInit should run before AcquireArtifact");
    }

    [Test]
    public async Task PreInit_failure_skips_install_but_continues_workload()
    {
        var (executor, _) = CreateExecutor(respond: (req, ct) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
            });

        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-fail", "1.0.0", preInitSteps: new List<string> { "exit 1" }),
                CreatePackage(1, "pkg-ok", "1.0.0", preInitSteps: new List<string> { "exit 0" })
            },
            postWorkloadSteps: new List<string> { "exit 0" });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        Assert.That(names, Does.Contain("PreInit_0_0"));
        var pkg0PreInit = context.StepHistory.First(s => s.StepName == "PreInit_0_0");
        Assert.That(pkg0PreInit.Success, Is.False);

        Assert.That(names, Does.Contain("PreInit_1_0"));
        var pkg1PreInit = context.StepHistory.First(s => s.StepName == "PreInit_1_0");
        Assert.That(pkg1PreInit.Success, Is.True);
        var pkg1HasAcquire = names.Contains("AcquireArtifact");
        Assert.That(pkg1HasAcquire, Is.True);

        Assert.That(names, Does.Contain("PostWorkload_0"));
    }

    [Test]
    public async Task PostInit_steps_execute_after_PostInstallVerify()
    {
        var (executor, _) = CreateExecutor(respond: (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
            };
        });

        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0", postInitSteps: new List<string> { "exit 0" })
            });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        var postInstallVerifyIdx = names.IndexOf("PostInstallVerify");
        var postInitIdx = names.IndexOf("PostInit_0_0");
        Assert.That(postInstallVerifyIdx >= 0, Is.True, "PostInstallVerify should be in step history");
        Assert.That(postInitIdx >= 0, Is.True, "PostInit_0_0 should be in step history");
        Assert.That(postInstallVerifyIdx < postInitIdx, Is.True, "PostInstallVerify should run before PostInit");
    }

    [Test]
    public async Task PostInit_failure_marks_package_failed_continues()
    {
        var (executor, _) = CreateExecutor(respond: (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
            };
        });

        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-fail", "1.0.0", postInitSteps: new List<string> { "exit 1" }),
                CreatePackage(1, "pkg-ok", "1.0.0", postInitSteps: new List<string> { "exit 0" })
            });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var pkg0PostInit = context.StepHistory.First(s => s.StepName == "PostInit_0_0");
        Assert.That(pkg0PostInit.Success, Is.False);

        var names = StepHistoryNames(context);
        Assert.That(names, Does.Contain("PostInit_1_0"));
        var pkg1PostInit = context.StepHistory.First(s => s.StepName == "PostInit_1_0");
        Assert.That(pkg1PostInit.Success, Is.True);
    }

    [Test]
    public async Task PostWorkload_steps_execute_after_all_packages()
    {
        var (executor, _) = CreateExecutor(respond: (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
            };
        });

        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0"),
                CreatePackage(1, "pkg-b", "1.0.0")
            },
            postWorkloadSteps: new List<string> { "exit 0" });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        var lastPkgStepIdx = Math.Max(
            names.IndexOf("PostInstallVerify"),
            names.LastIndexOf("AcquireArtifact"));
        var postWorkloadIdx = names.IndexOf("PostWorkload_0");
        Assert.That(postWorkloadIdx >= 0, Is.True, "PostWorkload_0 should be in step history");
        Assert.That(lastPkgStepIdx < postWorkloadIdx, Is.True, "PostWorkload should run after all package steps");
    }

    [Test]
    public async Task PostWorkload_failure_marks_run_failed()
    {
        var (executor, _) = CreateExecutor();

        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>(),
            postWorkloadSteps: new List<string> { "exit 1" });

        var result = await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        Assert.That(result.Success, Is.False);
        var names = StepHistoryNames(context);
        Assert.That(names, Does.Contain("PostWorkload_0"));
        var pwStep = context.StepHistory.First(s => s.StepName == "PostWorkload_0");
        Assert.That(pwStep.Success, Is.False);
    }

    [Test]
    public async Task Uninstall_mode_skips_all_init_steps()
    {
        var (executor, _) = CreateExecutor();

        var context = CreateContext(
            mode: "uninstall",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0",
                    preInitSteps: new List<string> { "exit 0" },
                    postInitSteps: new List<string> { "exit 0" })
            },
            preWorkloadSteps: new List<string> { "exit 0" },
            postWorkloadSteps: new List<string> { "exit 0" },
            preUninstallSteps: new List<string> { "exit 0" },
            postUninstallSteps: new List<string> { "exit 0" });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        Assert.That(names, Has.None.Matches<string>(n => n.StartsWith("PreWorkload")));
        Assert.That(names, Has.None.Matches<string>(n => n.StartsWith("PreInit")));
        Assert.That(names, Has.None.Matches<string>(n => n.StartsWith("PostInit")));
        Assert.That(names, Has.None.Matches<string>(n => n.StartsWith("PostWorkload")));
        Assert.That(names, Does.Contain("PreUninstall_0"));
        Assert.That(names, Does.Contain("PostUninstall_0"));
    }

    [Test]
    public async Task Uninstall_mode_runs_pre_and_post_uninstall_steps()
    {
        var (executor, _) = CreateExecutor();

        var context = CreateContext(
            mode: "uninstall",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0")
            },
            preUninstallSteps: new List<string> { "exit 0" },
            postUninstallSteps: new List<string> { "exit 0" });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        var preUninstallIdx = names.IndexOf("PreUninstall_0");
        var uninstallIdx = names.IndexOf("UninstallPackage");
        var postUninstallIdx = names.IndexOf("PostUninstall_0");

        Assert.That(preUninstallIdx >= 0, Is.True, "PreUninstall_0 should be in step history");
        Assert.That(uninstallIdx >= 0, Is.True, "UninstallPackage should be in step history");
        Assert.That(postUninstallIdx >= 0, Is.True, "PostUninstall_0 should be in step history");
        Assert.That(preUninstallIdx < uninstallIdx, Is.True, "PreUninstall should run before UninstallPackage");
        Assert.That(uninstallIdx < postUninstallIdx, Is.True, "PostUninstall should run after UninstallPackage");
    }

    [Test]
    public async Task ForceInstall_runs_init_steps_on_all_packages()
    {
        var (executor, _) = CreateExecutor(respond: (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
            };
        });

        // Packages are "Added" since CurrentPackages is empty; ForceInstall skips PreCheckProbe
        // but init steps still run because packages are in packagesToInstall.
        var context = CreateContext(
            mode: "install",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>(),
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0", preInitSteps: new List<string> { "exit 0" }, postInitSteps: new List<string> { "exit 0" }),
                CreatePackage(1, "pkg-b", "1.0.0", preInitSteps: new List<string> { "exit 0" }, postInitSteps: new List<string> { "exit 0" })
            },
            preWorkloadSteps: new List<string> { "exit 0" },
            postWorkloadSteps: new List<string> { "exit 0" });

        var result = await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        Assert.That(names, Does.Contain("PreWorkload_0"));
        Assert.That(names, Does.Contain("PreInit_0_0"));
        Assert.That(names, Does.Contain("PostInit_0_0"));
        Assert.That(names, Does.Contain("PreInit_1_0"));
        Assert.That(names, Does.Contain("PostInit_1_0"));
        Assert.That(names, Does.Contain("PostWorkload_0"));

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task Update_ChangedPackage_RunsPreInitAndPostInit()
    {
        var (executor, _) = CreateExecutor();

        var context = CreateContext(
            mode: "update",
            forceInstall: true,
            currentPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "1.0.0")
            },
            targetPackages: new List<PackageAssignment>
            {
                CreatePackage(0, "pkg-a", "2.0.0",
                    preInitSteps: new List<string> { "echo pre" },
                    postInitSteps: new List<string> { "echo post" })
            });

        await executor.ExecuteAsync(context, (msg, ct) => Task.CompletedTask);

        var names = StepHistoryNames(context);
        var preInitIdx = names.IndexOf("PreInit_0_0");
        var acquireIdx = names.IndexOf("AcquireArtifact");
        var postInstallVerifyIdx = names.IndexOf("PostInstallVerify");
        var postInitIdx = names.IndexOf("PostInit_0_0");

        Assert.That(preInitIdx >= 0, Is.True, "PreInit_0_0 should be in step history");
        Assert.That(acquireIdx >= 0, Is.True, "AcquireArtifact should be in step history");
        Assert.That(preInitIdx < acquireIdx, Is.True, "PreInit should run before AcquireArtifact");

        Assert.That(postInstallVerifyIdx >= 0, Is.True, "PostInstallVerify should be in step history");
        Assert.That(postInitIdx >= 0, Is.True, "PostInit_0_0 should be in step history");
        Assert.That(postInstallVerifyIdx < postInitIdx, Is.True, "PostInstallVerify should run before PostInit");
    }
}
