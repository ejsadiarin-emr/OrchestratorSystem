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

        // It's a command name — search PATH and common Windows install directories.
        var searchPaths = new List<string>();

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        searchPaths.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Search Program Files and immediate subdirectories (common for GUI installers)
            foreach (var baseDir in new[] { programFiles, programFilesX86 })
            {
                if (!string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
                {
                    searchPaths.Add(baseDir);
                    try
                    {
                        foreach (var subdir in Directory.GetDirectories(baseDir))
                        {
                            searchPaths.Add(subdir);
                        }
                    }
                    catch { /* ignore permission errors */ }
                }
            }

            // Search LocalAppData\Programs and its subdirectories (user-scoped installs)
            var localPrograms = Path.Combine(localAppData, "Programs");
            if (Directory.Exists(localPrograms))
            {
                searchPaths.Add(localPrograms);
                try
                {
                    foreach (var subdir in Directory.GetDirectories(localPrograms))
                    {
                        searchPaths.Add(subdir);
                    }
                }
                catch { /* ignore permission errors */ }
            }

            searchPaths.Add(Path.Combine(programFiles, "Git", "cmd"));
            searchPaths.Add(Path.Combine(programFilesX86, "Git", "cmd"));
            searchPaths.Add(Path.Combine(programFiles, "nodejs"));
            searchPaths.Add(Path.Combine(programFilesX86, "nodejs"));
            searchPaths.Add(Path.Combine(localAppData, "Programs", "Python"));
            searchPaths.Add(Path.Combine(programFiles, "Python310"));
            searchPaths.Add(Path.Combine(programFiles, "Python311"));
            searchPaths.Add(Path.Combine(programFiles, "Python312"));
            searchPaths.Add(Path.Combine(programFiles, "Python313"));
            searchPaths.Add(Path.Combine(programFiles, "Python314"));
        }

        // Binary name aliases for common packages
        var namesToSearch = new List<string> { path };
        if (string.Equals(path, "nodejs", StringComparison.OrdinalIgnoreCase))
        {
            namesToSearch.Insert(0, "node");
        }
        else if (string.Equals(path, "python", StringComparison.OrdinalIgnoreCase))
        {
            namesToSearch.Add("python3");
        }

        foreach (var dir in searchPaths.Distinct(StringComparer.OrdinalIgnoreCase))
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

                if (OperatingSystem.IsWindows())
                {
                    var exePath = fullPath + ".exe";
                    if (File.Exists(exePath))
                    {
                        return DetectFileAsync(new DetectionConfig
                        {
                            Type = config.Type,
                            Path = exePath,
                            ExpectedVersion = config.ExpectedVersion
                        }, ct);
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
