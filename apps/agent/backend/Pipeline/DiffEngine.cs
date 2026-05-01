using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Pipeline;

public static class DiffEngine
{
    public static DiffResult ComputeDiff(List<PackageAssignment> current, List<PackageAssignment> target)
        => ComputeDiff(current, target, null);

    public static DiffResult ComputeDiff(
        List<PackageAssignment> current,
        List<PackageAssignment> target,
        Dictionary<string, PreCheckResult>? preCheckResults)
    {
        var currentByName = current.ToDictionary(p => p.Name);
        var targetByName = target.ToDictionary(p => p.Name);

        var added = target.Where(p => !currentByName.ContainsKey(p.Name)).ToList();
        var removed = current.Where(p => !targetByName.ContainsKey(p.Name)).ToList();
        var changed = target.Where(p => currentByName.TryGetValue(p.Name, out var c) && c.Version != p.Version).ToList();
        var unchanged = target.Where(p => currentByName.TryGetValue(p.Name, out var c) && c.Version == p.Version).ToList();

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
        var addedToUnchanged = added
            .Where(p => preCheckResults.TryGetValue(p.Name, out var r) && r.Status == PreCheckStatus.AlreadySatisfied)
            .ToList();

        foreach (var package in addedToUnchanged)
        {
            added.Remove(package);
            unchanged.Add(package);
        }

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

        var changedToUnchanged = changed
            .Where(p => preCheckResults.TryGetValue(p.Name, out var r) && r.Status == PreCheckStatus.AlreadySatisfied)
            .ToList();

        foreach (var package in changedToUnchanged)
        {
            changed.Remove(package);
            unchanged.Add(package);
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
