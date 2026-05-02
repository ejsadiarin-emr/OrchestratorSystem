using DeploymentPoC.Orchestrator;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Hubs;
using DeploymentPoC.Orchestrator.Runtime;
using DeploymentPoC.Orchestrator.Services;
using DeploymentPoC.Orchestrator.Steps;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

var distRoot = ResolveDistRoot();

// Resolve absolute paths for data assets relative to the dist root
var configuredInstallerDb = builder.Configuration.GetConnectionString("InstallerDb");
var connectionString = !string.IsNullOrWhiteSpace(configuredInstallerDb)
    ? configuredInstallerDb
    : $"Data Source={Path.Combine(distRoot, "deployment-poc.db")}";
builder.Configuration["ConnectionStrings:InstallerDb"] = connectionString;

var artifactStorePath = builder.Configuration["ArtifactStore:RootPath"]
    ?? Path.Combine(distRoot, "artifacts");
if (!Path.IsPathRooted(artifactStorePath))
{
    artifactStorePath = Path.GetFullPath(Path.Combine(distRoot, artifactStorePath));
}
builder.Configuration["ArtifactStore:RootPath"] = artifactStorePath;

var agentExePath = builder.Configuration["AgentDownload:AgentExePath"]
    ?? Path.Combine(distRoot, "agent.exe");
if (!Path.IsPathRooted(agentExePath))
{
    agentExePath = Path.GetFullPath(Path.Combine(distRoot, agentExePath));
}
builder.Configuration["AgentDownload:AgentExePath"] = agentExePath;

// Ensure directories exist
Directory.CreateDirectory(distRoot);
Directory.CreateDirectory(artifactStorePath);

var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyMethod()
              .AllowAnyHeader();

        if (allowedCorsOrigins.Length > 0)
        {
            policy.WithOrigins(allowedCorsOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }
    });
});

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Logging.AddConsole();

builder.Services.AddDbContext<InstallerDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddSingleton<ArtifactStoreService>();
builder.Services.AddSingleton<ArtifactIngestService>();
builder.Services.AddSingleton<UploadSessionService>();
builder.Services.AddSingleton<ArtifactZipService>();
builder.Services.AddSingleton<AgentConnectionTracker>();
builder.Services.AddScoped<PolicyEvaluationService>();
builder.Services.AddScoped<WorkloadImportService>();
builder.Services.AddScoped<WorkloadRunDispatcher>();
builder.Services.AddScoped<NodeWorkloadStateService>();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

builder.Services.AddHostedService<NodeHeartbeatMonitorService>();

builder.Services.AddTransient<PreConditionCheckStep>();
builder.Services.AddTransient<CopyFilesStep>();

builder.Services.AddTransient<IPipeline<InstallContext>>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Pipeline<InstallContext>>>();
    var pipeline = new Pipeline<InstallContext>(logger);

    pipeline.AddStep(sp.GetRequiredService<PreConditionCheckStep>());
    pipeline.AddStep(sp.GetRequiredService<CopyFilesStep>());

    return pipeline;
});

var app = builder.Build();

app.Urls.Clear();
app.Urls.Add("http://0.0.0.0:5000");

// startup cleanup: purge stale temp upload directories
var tempRoot = Path.Combine(artifactStorePath, "_temp");
if (Directory.Exists(tempRoot))
{
    foreach (var dir in Directory.GetDirectories(tempRoot))
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // never fail startup on cleanup
        }
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseHttpsRedirection();
app.MapControllers();
app.MapHub<AgentRuntimeHub>("/hubs/agent");
app.MapHealthChecks("/health");
app.MapFallbackToFile("index.html");

app.Run();

static string ResolveDistRoot()
{
    // Check if we're running as a published single-file exe from dist/
    var exePath = Environment.ProcessPath;
    if (!string.IsNullOrEmpty(exePath))
    {
        var exeDir = Path.GetDirectoryName(exePath)!;
        // If the exe directory looks like our dist folder (contains either exe)
        if (File.Exists(Path.Combine(exeDir, "DeploymentPoC.Orchestrator.exe")) ||
            File.Exists(Path.Combine(exeDir, "DeploymentPoC.Agent.exe")) ||
            File.Exists(Path.Combine(exeDir, "orchestrator.exe")) ||
            File.Exists(Path.Combine(exeDir, "agent.exe")))
        {
            return exeDir;
        }
    }

    // Development: find project root by looking for .sln file
    var currentDir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(currentDir))
    {
        if (File.Exists(Path.Combine(currentDir, "DeploymentPoC.sln")))
        {
            return Path.Combine(currentDir, "dist");
        }
        var parent = Directory.GetParent(currentDir);
        if (parent is null) break;
        currentDir = parent.FullName;
    }

    // Fallback: current working directory + dist
    return Path.Combine(Directory.GetCurrentDirectory(), "dist");
}
