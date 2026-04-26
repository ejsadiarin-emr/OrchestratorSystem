using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Steps;

public static class PreCheckProbe
{
    public static Task<PreCheckResult> ExecuteAsync(DetectionConfig config, CancellationToken ct)
        => PackageDetector.DetectAsync(config, ct);
}
