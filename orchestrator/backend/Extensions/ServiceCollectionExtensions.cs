using Orchestrator.Configuration;
using Orchestrator.Services;

namespace Orchestrator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrchestratorOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OrchestratorOptions>(configuration);
        services.Configure<AgentOptions>(configuration.GetSection("Agent"));
        services.Configure<ArtifactStoreOptions>(configuration.GetSection("ArtifactStore"));
        services.Configure<EnrollmentOptions>(configuration.GetSection("Enrollment"));
        services.Configure<WorkloadDefinitionStoreOptions>(configuration.GetSection("WorkloadDefinitionStore"));
        services.Configure<WebHostOptions>(configuration.GetSection("WebHost"));
        return services;
    }

    public static IServiceCollection AddOrchestratorServices(this IServiceCollection services)
    {
        services.AddScoped<IDataMigrationService, DataMigrationService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IArtifactService, ArtifactService>();
        services.AddScoped<IWorkloadService, WorkloadService>();
        services.AddScoped<IRunService, RunService>();
        services.AddHostedService<WorkloadWatcherService>();
        return services;
    }
}
