namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class NodeListResponse
{
    public List<NodeSummaryDto> Nodes { get; set; } = new();
}

public sealed class NodeSummaryDto
{
    public Guid NodeId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime LastSeenUtc { get; set; }
}

public sealed class NodeWorkloadStateResponse
{
    public Guid NodeId { get; set; }
    public Guid WorkloadId { get; set; }
    public string WorkloadRevision { get; set; } = string.Empty;
    public Guid? CurrentRevisionId { get; set; }
    public Guid RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
