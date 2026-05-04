namespace Orchestrator.Configuration;

public class OrchestratorOptions
{
    public WebHostOptions WebHost { get; set; } = new();
    public AgentOptions Agent { get; set; } = new();
    public ArtifactOptions Artifact { get; set; } = new();
    public EnrollmentOptions Enrollment { get; set; } = new();
    public WorkloadOptions Workload { get; set; } = new();
}
