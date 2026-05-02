using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Agent.Steps;

public sealed class WorkloadPreCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public static class WorkloadPreCheck
{
    public static Task<WorkloadPreCheckResult> ExecuteAsync(
        DiffResult diff,
        List<PackageAssignment> targetPackages,
        ILogger logger,
        CancellationToken ct)
    {
        var warnings = new List<string>();

        // Admin privilege check
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                warnings.Add("not_running_as_administrator");
                logger.LogWarning("WorkloadPreCheck: not running as administrator");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WorkloadPreCheck: failed to check admin privileges");
        }

        // Disk space check (hard failure)
        long required = diff.Added.Sum(p => p.SizeBytes ?? 0)
                      + diff.Changed.Sum(p => p.SizeBytes ?? 0);

        var tempPath = Path.GetTempPath();
        var root = Path.GetPathRoot(tempPath) ?? tempPath;
        var drive = new DriveInfo(root);
        long available = drive.AvailableFreeSpace;

        logger.LogInformation(
            "WorkloadPreCheck: required {RequiredBytes} bytes, available {AvailableBytes} bytes",
            required,
            available);

        if (available < required)
        {
            return Task.FromResult(new WorkloadPreCheckResult
            {
                Success = false,
                Error = $"insufficient_temp_disk_space: required {required}, available {available}",
                Warnings = warnings
            });
        }

        // Package count check
        if (targetPackages.Count > 50)
        {
            warnings.Add("high_package_count");
            logger.LogWarning("WorkloadPreCheck: high package count {Count} > 50", targetPackages.Count);
        }

        // Process locks check
        foreach (var pkg in targetPackages)
        {
            if (!string.Equals(pkg.InstallAdapter.Type, "msi", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pkg.InstallAdapter.Type, "exe", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = pkg.ArtifactFileName;
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            var processName = Path.GetFileNameWithoutExtension(fileName);

            try
            {
                var running = Process.GetProcessesByName(processName);
                if (running.Length > 0)
                {
                    warnings.Add("process_may_be_running");
                    logger.LogWarning(
                        "WorkloadPreCheck: process {ProcessName} matching {ArtifactFileName} is running",
                        processName,
                        fileName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WorkloadPreCheck: failed to check process for {ArtifactFileName}", fileName);
            }
        }

        return Task.FromResult(new WorkloadPreCheckResult
        {
            Success = true,
            Warnings = warnings
        });
    }
}
