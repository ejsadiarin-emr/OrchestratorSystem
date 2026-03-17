namespace EJInstaller.Orchestrator;

public interface IInstallStep<in TContext> where TContext : class
{
    string Name { get; }
    Task ExecuteAsync(TContext context);
    bool CanExecute(TContext context);
}
