using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Pipeline;

public static class DiffEngine
{
    public static DiffResult ComputeDiff(List<PackageAssignment> current, List<PackageAssignment> target)
    {
        var currentByName = current.ToDictionary(p => p.Name);
        var targetByName = target.ToDictionary(p => p.Name);

        return new DiffResult
        {
            Added = target.Where(p => !currentByName.ContainsKey(p.Name)).ToList(),
            Removed = current.Where(p => !targetByName.ContainsKey(p.Name)).ToList(),
            Changed = target.Where(p => currentByName.TryGetValue(p.Name, out var c) && c.Version != p.Version).ToList(),
            Unchanged = target.Where(p => currentByName.TryGetValue(p.Name, out var c) && c.Version == p.Version).ToList()
        };
    }
}

public sealed class DiffResult
{
    public List<PackageAssignment> Added { get; set; } = new();
    public List<PackageAssignment> Removed { get; set; } = new();
    public List<PackageAssignment> Changed { get; set; } = new();
    public List<PackageAssignment> Unchanged { get; set; } = new();
}
