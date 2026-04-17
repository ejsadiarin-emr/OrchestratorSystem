namespace DeploymentPoC.Orchestrator.Contracts.Api.Workloads;

public sealed class WorkloadListResponse
{
    public List<WorkloadSummaryDto> Workloads { get; set; } = new();
}

public sealed class WorkloadSummaryDto
{
    public Guid WorkloadId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class WorkloadDetailResponse
{
    public Guid WorkloadId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<WorkloadRevisionDto> Revisions { get; set; } = new();
}

public sealed class WorkloadRevisionDto
{
    public Guid RevisionId { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<WorkloadPackageDto> Packages { get; set; } = new();
}

public sealed class WorkloadPackageDto
{
    public Guid PackageId { get; set; }
    public int PackageIndex { get; set; }
}
