namespace DeploymentPoC.Agent.Steps;

public sealed class InitStepResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? ErrorOutput { get; init; }
}
