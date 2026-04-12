namespace DeploymentPoC.Orchestrator.Models;

public class Package
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string InstallType { get; set; } = string.Empty;
    public string InstallArgs { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreatePackageRequest
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string InstallType { get; set; } = string.Empty;
    public string InstallArgs { get; set; } = string.Empty;
}
