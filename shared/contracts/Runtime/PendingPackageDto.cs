using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Contracts.Runtime;

public sealed class PendingPackageDto
{
    public Guid PackageEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? ExpectedSha256 { get; set; }

    // TODO: Orchestrator endpoint must also return these fields
    public InstallAdapterConfig InstallAdapter { get; set; } = new();
    public DetectionConfig Detection { get; set; } = new();
}
