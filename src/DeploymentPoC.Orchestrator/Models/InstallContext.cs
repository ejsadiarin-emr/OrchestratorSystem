namespace DeploymentPoC.Orchestrator;

public class InstallContext : IPipelineContext
{
    public string PackageName { get; set; } = string.Empty;
    public string TargetMachine { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public List<string> ExecutionLog { get; set; } = new();
    public bool IsSuccessful { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
