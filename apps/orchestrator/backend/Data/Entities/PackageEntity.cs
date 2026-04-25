namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class PackageEntity
{
    public Guid PackageId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string InstallType { get; set; } = string.Empty;
    public string InstallArgs { get; set; } = string.Empty;
    public string UninstallArgs { get; set; } = string.Empty;
    public string ExpectedExitCodesJson { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 300;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
