using System;

namespace DeploymentPoC.Orchestrator.Data.Entities;

public class WorkloadRunTimelineEntity
{
    public Guid TimelineId { get; set; }
    public Guid RunId { get; set; }
    public Guid NodeId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string? PackageId { get; set; }
    public int? PackageIndex { get; set; }
    public string? StepName { get; set; }
    public string? Status { get; set; }
    public string? Detail { get; set; }
    public DateTime AtUtc { get; set; }
}
