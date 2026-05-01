using DeploymentPoC.Agent.Models;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Services;
using DeploymentPoC.Agent.Steps;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Parse CLI args
var parsedArgs = AgentProgram.ParseArgs(args);

var enrollmentService = new AgentEnrollmentService(new HttpClient());

if (parsedArgs.ResetEnrollment)
{
    enrollmentService.ResetConfig();
    Console.WriteLine("Enrollment reset.");
    return 0;
}

AgentConfig? config = null;

if (!string.IsNullOrEmpty(parsedArgs.EnrollToken) && !string.IsNullOrEmpty(parsedArgs.OrchestratorUrl))
{
    config = enrollmentService.LoadConfig();
    if (config is not null)
    {
        Console.Error.WriteLine("Already enrolled. Use --reset-enrollment to re-enroll, or omit --enroll to auto-connect.");
        return 1;
    }

    var nodeId = await enrollmentService.ConsumeEnrollmentTokenAsync(parsedArgs.EnrollToken, parsedArgs.OrchestratorUrl, parsedArgs.DisplayName);
    config = new AgentConfig
    {
        NodeId = nodeId,
        OrchestratorUrl = parsedArgs.OrchestratorUrl
    };
    enrollmentService.SaveConfig(config);
    Console.WriteLine($"Enrollment successful. NodeId={nodeId}");
}
else
{
    config = enrollmentService.LoadConfig();
    if (config is null)
    {
        Console.Error.WriteLine("Not enrolled. Run with --enroll <token> --orchestrator-url <url>.");
        return 1;
    }

    Console.WriteLine($"Auto-connecting with stored config. NodeId={config.NodeId}");
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = parsedArgs.RemainingArgs,
    ContentRootPath = AppContext.BaseDirectory
});

builder.WebHost.UseUrls("http://localhost:5001");

builder.Configuration["Agent:NodeId"] = config.NodeId.ToString();
builder.Configuration["Orchestrator:BaseUrl"] = config.OrchestratorUrl;

var hostConfig = new HostPlatformConfiguration();
hostConfig.ConfigureHostForPlatform(builder.Host);
builder.Services.AddHttpClient().ConfigureHttpClientDefaults(b => b.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(600)));
builder.Services.AddSingleton<PipelineExecutor>();
builder.Services.AddSingleton<IHubConnectionFactory, HubConnectionFactory>();
builder.Services.AddHostedService<AgentRuntimeService>();

var app = builder.Build();

app.UseRouting();
app.MapGet("/health", () => Results.Ok(new { service = "agent", status = "ok" }));
app.MapPost("/api/detect", DetectEndpointHandler.HandleAsync);
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

return 0;

public static class AgentProgram
{
    public static (string? EnrollToken, string? OrchestratorUrl, bool ResetEnrollment, string? DisplayName, string[] RemainingArgs) ParseArgs(string[] args)
    {
        string? enrollToken = null;
        string? orchestratorUrl = null;
        bool resetEnrollment = false;
        string? displayName = null;
        var remainingArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--enroll" && i + 1 < args.Length)
            {
                enrollToken = args[++i];
            }
            else if (args[i] == "--orchestrator-url" && i + 1 < args.Length)
            {
                orchestratorUrl = args[++i];
            }
            else if (args[i] == "--display-name" && i + 1 < args.Length)
            {
                displayName = args[++i];
            }
            else if (args[i] == "--reset-enrollment")
            {
                resetEnrollment = true;
            }
            else
            {
                remainingArgs.Add(args[i]);
            }
        }

        return (enrollToken, orchestratorUrl, resetEnrollment, displayName, remainingArgs.ToArray());
    }
}
