using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class WorkloadRunDispatcherTests
{
    [Test]
    public void BuildDetectionConfig_WithNoDetectionConfigJson_SetsExpectedVersionFromPackageVersion()
    {
        var pkg = new PackageEntity
        {
            Name = "test-pkg",
            Version = "2.1.0",
            DetectionConfigJson = ""
        };

        var config = WorkloadRunDispatcher.BuildDetectionConfig(pkg);

        Assert.That(config.ExpectedVersion, Is.EqualTo("2.1.0"));
    }

    [Test]
    public void BuildDetectionConfig_WithDetectionConfigJson_OverridesExpectedVersionWithPackageVersion()
    {
        var pkg = new PackageEntity
        {
            Name = "test-pkg",
            Version = "3.0.0",
            DetectionConfigJson = """{"type":"file","path":"/usr/bin/app","expectedVersion":"1.0.0"}"""
        };

        var config = WorkloadRunDispatcher.BuildDetectionConfig(pkg);

        Assert.That(config.ExpectedVersion, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void BuildDetectionConfig_WithNoDetectionConfigJson_DefaultsToVersionManifestType()
    {
        var pkg = new PackageEntity
        {
            Name = "test-pkg",
            Version = "1.0.0",
            DetectionConfigJson = ""
        };

        var config = WorkloadRunDispatcher.BuildDetectionConfig(pkg);

        Assert.That(config.Type, Is.EqualTo("version_manifest"));
    }
}
