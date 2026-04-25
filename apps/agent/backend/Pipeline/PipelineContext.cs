using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Pipeline;

public sealed class PipelineContext
{
    public required AssignRunPayload Payload { get; init; }
    public required string OrchestratorBaseUrl { get; init; }
    public required string AgentId { get; init; }
    public required string RunId { get; init; }
    public required int Sequence { get; init; }

    public List<PackageAssignment> CurrentPackages { get; set; } = new();

    public List<StepRecord> StepHistory { get; } = new();

    public void RecordStep(string stepName, int packageIndex, string packageId, bool success, string? error = null)
    {
        StepHistory.Add(new StepRecord
        {
            StepName = stepName,
            PackageIndex = packageIndex,
            PackageId = packageId,
            Success = success,
            Error = error,
            TimestampUtc = DateTime.UtcNow
        });
    }

    public bool AllStepsSucceeded => StepHistory.Count > 0 && StepHistory.All(s => s.Success);

    public string? FirstError => StepHistory.FirstOrDefault(s => !s.Success)?.Error;
}

public sealed class StepRecord
{
    public string StepName { get; set; } = string.Empty;
    public int PackageIndex { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime TimestampUtc { get; set; }
}
