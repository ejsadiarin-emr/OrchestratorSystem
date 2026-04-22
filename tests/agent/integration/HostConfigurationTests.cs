using DeploymentPoC.Agent.Services;
using NUnit.Framework;

namespace DeploymentPoC.Agent.IntegrationTests;

public sealed class HostConfigurationTests
{
    [Test]
    public void ConfigureHostBuilder_SelectsWindowsService_WhenOnWindows()
    {
        var config = new HostPlatformConfiguration();
        Assert.That(config.GetServiceTypeForPlatform("windows"), Is.EqualTo(HostServiceType.WindowsService));
    }

    [Test]
    public void ConfigureHostBuilder_SelectsSystemd_WhenOnLinux()
    {
        var config = new HostPlatformConfiguration();
        Assert.That(config.GetServiceTypeForPlatform("linux"), Is.EqualTo(HostServiceType.Systemd));
    }

    [Test]
    public void ConfigureHostBuilder_SelectsNone_WhenOnUnknownPlatform()
    {
        var config = new HostPlatformConfiguration();
        Assert.That(config.GetServiceTypeForPlatform("unknown"), Is.EqualTo(HostServiceType.None));
    }

    [Test]
    public void ConfigureHostBuilder_DefaultsToCurrentPlatform()
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