using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Steps;

public static class PostInstallVerify
{
    public static async Task<VerifyResult> ExecuteAsync(DetectionConfig config, CancellationToken ct)
    {
        var result = await PackageDetector.DetectAsync(config, ct);
        return result.Status switch
        {
            PreCheckStatus.AlreadySatisfied => new VerifyResult { Success = true },
            PreCheckStatus.WrongVersion => new VerifyResult
            {
                Success = false,
                Error = result.Error ?? "version_mismatch"
            },
            PreCheckStatus.NotPresent => new VerifyResult
            {
                Success = false,
                Error = result.Error ?? "not_detected"
            },
            _ => new VerifyResult { Success = false, Error = "unknown_detection_result" }
        };
    }
}

public sealed class VerifyResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
