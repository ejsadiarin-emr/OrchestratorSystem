namespace DeploymentPoC.Contracts.Runtime.Probes;

public enum PreCheckStatus
{
    AlreadySatisfied,
    WrongVersion,
    NotPresent
}

public sealed class PackageDetectionResult
{
    public Guid PackageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PreCheckStatus Status { get; set; }
    public string? ActualVersion { get; set; }
}

public sealed class DiskInfo
{
    public long FreeBytes { get; set; }
    public long TotalBytes { get; set; }
    public string Drive { get; set; } = string.Empty;
}

public sealed class NodeDetectResponse
{
    public List<PackageDetectionResult> Results { get; set; } = new();
    public DiskInfo DiskInfo { get; set; } = new();
}
