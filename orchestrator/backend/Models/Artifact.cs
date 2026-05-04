namespace Orchestrator.Models;

public class Artifact
{
    public int Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallerFile { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string BinaryPath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
