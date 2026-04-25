using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Xunit;

namespace DeploymentPoC.Agent.Tests;

public sealed class UninstallPackageTests
{
    [Fact]
    public async Task UninstallPackage_SuccessfulUninstall_WithValidExitCode()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "true",
            UninstallArgs = "",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task UninstallPackage_FailedUninstall_WithInvalidExitCode()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "false",
            UninstallArgs = "",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("exit_code_1", result.Error);
    }

    [Fact]
    public async Task UninstallPackage_TimesOut_WhenCommandExceedsTimeout()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "sleep",
            UninstallArgs = "10",
            TimeoutSeconds = 1
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("uninstall_timeout", result.Error);
    }

    [Fact]
    public async Task UninstallPackage_ReplacesArtifactPath_WhenPlaceholderPresent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"uninstall-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "cat",
                UninstallArgs = "{artifactPath}",
                TimeoutSeconds = 5
            };

            var result = await UninstallPackage.ExecuteAsync(config, tempFile, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Null(result.Error);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UninstallPackage_NoArtifactPathNeeded_WhenPlaceholderMissing()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "true",
            UninstallArgs = "--silent",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }
}
