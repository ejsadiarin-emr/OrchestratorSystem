using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Contracts.Runtime;

public sealed class PendingWorkloadRunResponse
{
    public Guid RunId { get; set; }
    public Guid WorkloadId { get; set; }
    public string WorkloadName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public List<PendingPackageDto> Packages { get; set; } = new();
    public List<PendingPackageDto> CurrentPackages { get; set; } = new();
}
