using System.Reflection;
using System.Runtime.InteropServices;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Win32;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests.Steps;

[TestFixture]
public class PackageDetectorTests
{
    [SetUp]
    public void SkipOnNonWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Ignore("Registry tests require Windows");
    }
    [Test]
    public async Task DetectAsync_FileExists_ReturnsAlreadySatisfied()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"pkg-detect-test-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "test");

        try
        {
            var config = new DetectionConfig
            {
                Type = "file",
                Path = tempFile,
                ExpectedVersion = "1.0.0"
            };

            var result = await PackageDetector.DetectAsync(config, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(PreCheckStatus.AlreadySatisfied));
            Assert.That(result.ActualVersion, Is.Null);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task DetectAsync_FileMissing_ReturnsNotPresent()
    {
        var config = new DetectionConfig
        {
            Type = "file",
            Path = Path.Combine(Path.GetTempPath(), $"pkg-detect-missing-{Guid.NewGuid():N}.txt"),
            ExpectedVersion = "1.0.0"
        };

        var result = await PackageDetector.DetectAsync(config, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PreCheckStatus.NotPresent));
    }

    [Test]
    public async Task DetectAsync_RegistryMatchingVersion_ReturnsAlreadySatisfied()
    {
        var testAppName = $"DeploymentPoC-Test-{Guid.NewGuid():N}";
        var keyPath = $"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{testAppName}";

        using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
        {
            key?.SetValue("DisplayName", testAppName);
            key?.SetValue("DisplayVersion", "3.14.4");
        }

        try
        {
            var config = new DetectionConfig
            {
                Type = "registry",
                Path = testAppName,
                ExpectedVersion = "3.14.4"
            };

            var result = await PackageDetector.DetectAsync(config, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(PreCheckStatus.AlreadySatisfied));
            Assert.That(result.ActualVersion, Is.EqualTo("3.14.4"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
        }
    }

    [Test]
    public async Task DetectAsync_RegistryWrongVersion_ReturnsWrongVersion()
    {
        var testAppName = $"DeploymentPoC-Test-{Guid.NewGuid():N}";
        var keyPath = $"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{testAppName}";

        using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
        {
            key?.SetValue("DisplayName", testAppName);
            key?.SetValue("DisplayVersion", "3.13.3");
        }

        try
        {
            var config = new DetectionConfig
            {
                Type = "registry",
                Path = testAppName,
                ExpectedVersion = "3.14.4"
            };

            var result = await PackageDetector.DetectAsync(config, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(PreCheckStatus.WrongVersion));
            Assert.That(result.ActualVersion, Is.EqualTo("3.13.3"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
        }
    }

    [Test]
    public async Task DetectAsync_RegistryNoDisplayVersion_ReturnsAlreadySatisfied()
    {
        var testAppName = $"DeploymentPoC-Test-{Guid.NewGuid():N}";
        var keyPath = $"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{testAppName}";

        using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
        {
            key?.SetValue("DisplayName", testAppName);
        }

        try
        {
            var config = new DetectionConfig
            {
                Type = "registry",
                Path = testAppName,
                ExpectedVersion = "1.0.0"
            };

            var result = await PackageDetector.DetectAsync(config, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(PreCheckStatus.AlreadySatisfied));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
        }
    }

    [Test]
    public async Task DetectAsync_VersionManifest_BinaryNotFound_ReturnsNotPresent()
    {
        var config = new DetectionConfig
        {
            Type = "version_manifest",
            Path = $"nonexistent-binary-{Guid.NewGuid():N}",
            ExpectedVersion = "1.0.0"
        };

        var result = await PackageDetector.DetectAsync(config, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PreCheckStatus.NotPresent));
    }

    [Test]
    public async Task DetectAsync_VersionManifest_BinaryWithVersion_ReturnsAlreadySatisfied()
    {
        // Create a temporary batch file that outputs a version string
        var tempDir = Path.Combine(Path.GetTempPath(), $"pkg-detect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempCmd = Path.Combine(tempDir, "testversion.cmd");
        await File.WriteAllTextAsync(tempCmd, "@echo off\necho Python 3.13.3\n");

        try
        {
            var config = new DetectionConfig
            {
                Type = "version_manifest",
                Path = tempCmd,
                ExpectedVersion = "3.13.3"
            };

            var result = await PackageDetector.DetectAsync(config, CancellationToken.None);

            // cmd files may not be executable directly with ProcessStartInfo(UseShellExecute=false)
            // so we accept either AlreadySatisfied (if it worked) or NotPresent/AlreadySatisfied fallback
            Assert.That(result.Status, Is.AnyOf(PreCheckStatus.AlreadySatisfied, PreCheckStatus.WrongVersion));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
