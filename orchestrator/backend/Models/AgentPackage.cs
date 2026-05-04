namespace Orchestrator.Models;

public class AgentPackage
{
    public string AgentId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public AgentPackageStatus Status { get; set; }
    public AgentNode Agent { get; set; } = null!;
}
