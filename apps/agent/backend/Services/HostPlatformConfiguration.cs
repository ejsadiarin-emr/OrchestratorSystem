using Microsoft.Extensions.Hosting;

namespace DeploymentPoC.Agent.Services;

public enum HostServiceType
{
    None,
    WindowsService,
    Systemd
}

public sealed class HostPlatformConfiguration
{
    public HostServiceType GetServiceTypeForPlatform(string platform)
    {
        return platform.ToLowerInvariant() switch
        {
            "windows" => HostServiceType.WindowsService,
            "linux" => HostServiceType.Systemd,
            _ => HostServiceType.None
        };
    }

    public HostServiceType GetCurrentServiceType()
    {
        if (OperatingSystem.IsWindows())
            return HostServiceType.WindowsService;
        if (OperatingSystem.IsLinux())
            return HostServiceType.Systemd;
        return HostServiceType.None;
    }

    public IHostBuilder ConfigureHostForPlatform(IHostBuilder hostBuilder)
    {
        var serviceType = GetCurrentServiceType();
        switch (serviceType)
        {
            case HostServiceType.WindowsService:
                hostBuilder.UseWindowsService();
                break;
            case HostServiceType.Systemd:
                hostBuilder.UseSystemd();
                break;
        }
        return hostBuilder;
    }
}