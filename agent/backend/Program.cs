// agent/backend/Program.cs
using Agent.Services;
using System.CommandLine;

var rootCommand = new RootCommand("Orchestrator Agent");

var enrollOption = new Option<string>("--enroll", "Enrollment token from Orchestrator");
var urlOption = new Option<string>("--url", "Orchestrator URL (e.g., http://host:5000)");
var resetOption = new Option<bool>("--reset", "Unregister agent, remove config, and uninstall service");

rootCommand.AddOption(enrollOption);
rootCommand.AddOption(urlOption);
rootCommand.AddOption(resetOption);

rootCommand.SetHandler((string? enrollToken, string? url, bool reset) =>
{
    if (reset)
    {
        // --reset mode: unregister with Orchestrator, stop/delete service, delete agent.json, exit
        var resetService = new AgentResetService();
        return resetService.ExecuteResetAsync();
    }

    if (!string.IsNullOrEmpty(enrollToken))
    {
        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Error: --url is required when using --enroll");
            return Task.FromResult(1);
        }
        var enrollService = new AgentEnrollService();
        return enrollService.ExecuteEnrollAsync(enrollToken, url);
    }

    // Default mode: run as Windows Service
    return Task.FromResult(0);
}, enrollOption, urlOption, resetOption);

var commandResult = await rootCommand.InvokeAsync(args);

if (commandResult != 0 || args.Contains("--enroll") || args.Contains("--reset"))
{
    return;
}

// Default: run as Windows Service
var builder = Host.CreateDefaultBuilder(args);
builder.UseWindowsService();
builder.ConfigureServices((hostContext, services) =>
{
    services.AddHostedService<AgentPollingService>();
    services.AddHttpClient();
});

var host = builder.Build();
await host.RunAsync();
