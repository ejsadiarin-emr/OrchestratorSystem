using DeploymentPoC.Agent.Services;
using NUnit.Framework;

namespace DeploymentPoC.Agent.IntegrationTests;

public sealed class HostConfigurationTests
{
    [Test]
    public void GetServiceTypeForPlatform_WindowsPlatform_ReturnsWindowsService()
    {
        var config = new HostPlatformConfiguration();
        Assert.That(config.GetServiceTypeForPlatform("windows"), Is.EqualTo(HostServiceType.WindowsService));
    }

    [Test]
    public void GetServiceTypeForPlatform_LinuxPlatform_ReturnsSystemd()
    {
        var config = new HostPlatformConfiguration();
        Assert.That(config.GetServiceTypeForPlatform("linux"), Is.EqualTo(HostServiceType.Systemd));
    }

    [Test]
    public void GetServiceTypeForPlatform_UnknownPlatform_ReturnsNone()
    {
        var config = new HostPlatformConfiguration();
        Assert.That(config.GetServiceTypeForPlatform("unknown"), Is.EqualTo(HostServiceType.None));
    }

    [Test]
    public void GetCurrentServiceType_OnCurrentPlatform_ReturnsCorrectType()
    {
        var config = new HostPlatformConfiguration();
        var result = config.GetCurrentServiceType();
        if (OperatingSystem.IsWindows())
        {
            Assert.That(result, Is.EqualTo(HostServiceType.WindowsService));
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.That(result, Is.EqualTo(HostServiceType.Systemd));
        }
        else
        {
            Assert.That(result, Is.EqualTo(HostServiceType.None));
        }
    }
}