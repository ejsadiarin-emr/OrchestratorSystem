using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Pipeline;

public static class DiffEngine
{
    public static DiffResult ComputeDiff(List<PackageAssignment> current, List<PackageAssignment> target)
        => ComputeDiff(current, target, null, null);

    public static DiffResult ComputeDiff(
        List<PackageAssignment> current,
        List<PackageAssignment> target,
        Dictionary<string, PreCheckResult>? preCheckResults)
        => ComputeDiff(current, target, preCheckResults, null);

    public static DiffResult ComputeDiff(
        List<PackageAssignment> current,
        List<PackageAssignment> target,
        Dictionary<string, PreCheckResult>? preCheckResults,
        string? mode)
    {
        var currentByName = current.ToDictionary(p => p.Name);
        var targetByName = target.ToDictionary(p => p.Name);

        var added = target.Where(p => !currentByName.ContainsKey(p.Name)).ToList();
        var removed = current.Where(p => !targetByName.ContainsKey(p.Name)).ToList();
        var changed = target.Where(p => currentByName.TryGetValue(p.Name, out var c) && c.Version != p.Version).ToList();
        var unchanged = target.Where(p => currentByName.TryGetValue(p.Name, out var c) && c.Version == p.Version).ToList();

        // In uninstall mode, all target packages represent what to remove.
        // Packages with different versions should be reported as Removed, not Changed.
        if (string.Equals(mode, "uninstall", StringComparison.OrdinalIgnoreCase))
        {
            removed = removed.Concat(changed).Concat(unchanged).ToList();
            changed = new List<PackageAssignment>();
            added = new List<PackageAssignment>();
            unchanged = new List<PackageAssignment>();
        }

        if (preCheckResults is not null && preCheckResults.Count > 0)
        {
            ApplyPreCheckOverrides(added, changed, unchanged, preCheckResults);
        }

        return new DiffResult
        {
            Added = added,
            Removed = removed,
            Changed = changed,
            Unchanged = unchanged
        };
    }

    private static void ApplyPreCheckOverrides(
        List<PackageAssignment> added,
        List<PackageAssignment> changed,
        List<PackageAssignment> unchanged,
        Dictionary<string, PreCheckResult> preCheckResults)
    {
        // NOTE: We intentionally do NOT move added→unchanged or changed→unchanged
        // based on PreCheckStatus.AlreadySatisfied. The detectors only verify
        // existence (not version), so AlreadySatisfied is unreliable for versioned
        // packages. Only conservative overrides (that add work) are safe here.

        var unchangedToChanged = unchanged
            .Where(p => preCheckResults.TryGetValue(p.Name, out var r) && (r.Status == PreCheckStatus.WrongVersion || r.Status == PreCheckStatus.NotPresent))
            .ToList();

        foreach (var package in unchangedToChanged)
        {
            unchanged.Remove(package);
            changed.Add(package);
        }

        var addedToChanged = added
            .Where(p => preCheckResults.TryGetValue(p.Name, out var r) && r.Status == PreCheckStatus.WrongVersion)
            .ToList();

        foreach (var package in addedToChanged)
        {
            added.Remove(package);
            changed.Add(package);
        }

        var changedToAdded = changed
            .Where(p => preCheckResults.TryGetValue(p.Name, out var r) && r.Status == PreCheckStatus.NotPresent)
            .ToList();

        foreach (var package in changedToAdded)
        {
            changed.Remove(package);
            added.Add(package);
        }
    }
}

public sealed class DiffResult
{
    public List<PackageAssignment> Added { get; set; } = new();
    public List<PackageAssignment> Removed { get; set; } = new();
    public List<PackageAssignment> Changed { get; set; } = new();
    public List<PackageAssignment> Unchanged { get; set; } = new();
}
