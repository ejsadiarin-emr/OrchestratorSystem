using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Orchestrator.Contracts.Api.WorkloadRuns;

public sealed class CreateWorkloadRunResponse
{
    public Guid RunId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? RiskLevel { get; set; }
}

public sealed class WorkloadRunDetailResponse
{
    public Guid RunId { get; set; }
    public Guid WorkloadId { get; set; }
    public Guid RevisionId { get; set; }
    public string WorkloadVersion { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? RiskLevel { get; set; }
    public List<Guid> NodeIds { get; set; } = new();
}

public sealed class WorkloadRunStepsResponse
{
    public List<WorkloadRunStepDto> Steps { get; set; } = new();
}

public sealed class WorkloadRunStepDto
{
    public Guid PackageId { get; set; }
    public int PackageIndex { get; set; }
    public string StepId { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Action { get; set; } = "install";
}

public sealed class CancelWorkloadRunResponse
{
    public Guid RunId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CancelledAtUtc { get; set; }
}

public sealed class PendingWorkloadRunResponse
{
    public Guid RunId { get; set; }
    public Guid WorkloadId { get; set; }
    public string WorkloadName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public List<PendingPackageDto> Packages { get; set; } = new();
    public List<PendingPackageDto> CurrentPackages { get; set; } = new();
}

public sealed class PendingPackageDto
{
    public Guid PackageEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public InstallAdapterConfig InstallAdapter { get; set; } = new();
    public DetectionConfig Detection { get; set; } = new();
}

public sealed class RunStatusUpdateRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class TimelineEventRequest
{
    public string Step { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}
