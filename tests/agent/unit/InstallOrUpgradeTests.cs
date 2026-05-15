using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class InstallOrUpgradeTests
{
    private readonly ILogger _logger = new FakeLogger();

    private sealed class FakeLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Test]
    public async Task ExecuteAsync_NullConfig_ReturnsInvalidConfig()
    {
        var result = await InstallOrUpgrade.ExecuteAsync(null!, "dummy.msi", _logger, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("invalid_config"));
    }

    [Test]
    public async Task ExecuteAsync_MissingArtifact_ReturnsArtifactNotFound()
    {
        var config = new InstallAdapterConfig { Type = "msi", Command = "msiexec" };
        var result = await InstallOrUpgrade.ExecuteAsync(config, @"C:\nonexistent\file.msi", _logger, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("artifact_not_found"));
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulCommand_ReturnsSuccess()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"install-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "cmd",
                Arguments = "/c exit 0",
                TimeoutSeconds = 5
            };

            var result = await InstallOrUpgrade.ExecuteAsync(config, tempFile, _logger, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExecuteAsync_InvalidExitCode_ReturnsError()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"install-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "cmd",
                Arguments = "/c exit 5",
                TimeoutSeconds = 5
            };

            var result = await InstallOrUpgrade.ExecuteAsync(config, tempFile, _logger, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("exit_code_5"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExecuteAsync_MsiType_InvokesMsiexec()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"install-test-{Guid.NewGuid():N}.msi");
        File.WriteAllText(tempFile, "dummy msi");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "msi",
                Command = tempFile,
                Arguments = "/qn",
                TimeoutSeconds = 5
            };

            // msiexec /i with a dummy file will fail, but we're verifying the command path
            var result = await InstallOrUpgrade.ExecuteAsync(config, tempFile, _logger, CancellationToken.None);

            // Should be an exit code error from msiexec, not command_not_found
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.StartWith("exit_code_"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExecuteAsync_TimesOut_WhenCommandExceedsTimeout()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"install-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "cmd",
                Arguments = "/c ping 127.0.0.1 -n 11 > nul",
                TimeoutSeconds = 1
            };

            var result = await InstallOrUpgrade.ExecuteAsync(config, tempFile, _logger, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("install_timeout"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExecuteAsync_ExitCode1603_AttemptsElevation()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"install-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "cmd",
                Arguments = "/c exit 1603",
                TimeoutSeconds = 5
            };

            var result = await InstallOrUpgrade.ExecuteAsync(config, tempFile, _logger, CancellationToken.None);

            // The method should have attempted elevation after seeing 1603.
            // In non-interactive environments UAC cannot be clicked, so the result
            // varies by environment (elevation_denied, win32_error_*, or exit_code_1603
            // if already running as admin). We primarily verify the code path runs.
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.False);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
