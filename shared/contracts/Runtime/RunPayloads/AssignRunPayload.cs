namespace DeploymentPoC.Contracts.Runtime.RunPayloads;

public sealed class AssignRunPayload
{
    public Guid RunId { get; set; }
    public Guid WorkloadId { get; set; }
    public string WorkloadName { get; set; } = string.Empty;
    public Guid RevisionId { get; set; }
    public string RevisionVersion { get; set; } = string.Empty;
    public string Mode { get; set; } = "install";
    public Guid NodeId { get; set; }
    public List<PackageAssignment> Packages { get; set; } = new();
    public List<string> PreUpgradeActions { get; set; } = new();
}
