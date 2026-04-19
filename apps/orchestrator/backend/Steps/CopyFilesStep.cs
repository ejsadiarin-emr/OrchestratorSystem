using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Steps;

public class CopyFilesStep : IInstallStep<InstallContext>
{
    private readonly ILogger<CopyFilesStep> _logger;

    public CopyFilesStep(ILogger<CopyFilesStep> logger)
    {
        _logger = logger;
    }

    public string Name => "CopyFiles";

    public bool CanExecute(InstallContext context) =>
        context.Data.ContainsKey("PreCheckPassed");

    public Task ExecuteAsync(InstallContext context)
    {
        _logger.LogInformation("Copying files for {Package} v{Version}", context.PackageName, context.Version);
        context.ExecutionLog.Add($"{Name}: Copying files for {context.PackageName}");
        context.Data["FilesCopied"] = true;
        return Task.CompletedTask;
    }
}
