using System.Reflection;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class WorkloadRunDispatcherTests
{
    private static MethodInfo GetBuildDetectionConfigMethod()
    {
        var method = typeof(WorkloadRunDispatcher).GetMethod("BuildDetectionConfig",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        return method!;
    }

    [Test]
    public void BuildDetectionConfig_WithNoDetectionConfigJson_SetsExpectedVersionFromPackageVersion()
    {
        var method = GetBuildDetectionConfigMethod();
        var pkg = new PackageEntity
        {
            Name = "test-pkg",
            Version = "2.1.0",
            DetectionConfigJson = ""
        };

        var result = method.Invoke(null, new object?[] { pkg });

        Assert.That(result, Is.TypeOf<DetectionConfig>());
        var config = (DetectionConfig)result!;
        Assert.That(config.ExpectedVersion, Is.EqualTo("2.1.0"));
    }

    [Test]
    public void BuildDetectionConfig_WithDetectionConfigJson_OverridesExpectedVersionWithPackageVersion()
    {
        var method = GetBuildDetectionConfigMethod();
        var pkg = new PackageEntity
        {
            Name = "test-pkg",
            Version = "3.0.0",
            DetectionConfigJson = """{"type":"file","path":"/usr/bin/app","expectedVersion":"1.0.0"}"""
        };

        var result = method.Invoke(null, new object?[] { pkg });

        Assert.That(result, Is.TypeOf<DetectionConfig>());
        var config = (DetectionConfig)result!;
        Assert.That(config.ExpectedVersion, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void BuildDetectionConfig_WithNoDetectionConfigJson_DefaultsToVersionManifestType()
    {
        var method = GetBuildDetectionConfigMethod();
        var pkg = new PackageEntity
        {
            Name = "test-pkg",
            Version = "1.0.0",
            DetectionConfigJson = ""
        };

        var result = method.Invoke(null, new object?[] { pkg });

        Assert.That(result, Is.TypeOf<DetectionConfig>());
        var config = (DetectionConfig)result!;
        Assert.That(config.Type, Is.EqualTo("version_manifest"));
    }
}
