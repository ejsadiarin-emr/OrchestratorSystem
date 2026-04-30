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
    public int RevisionCount { get; set; }
    public WorkloadRevisionSummaryDto? LatestRevision { get; set; }
}

public sealed class WorkloadRevisionSummaryDto
{
    public Guid RevisionId { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
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
    public List<string> PreWorkloadSteps { get; set; } = new();
    public List<string> PostWorkloadSteps { get; set; } = new();
    public string DefaultShell { get; set; } = "powershell";
    public List<WorkloadPackageDto> Packages { get; set; } = new();
}

public sealed class WorkloadPackageDto
{
    public Guid PackageId { get; set; }
    public int PackageIndex { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public List<string> PreInitSteps { get; set; } = new();
    public List<string> PostInitSteps { get; set; } = new();
}
