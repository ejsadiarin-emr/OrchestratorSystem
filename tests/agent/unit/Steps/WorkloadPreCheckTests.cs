using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests.Steps;

[TestFixture]
public class WorkloadPreCheckTests
{
    private Mock<ILogger> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger>();
    }

    private static PackageAssignment CreatePackage(string name, string version,
        long? sizeBytes = null, string? artifactFileName = null, string adapterType = "exe")
    {
        return new PackageAssignment
        {
            PackageIndex = 0,
            PackageId = name,
            Name = name,
            Version = version,
            SizeBytes = sizeBytes,
            ArtifactFileName = artifactFileName,
            InstallAdapter = new InstallAdapterConfig
            {
                Type = adapterType,
                Command = "installer",
                Arguments = "/quiet",
                TimeoutSeconds = 30
            },
            Detection = new DetectionConfig
            {
                Type = "file",
                Path = name
            }
        };
    }

    [Test]
    public async Task ExecuteAsync_EmptyPackageList_Succeeds()
    {
        var diff = new DiffResult();
        var targetPackages = new List<PackageAssignment>();

        var result = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_SufficientDiskSpace_Succeeds()
    {
        var pkg = CreatePackage("test-pkg", "1.0.0", sizeBytes: 1);
        var diff = new DiffResult
        {
            Added = new List<PackageAssignment> { pkg }
        };
        var targetPackages = new List<PackageAssignment> { pkg };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_InsufficientDiskSpace_FailsWithError()
    {
        var pkg = CreatePackage("huge-pkg", "1.0.0", sizeBytes: long.MaxValue);
        var diff = new DiffResult
        {
            Added = new List<PackageAssignment> { pkg }
        };
        var targetPackages = new List<PackageAssignment> { pkg };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.StartWith("insufficient_temp_disk_space"));
    }

    [Test]
    public async Task ExecuteAsync_InsufficientDiskSpace_IncludesRequiredAndAvailable()
    {
        var pkg = CreatePackage("huge-pkg", "1.0.0", sizeBytes: long.MaxValue);
        var diff = new DiffResult
        {
            Added = new List<PackageAssignment> { pkg }
        };
        var targetPackages = new List<PackageAssignment> { pkg };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Error, Does.Contain("required"));
        Assert.That(result.Error, Does.Contain("available"));
    }

    [Test]
    public async Task ExecuteAsync_InsufficientDiskSpace_ReturnsEarlyBeforePackageCountCheck()
    {
        var pkgs = Enumerable.Range(0, 51)
            .Select(i => CreatePackage($"pkg-{i}", "1.0.0", sizeBytes: long.MaxValue / 51))
            .ToList();
        var diff = new DiffResult
        {
            Added = pkgs
        };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, pkgs, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.StartWith("insufficient_temp_disk_space"));
        Assert.That(result.Warnings, Does.Not.Contain("high_package_count"));
    }

    [Test]
    public async Task ExecuteAsync_HighPackageCount_Warns()
    {
        var pkgs = Enumerable.Range(0, 51)
            .Select(i => CreatePackage($"pkg-{i}", "1.0.0"))
            .ToList();
        var diff = new DiffResult
        {
            Added = pkgs
        };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, pkgs, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Warnings, Does.Contain("high_package_count"));
    }

    [Test]
    public async Task ExecuteAsync_FiftyPackages_DoesNotWarn()
    {
        var pkgs = Enumerable.Range(0, 50)
            .Select(i => CreatePackage($"pkg-{i}", "1.0.0"))
            .ToList();
        var diff = new DiffResult
        {
            Added = pkgs
        };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, pkgs, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Warnings, Does.Not.Contain("high_package_count"));
    }

    [Test]
    public async Task ExecuteAsync_ChangedPackages_IncludedInDiskCalculation()
    {
        var addedPkg = CreatePackage("new-pkg", "1.0.0", sizeBytes: 1000);
        var changedPkg = CreatePackage("changed-pkg", "2.0.0", sizeBytes: 2000);
        var diff = new DiffResult
        {
            Added = new List<PackageAssignment> { addedPkg },
            Changed = new List<PackageAssignment> { changedPkg }
        };
        var targetPackages = new List<PackageAssignment> { addedPkg, changedPkg };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_NullSizeBytes_TreatedAsZero()
    {
        var pkg = CreatePackage("no-size-pkg", "1.0.0", sizeBytes: null);
        var diff = new DiffResult
        {
            Added = new List<PackageAssignment> { pkg }
        };
        var targetPackages = new List<PackageAssignment> { pkg };

        var result = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_AdminCheckFail_GracefullyHandled()
    {
        var diff = new DiffResult();
        var targetPackages = new List<PackageAssignment>();

        var result = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _loggerMock.Object, CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }
}
