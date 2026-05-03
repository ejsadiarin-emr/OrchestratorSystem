using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Steps;

public static class PostInstallVerify
{
    public static async Task<VerifyResult> ExecuteAsync(DetectionConfig config, CancellationToken ct)
    {
        var result = await PackageDetector.DetectAsync(config, ct);

        var hasExpectedVersion = !string.IsNullOrWhiteSpace(config.ExpectedVersion);
        var versionMatches = result.Status == PreCheckStatus.AlreadySatisfied
            && (!hasExpectedVersion || VersionComparer.Matches(config.ExpectedVersion, result.ActualVersion));

        if (versionMatches)
        {
            return new VerifyResult { Success = true };
        }

        return new VerifyResult
        {
            Success = false,
            Error = result.Status == PreCheckStatus.NotPresent
                ? "not_detected"
                : $"version_mismatch (expected {config.ExpectedVersion}, actual {result.ActualVersion})"
        };
    }
}

public sealed class VerifyResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
