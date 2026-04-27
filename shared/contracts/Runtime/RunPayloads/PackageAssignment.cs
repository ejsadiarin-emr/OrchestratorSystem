namespace DeploymentPoC.Contracts.Runtime.RunPayloads;

public sealed class PackageAssignment
{
    public int PackageIndex { get; set; }
    public Guid PackageEntityId { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? ArtifactFileName { get; set; }
    public string? DownloadUrl { get; set; }
    public InstallAdapterConfig InstallAdapter { get; set; } = new();
    public DetectionConfig Detection { get; set; } = new();
}
