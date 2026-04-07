using Microsoft.Extensions.Logging;

namespace EJInstaller.Orchestrator.Steps;

public class PreConditionCheckStep : IInstallStep<InstallContext>
{
    private readonly ILogger<PreConditionCheckStep> _logger;

    public PreConditionCheckStep(ILogger<PreConditionCheckStep> logger)
    {
        _logger = logger;
    }

    public string Name => "PreConditionCheck";

    public bool CanExecute(InstallContext context) =>
        !string.IsNullOrEmpty(context.PackageName);

    public Task ExecuteAsync(InstallContext context)
    {
        _logger.LogInformation("Checking preconditions for {Package}", context.PackageName);
        context.ExecutionLog.Add($"{Name}: Checking {context.PackageName}");
        context.Data["PreCheckPassed"] = true;
        return Task.CompletedTask;
    }
}
