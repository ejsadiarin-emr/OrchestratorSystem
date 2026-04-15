namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class JobStepListResponse
{
    public List<JobStepDto> Steps { get; set; } = new();
}

public sealed class JobStepDto
{
    public string StepId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public int? ReasonCode { get; set; }
    public string? TelemetryRef { get; set; }
}
