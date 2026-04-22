using DeploymentPoC.Contracts.Runtime.RunPayloads;
using System.Diagnostics;

namespace DeploymentPoC.Agent.Steps;

public static class PostInstallVerify
{
    public static Task<VerifyResult> ExecuteAsync(DetectionConfig config, CancellationToken ct)
    {
        if (config is null)
        {
            return Task.FromResult(new VerifyResult { Success = false, Error = "invalid_config" });
        }

        if (string.IsNullOrWhiteSpace(config.Type))
        {
            return Task.FromResult(new VerifyResult { Success = false, Error = "missing_detection_type" });
        }

        return config.Type.ToLowerInvariant() switch
        {
            "file" => VerifyFileAsync(config, ct),
            "registry" => VerifyRegistryAsync(config, ct),
            _ => Task.FromResult(new VerifyResult { Success = false, Error = $"unsupported_detection_type:{config.Type}" })
        };
    }

    private static Task<VerifyResult> VerifyFileAsync(DetectionConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
        {
            return Task.FromResult(new VerifyResult { Success = false, Error = "missing_detection_path" });
        }

        if (!File.Exists(config.Path))
        {
            return Task.FromResult(new VerifyResult { Success = false, Error = "file_not_found" });
        }

        // If expected version is specified, try to read it from file version info
        if (!string.IsNullOrWhiteSpace(config.ExpectedVersion))
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(config.Path);
                var actualVersion = versionInfo.FileVersion ?? versionInfo.ProductVersion;
                if (string.IsNullOrWhiteSpace(actualVersion))
                {
                    return Task.FromResult(new VerifyResult { Success = false, Error = "version_not_readable" });
                }

                if (!VersionEquals(actualVersion, config.ExpectedVersion))
                {
                    return Task.FromResult(new VerifyResult
                    {
                        Success = false,
                        Error = $"version_mismatch: expected {config.ExpectedVersion}, got {actualVersion}"
                    });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new VerifyResult { Success = false, Error = $"version_check_error: {ex.Message}" });
            }
        }

        return Task.FromResult(new VerifyResult { Success = true });
    }

    private static Task<VerifyResult> VerifyRegistryAsync(DetectionConfig config, CancellationToken ct)
    {
        // PoC Phase 1: registry detection is a stub.
        // Full implementation would read registry key and compare value.
        return Task.FromResult(new VerifyResult { Success = true });
    }

    private static bool VersionEquals(string actual, string expected)
    {
        // Normalize and compare version strings
        var a = NormalizeVersion(actual);
        var b = NormalizeVersion(expected);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version)
    {
        var v = version.Trim();
        // Remove trailing .0 segments for loose comparison (1.0.0.0 == 1.0.0)
        while (v.EndsWith(".0", StringComparison.Ordinal) && v.Count(c => c == '.') > 1)
        {
            v = v[..^2];
        }
        return v;
    }
}

public sealed class VerifyResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
