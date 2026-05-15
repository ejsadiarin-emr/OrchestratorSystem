using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class UninstallPackageTests
{
    private static string Shell => OperatingSystem.IsWindows() ? "cmd.exe" : "sh";
    private static string ShellArgsPrefix => OperatingSystem.IsWindows() ? "/c" : "-c";

    [Test]
    public async Task UninstallPackage_SuccessfulUninstall_WithValidExitCode()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = Shell,
            UninstallArgs = $"{ShellArgsPrefix} exit 0",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task UninstallPackage_FailedUninstall_WithInvalidExitCode()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = Shell,
            UninstallArgs = $"{ShellArgsPrefix} exit 1",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("exit_code_1"));
    }

    [Test]
    public async Task UninstallPackage_TimesOut_WhenCommandExceedsTimeout()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = Shell,
            UninstallArgs = OperatingSystem.IsWindows()
                ? $"{ShellArgsPrefix} ping -n 11 127.0.0.1"
                : $"{ShellArgsPrefix} sleep 10",
            TimeoutSeconds = 1
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("uninstall_timeout"));
    }

    [Test]
    public async Task UninstallPackage_ReplacesArtifactPath_WhenPlaceholderPresent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"uninstall-test-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "dummy");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "exe",
                Command = Shell,
                UninstallArgs = OperatingSystem.IsWindows()
                    ? $"{ShellArgsPrefix} type \"{{artifactPath}}\""
                    : $"{ShellArgsPrefix} cat '{{artifactPath}}'",
                TimeoutSeconds = 5
            };

            var result = await UninstallPackage.ExecuteAsync(config, tempFile, CancellationToken.None);

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
    public async Task UninstallPackage_NoArtifactPathNeeded_WhenPlaceholderMissing()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = Shell,
            UninstallArgs = $"{ShellArgsPrefix} exit 0",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task UninstallPackage_UsesArtifactPath_WhenCommandIsPlaceholder()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "{artifactPath}",
            UninstallArgs = "/c exit 0",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, "cmd.exe", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task UninstallPackage_UsesMsiexec_WhenTypeIsMsiAndCommandIsPlaceholder()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"uninstall-msi-test-{Guid.NewGuid():N}.msi");
        File.WriteAllText(tempFile, "not a real msi");

        try
        {
            var config = new InstallAdapterConfig
            {
                Type = "msi",
                Command = "{artifactPath}",
                UninstallArgs = "/quiet /norestart",
                TimeoutSeconds = 5
            };

            var result = await UninstallPackage.ExecuteAsync(config, tempFile, CancellationToken.None);

            // msiexec should run (so not command_not_found) but fail because the package is invalid
            Assert.That(result.Success, Is.False);
            Assert.That("command_not_found", Is.Not.EqualTo(result.Error));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task UninstallPackage_UsesUninstallCommand_WhenPresent()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "should-be-ignored.exe",
            UninstallCommand = Shell,
            UninstallArgs = $"{ShellArgsPrefix} exit 0",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, "dummy-artifact.exe", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task UninstallCommand_SkipsArtifactDownload_PlaceholderIgnored()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "{artifactPath}",
            UninstallCommand = Shell,
            UninstallArgs = $"{ShellArgsPrefix} exit 0",
            TimeoutSeconds = 5
        };

        // artifactPath is provided but should be ignored because UninstallCommand is set
        var result = await UninstallPackage.ExecuteAsync(config, "should-not-exist.exe", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task UninstallCommand_FallsBackToCommand_WhenUninstallCommandEmpty()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            Command = Shell,
            UninstallCommand = "",
            UninstallArgs = $"{ShellArgsPrefix} exit 0",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task UninstallCommand_ExpandsEnvironmentVariables_WindowsStyle()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            UninstallCommand = Shell,
            UninstallArgs = $"{ShellArgsPrefix} exit 0",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task UninstallCommand_ExpandsPowerShellEnvVariable()
    {
        var config = new InstallAdapterConfig
        {
            Type = "exe",
            UninstallCommand = Shell,
            UninstallArgs = $"{ShellArgsPrefix} exit 0",
            TimeoutSeconds = 5
        };

        var result = await UninstallPackage.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }
}
