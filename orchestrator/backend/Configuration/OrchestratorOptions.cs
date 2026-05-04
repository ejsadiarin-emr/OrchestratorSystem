namespace Orchestrator.Configuration;

public class OrchestratorOptions
{
    public WebHostOptions WebHost { get; set; } = new();
    public AgentOptions Agent { get; set; } = new();
    public ArtifactStoreOptions ArtifactStore { get; set; } = new();
    public EnrollmentOptions Enrollment { get; set; } = new();
    public WorkloadDefinitionStoreOptions WorkloadDefinitionStore { get; set; } = new();
}
