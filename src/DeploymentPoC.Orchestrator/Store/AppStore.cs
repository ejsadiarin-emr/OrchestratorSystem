using DeploymentPoC.Orchestrator.Models;

namespace DeploymentPoC.Orchestrator.Store;

public class AppStore
{
    public Dictionary<Guid, Package> Packages { get; set; } = new();
    public Dictionary<Guid, Node> Nodes { get; set; } = new();
    public Dictionary<Guid, InstallJob> Jobs { get; set; } = new();
}
