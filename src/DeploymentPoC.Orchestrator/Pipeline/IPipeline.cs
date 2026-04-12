namespace DeploymentPoC.Orchestrator;

public interface IPipeline<TContext> where TContext : class, IPipelineContext
{
    IPipeline<TContext> AddStep(IInstallStep<TContext> step);
    Task<TContext> ExecuteAsync(TContext context);
}
