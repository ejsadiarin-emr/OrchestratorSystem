using DeploymentPoC.Orchestrator.Services;

namespace DeploymentPoC.Orchestrator.Models;

public sealed class UploadSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string TempDirectory { get; set; } = string.Empty;
    public ArtifactIngestManifest? Manifest { get; set; }
    public int TotalChunks { get; set; }
    public HashSet<int> ReceivedChunks { get; set; } = new();
    public string? FinalFilePath { get; set; }
    public bool IsComplete { get; set; }
}
