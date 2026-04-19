namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class DeprecatedEndpointResponse
{
    public string Code { get; set; } = "deprecated_endpoint";
    public string Message { get; set; } = "Use /api/workload-runs";
    public string ReplacementPath { get; set; } = "/api/workload-runs";
}
