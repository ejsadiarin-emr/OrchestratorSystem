using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator;

public class Pipeline<TContext> : IPipeline<TContext> where TContext : class, IPipelineContext
{
    private readonly List<IInstallStep<TContext>> _steps;
    private readonly ILogger<Pipeline<TContext>> _logger;

    public Pipeline(ILogger<Pipeline<TContext>> logger)
    {
        _steps = new List<IInstallStep<TContext>>();
        _logger = logger;
    }

    public IPipeline<TContext> AddStep(IInstallStep<TContext> step)
    {
        _steps.Add(step);
        return this;
    }

    public async Task<TContext> ExecuteAsync(TContext context)
    {
        _logger.LogInformation("Starting pipeline with {StepCount} steps", _steps.Count);

        foreach (var step in _steps)
        {
            _logger.LogInformation("Executing step: {StepName}", step.Name);

            if (!step.CanExecute(context))
            {
                _logger.LogWarning("Step {StepName} skipped", step.Name);
                continue;
            }

            try
            {
                await step.ExecuteAsync(context);
                _logger.LogInformation("Step {StepName} completed", step.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step {StepName} failed", step.Name);
                context.IsSuccessful = false;
                context.ErrorMessage = ex.Message;
                break;
            }
        }

        _logger.LogInformation("Pipeline completed. Success: {Success}", context.IsSuccessful);
        return context;
    }
}
