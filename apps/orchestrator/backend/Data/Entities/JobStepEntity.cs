namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class JobStepEntity
{
    public Guid JobStepId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public JobEntity Job { get; set; } = null!;
    public string StepId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int Sequence { get; set; }
    public int? ReasonCode { get; set; }
    public string? TelemetryRef { get; set; }
    public string? Detail { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
