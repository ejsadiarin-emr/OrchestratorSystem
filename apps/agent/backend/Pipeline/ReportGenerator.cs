using System.Text;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Pipeline;

public static class ReportGenerator
{
    public static string Generate(
        PipelineContext context,
        Dictionary<string, PreCheckResult>? preCheckResults,
        Dictionary<string, PostVerifyResult>? postVerifyResults)
    {
        var sb = new StringBuilder();
        var payload = context.Payload;
        var allSucceeded = context.AllStepsSucceeded;
        var error = context.FirstError;

        sb.AppendLine("=== Deployment Report ===");
        sb.AppendLine($"Run ID:       {context.RunId}");
        sb.AppendLine($"Workload:     {payload.WorkloadName} (revision {payload.RevisionVersion})");
        sb.AppendLine($"Node:         {payload.NodeId}");
        sb.AppendLine($"Mode:         {payload.Mode}");
        sb.AppendLine($"Started:      {context.PipelineStartUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"Completed:    {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"Result:       {(allSucceeded ? "SUCCESS" : $"FAILED [{error ?? "unknown"}]")}");
        sb.AppendLine();

        var targetPackages = payload.Packages.OrderBy(p => p.PackageIndex).ToList();

        sb.AppendLine("--- Pre-Run Detection ---");
        sb.AppendLine("Package         Expected    Detected    Status");
        if (preCheckResults != null)
        {
            foreach (var package in targetPackages)
            {
                var expected = TruncateVersion(package.Detection.ExpectedVersion);
                if (preCheckResults.TryGetValue(package.Name, out var result))
                {
                    var detected = TruncateVersion(result.ActualVersion);
                    sb.AppendLine($"{TruncateName(package.Name),-16}{expected,-12}{detected,-12}{result.Status}");
                }
                else
                {
                    sb.AppendLine($"{TruncateName(package.Name),-16}{expected,-12}{"(unknown)",-12}Unknown");
                }
            }
        }
        else
        {
            foreach (var package in targetPackages)
            {
                var expected = TruncateVersion(package.Detection.ExpectedVersion);
                sb.AppendLine($"{TruncateName(package.Name),-16}{expected,-12}{"(skipped)",-12}ForceInstall");
            }
        }
        sb.AppendLine();

        sb.AppendLine("--- Post-Run Detection ---");
        sb.AppendLine("Package         Expected    Detected    Status");
        foreach (var package in targetPackages)
        {
            var expected = TruncateVersion(package.Detection.ExpectedVersion);
            if (postVerifyResults != null && postVerifyResults.TryGetValue(package.Name, out var pvr))
            {
                var detected = TruncateVersion(pvr.ActualVersion);
                var status = pvr.Success ? "AlreadySatisfied" : "NotDetected";
                if (!string.IsNullOrEmpty(pvr.Error))
                    status += $" ({pvr.Error})";
                sb.AppendLine($"{TruncateName(package.Name),-16}{expected,-12}{detected,-12}{status}");
            }
            else if (IsPackageReached(context, package))
            {
                if (allSucceeded)
                {
                    sb.AppendLine($"{TruncateName(package.Name),-16}{expected,-12}{"-",-12}AssumedInstalled");
                }
                else
                {
                    sb.AppendLine($"{TruncateName(package.Name),-16}{expected,-12}{"-",-12}Unknown");
                }
            }
            else
            {
                sb.AppendLine($"{TruncateName(package.Name),-16}{expected,-12}{"(not reached)",-12}Skipped");
            }
        }
        sb.AppendLine();

        var summary = ComputeSummary(context, preCheckResults, postVerifyResults, targetPackages);
        sb.AppendLine("--- Run Summary ---");
        sb.AppendLine($"Packages processed:  {targetPackages.Count}");
        sb.AppendLine($"  Installed:         {summary.InstalledCount}{FormatPackageList(summary.Installed)}");
        sb.AppendLine($"  Updated:           {summary.UpdatedCount}{FormatPackageList(summary.Updated)}");
        sb.AppendLine($"  Uninstalled:       {summary.UninstalledCount}{FormatPackageList(summary.Uninstalled)}");
        sb.AppendLine($"  Unchanged:         {summary.UnchangedCount}{FormatPackageList(summary.Unchanged)}");
        sb.AppendLine($"  Failed/Skipped:    {summary.FailedCount}{FormatPackageList(summary.Failed)}");
        sb.AppendLine();

        sb.AppendLine("--- Step Timeline ---");
        sb.AppendLine("Step                Package       Status      Detail");
        foreach (var step in context.StepHistory)
        {
            var status = step.Success ? "Completed" : "Failed";
            var detail = step.Error ?? "";
            sb.AppendLine($"{TruncateStepName(step.StepName),-20}{TruncateName(step.PackageId),-13}{status,-12}{detail}");
        }

        return sb.ToString();
    }

    private static bool IsPackageReached(PipelineContext context, PackageAssignment package)
    {
        return context.StepHistory.Any(s =>
            (s.PackageId == package.PackageId || s.PackageId == package.Name) &&
            (s.StepName == "InstallOrUpgrade" || s.StepName == "UninstallPackage"));
    }

    private static RunSummary ComputeSummary(
        PipelineContext context,
        Dictionary<string, PreCheckResult>? preCheckResults,
        Dictionary<string, PostVerifyResult>? postVerifyResults,
        List<PackageAssignment> targetPackages)
    {
        var summary = new RunSummary();
        var isUninstall = string.Equals(context.Payload.Mode, "uninstall", StringComparison.OrdinalIgnoreCase);

        foreach (var package in targetPackages)
        {
            var installed = IsPackageReached(context, package);
            var installSucceeded = context.StepHistory.Any(s =>
                (s.PackageId == package.PackageId || s.PackageId == package.Name) &&
                s.StepName == "InstallOrUpgrade" && s.Success);

            var uninstallSucceeded = context.StepHistory.Any(s =>
                (s.PackageId == package.PackageId || s.PackageId == package.Name) &&
                s.StepName == "UninstallPackage" && s.Success);

            if (isUninstall)
            {
                if (uninstallSucceeded)
                {
                    summary.UninstalledCount++;
                    summary.Uninstalled.Add(package);
                }
                else if (installed)
                {
                    summary.FailedCount++;
                    summary.Failed.Add(package);
                }
                else
                {
                    summary.UnchangedCount++;
                    summary.Unchanged.Add(package);
                }
            }
            else
            {
                if (installSucceeded)
                {
                    var wasPresent = preCheckResults?.TryGetValue(package.Name, out var preCheck) == true
                        && preCheck.Status != PreCheckStatus.NotPresent;

                    if (wasPresent)
                    {
                        summary.UpdatedCount++;
                        summary.Updated.Add(package);
                    }
                    else
                    {
                        summary.InstalledCount++;
                        summary.Installed.Add(package);
                    }
                }
                else if (installed)
                {
                    summary.FailedCount++;
                    summary.Failed.Add(package);
                }
                else
                {
                    var skipped = context.StepHistory.Any(s =>
                        (s.PackageId == package.PackageId || s.PackageId == package.Name) &&
                        s.StepName == "PreCheckSkipped" && s.Success);
                    if (skipped)
                    {
                        summary.UnchangedCount++;
                        summary.Unchanged.Add(package);
                    }
                    else
                    {
                        summary.FailedCount++;
                        summary.Failed.Add(package);
                    }
                }
            }
        }

        return summary;
    }

    private static string FormatPackageList(List<PackageAssignment> packages)
    {
        if (packages.Count == 0) return "";
        var entries = packages.Select(p => $"{p.Name} {p.Version}");
        return " (" + string.Join(", ", entries) + ")";
    }

    private static string TruncateName(string name)
    {
        return name.Length > 16 ? name[..13] + "..." : name;
    }

    private static string TruncateVersion(string? version)
    {
        if (string.IsNullOrEmpty(version)) return "(none)";
        return version.Length > 12 ? version[..9] + "..." : version;
    }

    private static string TruncateStepName(string name)
    {
        return name.Length > 20 ? name[..17] + "..." : name;
    }
}

public sealed class PostVerifyResult
{
    public bool Success { get; set; }
    public string? ActualVersion { get; set; }
    public string? Error { get; set; }
}

internal sealed class RunSummary
{
    public int InstalledCount;
    public int UpdatedCount;
    public int UninstalledCount;
    public int UnchangedCount;
    public int FailedCount;
    public List<PackageAssignment> Installed = new();
    public List<PackageAssignment> Updated = new();
    public List<PackageAssignment> Uninstalled = new();
    public List<PackageAssignment> Unchanged = new();
    public List<PackageAssignment> Failed = new();
}
