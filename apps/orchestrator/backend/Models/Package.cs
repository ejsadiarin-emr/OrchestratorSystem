using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Models;

public class Package
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string InstallType { get; set; } = string.Empty;
    public string InstallArgs { get; set; } = string.Empty;
    public string UninstallCommand { get; set; } = string.Empty;
    public string UninstallArgs { get; set; } = string.Empty;
    public string UpgradeBehavior { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreatePackageRequest
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Version { get; set; } = string.Empty;

    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public string SourcePath { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string InstallType { get; set; } = string.Empty;

    [StringLength(2048)]
    public string InstallArgs { get; set; } = string.Empty;

    [StringLength(2048)]
    public string UninstallCommand { get; set; } = string.Empty;

    [StringLength(2048)]
    public string UninstallArgs { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string UpgradeBehavior { get; set; } = string.Empty;
}
