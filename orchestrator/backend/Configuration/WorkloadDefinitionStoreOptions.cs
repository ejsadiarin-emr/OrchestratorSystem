namespace Orchestrator.Configuration;

public class WorkloadDefinitionStoreOptions
{
    public string Path { get; set; } = "dist/workload-definitions/";
    public bool WatchForChanges { get; set; } = true;
}
