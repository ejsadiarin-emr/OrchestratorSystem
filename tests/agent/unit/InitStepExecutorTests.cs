using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Steps;
using Moq;
using Xunit;

namespace DeploymentPoC.Agent.Tests;

public sealed class InitStepExecutorTests
{
    [Fact]
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

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.ErrorOutput);
        Assert.Equal(2, statusCalls.Count);
        Assert.Equal("Running", statusCalls[0].Status);
        Assert.Equal("Completed", statusCalls[1].Status);
        Assert.Equal("PreInit_0_0", statusCalls[0].StepName);
        Assert.Equal(0, statusCalls[0].PackageIndex);
    }

    [Fact]
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

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(2, statusCalls.Count);
        Assert.Equal("Running", statusCalls[0].Status);
        Assert.Equal("Failed", statusCalls[1].Status);
    }

    [Fact]
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

        Assert.False(result.Success);
        Assert.Equal(-1, result.ExitCode);
        Assert.Equal("timeout", result.ErrorOutput);
        Assert.Equal(2, statusCalls.Count);
        Assert.Equal("Running", statusCalls[0].Status);
        Assert.Equal("Failed", statusCalls[1].Status);
        Assert.Equal("timeout", statusCalls[1].Error);
    }

    [Fact]
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

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
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

        Assert.True(result.Success);
        Assert.Equal(2, statusCalls.Count);

        var first = statusCalls[0];
        Assert.Equal("Running", first.Status);
        Assert.Equal("PostWorkload_0", first.StepName);
        Assert.Equal(2, first.PackageIndex);

        var second = statusCalls[1];
        Assert.Equal("Completed", second.Status);
        Assert.Equal("PostWorkload_0", second.StepName);
        Assert.Equal(2, second.PackageIndex);
    }

    [Fact]
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

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
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

        Assert.False(result.Success);
        Assert.Equal("cancelled", result.ErrorOutput);
    }
}
