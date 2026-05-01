using System.IO;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Agent.Steps;

public sealed class WorkloadPreCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public static class WorkloadPreCheck
{
    public static Task<WorkloadPreCheckResult> ExecuteAsync(
        DiffResult diff,
        List<PackageAssignment> targetPackages,
        ILogger logger,
        CancellationToken ct)
    {
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
                Error = $"insufficient_temp_disk_space: required {required}, available {available}"
            });
        }

        return Task.FromResult(new WorkloadPreCheckResult
        {
            Success = true
        });
    }
}
