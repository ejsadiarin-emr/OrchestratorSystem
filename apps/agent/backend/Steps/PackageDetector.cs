using DeploymentPoC.Contracts.Runtime.RunPayloads;
using System.Diagnostics;

namespace DeploymentPoC.Agent.Steps;

public enum PreCheckStatus
{
    AlreadySatisfied,
    WrongVersion,
    NotPresent
}

public sealed class PreCheckResult
{
    public PreCheckStatus Status { get; set; }
    public string? ActualVersion { get; set; }
    public string? Error { get; set; }
}

public static class PackageDetector
{
    public static Task<PreCheckResult> DetectAsync(DetectionConfig config, CancellationToken ct)
    {
        if (config is null)
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "invalid_config" });
        }

        if (string.IsNullOrWhiteSpace(config.Type))
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "missing_detection_type" });
        }

        return config.Type.ToLowerInvariant() switch
        {
            "file" => DetectFileAsync(config, ct),
            "registry" => DetectRegistryAsync(config, ct),
            "version_manifest" => DetectVersionManifestAsync(config, ct),
            _ => Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = $"unsupported_detection_type:{config.Type}" })
        };
    }

    private static Task<PreCheckResult> DetectFileAsync(DetectionConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "missing_detection_path" });
        }

        if (!File.Exists(config.Path))
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "file_not_found" });
        }

        if (!string.IsNullOrWhiteSpace(config.ExpectedVersion))
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(config.Path);
                var actualVersion = versionInfo.FileVersion ?? versionInfo.ProductVersion;
                if (string.IsNullOrWhiteSpace(actualVersion))
                {
                    return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "version_not_readable" });
                }

                if (!VersionEquals(actualVersion, config.ExpectedVersion))
                {
                    return Task.FromResult(new PreCheckResult
                    {
                        Status = PreCheckStatus.WrongVersion,
                        ActualVersion = actualVersion,
                        Error = $"version_mismatch: expected {config.ExpectedVersion}, got {actualVersion}"
                    });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = $"version_check_error: {ex.Message}" });
            }
        }

        return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied });
    }

    private static Task<PreCheckResult> DetectRegistryAsync(DetectionConfig config, CancellationToken ct)
    {
        // PoC Phase 1: registry detection is a stub.
        // Conservative fallback: assume not present so the package is actually installed.
        return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
    }

    private static Task<PreCheckResult> DetectVersionManifestAsync(DetectionConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
        }

        var path = config.Path;

        // If it's a full path, do a simple file check.
        if (path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(path))
            {
                return DetectFileAsync(config, ct);
            }
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "file_not_found" });
        }

        // It's a command name — search PATH and common install directories dynamically.
        // No package-specific paths or aliases are hardcoded; manifests must specify
        // the exact binary name in detection.path (e.g. "node" not "nodejs").
        var namesToSearch = new List<string> { path };
        if (OperatingSystem.IsWindows())
        {
            namesToSearch.Add(path + ".exe");
        }

        // 1. Search PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in namesToSearch)
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath))
                {
                    return DetectFileAsync(new DetectionConfig
                    {
                        Type = config.Type,
                        Path = fullPath,
                        ExpectedVersion = config.ExpectedVersion
                    }, ct);
                }
            }
        }

        // 2. Search common Windows install roots and their immediate subdirectories.
        // This covers GUI installers that drop into C:\Program Files\{ProductName}.
        if (OperatingSystem.IsWindows())
        {
            var searchRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            foreach (var root in searchRoots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)))
            {
                var directoriesToSearch = new List<string> { root };
                try
                {
                    directoriesToSearch.AddRange(Directory.GetDirectories(root));
                }
                catch { /* ignore permission errors */ }

                foreach (var dir in directoriesToSearch)
                {
                    foreach (var name in namesToSearch)
                    {
                        var fullPath = Path.Combine(dir, name);
                        if (File.Exists(fullPath))
                        {
                            return DetectFileAsync(new DetectionConfig
                            {
                                Type = config.Type,
                                Path = fullPath,
                                ExpectedVersion = config.ExpectedVersion
                            }, ct);
                        }
                    }
                }
            }
        }

        return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
    }

    private static bool VersionEquals(string actual, string expected)
    {
        var a = NormalizeVersion(actual);
        var b = NormalizeVersion(expected);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string version)
    {
        var v = version.Trim();

        // Strip leading comparison operators (==, >=, <=, >, <, =)
        var opMatch = System.Text.RegularExpressions.Regex.Match(v, @"^(==|>=|<=|>|<|=)");
        if (opMatch.Success)
        {
            v = v[opMatch.Length..];
        }

        // Strip trailing .0 segments
        while (v.EndsWith(".0", StringComparison.Ordinal) && v.Count(c => c == '.') > 1)
        {
            v = v[..^2];
        }

        return v;
    }
}
