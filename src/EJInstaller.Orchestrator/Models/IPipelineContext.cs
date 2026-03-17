namespace EJInstaller.Orchestrator;

public interface IPipelineContext
{
    bool IsSuccessful { get; set; }
    string? ErrorMessage { get; set; }
    List<string> ExecutionLog { get; set; }
}
