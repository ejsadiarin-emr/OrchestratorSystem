using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Contracts.Runtime.Probes;

public sealed class PackageDetectionRequest
{
    public Guid PackageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DetectionConfig Detection { get; set; } = new();
}

public sealed class DetectRequest
{
    public List<PackageDetectionRequest> Packages { get; set; } = new();
}
