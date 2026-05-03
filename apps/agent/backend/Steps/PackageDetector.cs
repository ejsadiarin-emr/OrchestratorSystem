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

        return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = null });
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

                            var displayVersion = subKey.GetValue("DisplayVersion") as string;
                            if (string.IsNullOrWhiteSpace(displayVersion) || string.IsNullOrWhiteSpace(config.ExpectedVersion))
                            {
                                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = displayVersion });
                            }

                            var versionMatches = VersionComparer.Matches(config.ExpectedVersion, displayVersion);
                            if (versionMatches)
                            {
                                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = displayVersion });
                            }
                            else
                            {
                                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.WrongVersion, ActualVersion = displayVersion });
                            }
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
        string? resolvedPath = null;

        // If it's a full path, do a simple file check.
        if (path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar))
        {
            if (File.Exists(path))
            {
                resolvedPath = path;
            }
            else
            {
                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent, Error = "file_not_found" });
            }
        }
        else
        {
            // It's a command name — search PATH and common install directories dynamically.
            // No package-specific paths or aliases are hardcoded; manifests must specify
            // the exact binary name in detection.path (e.g. "node" not "nodejs").
            var namesToSearch = new List<string> { path };
            if (OperatingSystem.IsWindows())
            {
                namesToSearch.Add(path + ".exe");
            }

            var candidatePaths = new List<string>();
            var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string candidate)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && seenCandidates.Add(candidate))
                {
                    candidatePaths.Add(candidate);
                }
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
                        AddCandidate(fullPath);
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
                                AddCandidate(fullPath);
                            }
                        }
                    }
                }
            }

            if (candidatePaths.Count == 0)
            {
                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
            }

            if (!string.IsNullOrWhiteSpace(config.ExpectedVersion))
            {
                foreach (var candidate in candidatePaths)
                {
                    var candidateVersion = TryGetVersionFromBinary(candidate);
                    if (string.IsNullOrWhiteSpace(candidateVersion))
                        continue;

                    if (VersionComparer.Matches(config.ExpectedVersion, candidateVersion))
                    {
                        return Task.FromResult(new PreCheckResult
                        {
                            Status = PreCheckStatus.AlreadySatisfied,
                            ActualVersion = candidateVersion
                        });
                    }
                }
            }

            resolvedPath = candidatePaths[0];
        }

        if (resolvedPath is null)
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
        }

        // Run the binary with --version and parse the output
        try
        {
            var versionString = TryGetVersionFromBinary(resolvedPath);
            if (string.IsNullOrWhiteSpace(versionString))
            {
                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = null });
            }

            if (string.IsNullOrWhiteSpace(config.ExpectedVersion))
            {
                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = versionString });
            }

            var versionMatches = VersionComparer.Matches(config.ExpectedVersion, versionString);
            if (versionMatches)
            {
                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = versionString });
            }
            else
            {
                return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.WrongVersion, ActualVersion = versionString });
            }
        }
        catch
        {
            return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = null });
        }
    }

    private static string? TryGetVersionFromBinary(string binaryPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var parsedVersion = VersionComparer.NormalizeVersion(stdout).Length > 0
                ? stdout.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(parsedVersion))
            {
                return null;
            }

            var firstVersionMatch = System.Text.RegularExpressions.Regex.Match(parsedVersion, @"\d+(?:\.\d+)*");
            return firstVersionMatch.Success ? firstVersionMatch.Value : parsedVersion;
        }
        catch
        {
            return null;
        }
    }


}
