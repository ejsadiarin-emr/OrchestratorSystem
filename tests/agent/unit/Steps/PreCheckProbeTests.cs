using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests.Steps;

[TestFixture]
public class PreCheckProbeTests
{
    [Test]
    public async Task ExecuteAsync_FileDetection_ExistingFile_ReturnsAlreadySatisfied()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"precheck-probe-test-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "test");

        try
        {
            var config = new DetectionConfig
            {
                Type = "file",
                Path = tempFile,
                ExpectedVersion = "1.0.0"
            };

            var result = await PreCheckProbe.ExecuteAsync(config, CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(PreCheckStatus.AlreadySatisfied));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public async Task ExecuteAsync_FileDetection_MissingFile_ReturnsNotPresent()
    {
        var config = new DetectionConfig
        {
            Type = "file",
            Path = Path.Combine(Path.GetTempPath(), $"precheck-probe-missing-{Guid.NewGuid():N}.txt"),
            ExpectedVersion = "1.0.0"
        };

        var result = await PreCheckProbe.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PreCheckStatus.NotPresent));
    }

    [Test]
    public async Task ExecuteAsync_NullConfig_ReturnsNotPresent()
    {
        var result = await PreCheckProbe.ExecuteAsync(null!, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PreCheckStatus.NotPresent));
        Assert.That(result.Error, Is.EqualTo("invalid_config"));
    }

    [Test]
    public async Task ExecuteAsync_EmptyDetectionType_ReturnsNotPresent()
    {
        var config = new DetectionConfig
        {
            Type = "",
            Path = "test",
            ExpectedVersion = "1.0.0"
        };

        var result = await PreCheckProbe.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PreCheckStatus.NotPresent));
        Assert.That(result.Error, Is.EqualTo("missing_detection_type"));
    }

    [Test]
    public async Task ExecuteAsync_UnsupportedDetectionType_ReturnsNotPresent()
    {
        var config = new DetectionConfig
        {
            Type = "custom_probe",
            Path = "test",
            ExpectedVersion = "1.0.0"
        };

        var result = await PreCheckProbe.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PreCheckStatus.NotPresent));
        Assert.That(result.Error, Is.EqualTo("unsupported_detection_type:custom_probe"));
    }

    [Test]
    public async Task ExecuteAsync_VersionManifest_MissingBinary_ReturnsNotPresent()
    {
        var config = new DetectionConfig
        {
            Type = "version_manifest",
            Path = $"nonexistent-binary-{Guid.NewGuid():N}",
            ExpectedVersion = "1.0.0"
        };

        var result = await PreCheckProbe.ExecuteAsync(config, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(PreCheckStatus.NotPresent));
    }

    [Test]
    public void ExecuteAsync_CancelledToken_DoesNotThrow()
    {
        var config = new DetectionConfig
        {
            Type = "file",
            Path = Path.Combine(Path.GetTempPath(), $"precheck-probe-ct-{Guid.NewGuid():N}.txt"),
            ExpectedVersion = "1.0.0"
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.DoesNotThrowAsync(async () => await PreCheckProbe.ExecuteAsync(config, cts.Token));
    }
}
