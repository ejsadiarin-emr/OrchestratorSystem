namespace Orchestrator.Models;

public class WorkloadRunStep
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public WorkloadRunStepAction Action { get; set; }
    public WorkloadRunStepStatus Status { get; set; }
    public string? Message { get; set; }
    public int? ExitCode { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public WorkloadRun Run { get; set; } = null!;
}
