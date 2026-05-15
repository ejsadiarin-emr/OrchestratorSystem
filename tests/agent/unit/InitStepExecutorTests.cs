using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Moq;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class InitStepExecutorTests
{
    [Test]
    public async Task ExecuteAsync_SuccessfulCommand_ReturnsSuccess()
    {
        var executor = new InitStepExecutor();
        var statusCalls = new List<StepStatusPayload>();

        var result = await executor.ExecuteAsync(
            command: "exit 0",
            defaultShell: "cmd",
            stepName: "PreInit_0_0",
            envVars: new Dictionary<string, string>(),
            timeoutSeconds: 5,
            packageIndex: 0,
            sendStatusAsync: payload =>
            {
                statusCalls.Add(payload);
                return Task.CompletedTask;
            },
            ct: CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.ErrorOutput, Is.Null);
        Assert.That(statusCalls.Count, Is.EqualTo(2));
        Assert.That(statusCalls[0].Status, Is.EqualTo("Running"));
        Assert.That(statusCalls[1].Status, Is.EqualTo("Completed"));
        Assert.That(statusCalls[0].StepName, Is.EqualTo("PreInit_0_0"));
        Assert.That(statusCalls[0].PackageIndex, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_FailedCommand_ReturnsFailure()
    {
        var executor = new InitStepExecutor();
        var statusCalls = new List<StepStatusPayload>();

        var result = await executor.ExecuteAsync(
            command: "exit 1",
            defaultShell: "cmd",
            stepName: "PreInit_0_0",
            envVars: new Dictionary<string, string>(),
            timeoutSeconds: 5,
            packageIndex: 0,
            sendStatusAsync: payload =>
            {
                statusCalls.Add(payload);
                return Task.CompletedTask;
            },
            ct: CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(statusCalls.Count, Is.EqualTo(2));
        Assert.That(statusCalls[0].Status, Is.EqualTo("Running"));
        Assert.That(statusCalls[1].Status, Is.EqualTo("Failed"));
    }

    [Test]
    public async Task ExecuteAsync_Timeout_KillsProcessAndReturnsFailure()
    {
        var executor = new InitStepExecutor();
        var statusCalls = new List<StepStatusPayload>();

        var result = await executor.ExecuteAsync(
            command: "Start-Sleep -Seconds 30",
            defaultShell: "powershell",
            stepName: "PreInit_0_0",
            envVars: new Dictionary<string, string>(),
            timeoutSeconds: 1,
            packageIndex: 0,
            sendStatusAsync: payload =>
            {
                statusCalls.Add(payload);
                return Task.CompletedTask;
            },
            ct: CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(-1));
        Assert.That(result.ErrorOutput, Is.EqualTo("timeout"));
        Assert.That(statusCalls.Count, Is.EqualTo(2));
        Assert.That(statusCalls[0].Status, Is.EqualTo("Running"));
        Assert.That(statusCalls[1].Status, Is.EqualTo("Failed"));
        Assert.That(statusCalls[1].Error, Is.EqualTo("timeout"));
    }

    [Test]
    public async Task ExecuteAsync_EnvVars_InjectedIntoProcess()
    {
        var executor = new InitStepExecutor();
        var statusCalls = new List<StepStatusPayload>();

        var result = await executor.ExecuteAsync(
            command: "echo %DEPLOY_TEST_VAR%",
            defaultShell: "cmd",
            stepName: "PostInit_1_0",
            envVars: new Dictionary<string, string>
            {
                { "DEPLOY_TEST_VAR", "hello-from-env" }
            },
            timeoutSeconds: 5,
            packageIndex: 1,
            sendStatusAsync: payload =>
            {
                statusCalls.Add(payload);
                return Task.CompletedTask;
            },
            ct: CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_SendStatusCalledInCorrectSequence()
    {
        var executor = new InitStepExecutor();
        var statusCalls = new List<StepStatusPayload>();

        var result = await executor.ExecuteAsync(
            command: "echo test",
            defaultShell: "cmd",
            stepName: "PostWorkload_0",
            envVars: new Dictionary<string, string>(),
            timeoutSeconds: 5,
            packageIndex: 2,
            sendStatusAsync: payload =>
            {
                statusCalls.Add(payload);
                return Task.CompletedTask;
            },
            ct: CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(statusCalls.Count, Is.EqualTo(2));

        var first = statusCalls[0];
        Assert.That(first.Status, Is.EqualTo("Running"));
        Assert.That(first.StepName, Is.EqualTo("PostWorkload_0"));
        Assert.That(first.PackageIndex, Is.EqualTo(2));

        var second = statusCalls[1];
        Assert.That(second.Status, Is.EqualTo("Completed"));
        Assert.That(second.StepName, Is.EqualTo("PostWorkload_0"));
        Assert.That(second.PackageIndex, Is.EqualTo(2));
    }

    [Test]
    public async Task ExecuteAsync_PowerShellCommand_Success()
    {
        var executor = new InitStepExecutor();
        var statusCalls = new List<StepStatusPayload>();

        var result = await executor.ExecuteAsync(
            command: "exit 0",
            defaultShell: "powershell",
            stepName: "PreInit_0_0",
            envVars: new Dictionary<string, string>(),
            timeoutSeconds: 5,
            packageIndex: 0,
            sendStatusAsync: payload =>
            {
                statusCalls.Add(payload);
                return Task.CompletedTask;
            },
            ct: CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_CancellationToken_KillsProcess()
    {
        var executor = new InitStepExecutor();
        var statusCalls = new List<StepStatusPayload>();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        var result = await executor.ExecuteAsync(
            command: "ping 127.0.0.1 -n 30 > nul",
            defaultShell: "cmd",
            stepName: "PreInit_0_0",
            envVars: new Dictionary<string, string>(),
            timeoutSeconds: 30,
            packageIndex: 0,
            sendStatusAsync: payload =>
            {
                statusCalls.Add(payload);
                return Task.CompletedTask;
            },
            ct: cts.Token);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorOutput, Is.EqualTo("cancelled"));
    }
}
