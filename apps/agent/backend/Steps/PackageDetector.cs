using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Win32;
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
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "missing_detection_path" });

        if (!File.Exists(config.Path))
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "file_not_found" });

        return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied });
    }

    private static Task<PreCheckResult> DetectRegistryAsync(DetectionConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Path))
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "missing_detection_path" });
        }

        var displayNameToMatch = config.Path;

        var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    var subKeyPath = hive == RegistryHive.LocalMachine
                        ? @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
                        : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

                    using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
                    if (uninstallKey is null)
                        continue;

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = uninstallKey.OpenSubKey(subKeyName);
                            if (subKey is null)
                                continue;

                            var displayName = subKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName))
                                continue;

                            if (!string.Equals(displayName, displayNameToMatch, StringComparison.OrdinalIgnoreCase))
                                continue;

                            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied });
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (System.Security.SecurityException) { }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }

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
                        Path = fullPath
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
                                Path = fullPath
                            }, ct);
                        }
                    }
                }
            }
        }

        return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
    }


}
