namespace Orchestrator.Models;

public class WorkloadRun
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadVersion { get; set; } = string.Empty;
    public WorkloadRunMode Mode { get; set; }
    public WorkloadRunStatus Status { get; set; } = WorkloadRunStatus.PENDING;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Workload? Workload { get; set; }
    public ICollection<WorkloadRunStep> Steps { get; set; } = [];
}
