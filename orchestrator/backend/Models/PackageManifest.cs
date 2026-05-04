namespace Orchestrator.Models;

public class PackageManifest
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallerFile { get; set; } = string.Empty;
    public string InstallCommand { get; set; } = string.Empty;
    public string? InstallArgs { get; set; }
    public string? UninstallCommand { get; set; }
    public string? UninstallArgs { get; set; }
    public string UpdateStrategy { get; set; } = "uninstall-then-install";
    public DetectionRule? Detection { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PackageId))
            throw new InvalidOperationException("manifest 'packageId' is required");
        if (string.IsNullOrWhiteSpace(PackageName))
            throw new InvalidOperationException("manifest 'packageName' is required");
        if (string.IsNullOrWhiteSpace(Version))
            throw new InvalidOperationException("manifest 'version' is required");
        if (string.IsNullOrWhiteSpace(InstallerFile))
            throw new InvalidOperationException("manifest 'installerFile' is required");
        if (string.IsNullOrWhiteSpace(InstallCommand))
            throw new InvalidOperationException("manifest 'installCommand' is required");
    }
}

public class DetectionRule
{
    public string? RegistryKey { get; set; }
    public string? RegistryValue { get; set; }
    public string? FilePath { get; set; }
    public string? ProductCode { get; set; }
}
