namespace DeploymentPoC.Contracts.Runtime.RunPayloads;

public sealed class StepStatusPayload
{
    public string StepName { get; set; } = string.Empty;
    public int PackageIndex { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class FinalizationPayload
{
    public string Result { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int StepCount { get; set; }
    public string? Report { get; set; }
    public int? ReasonCode { get; set; }
}
