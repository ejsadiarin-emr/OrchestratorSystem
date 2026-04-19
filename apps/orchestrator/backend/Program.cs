using DeploymentPoC.Orchestrator;
using DeploymentPoC.Orchestrator.Data;
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

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Logging.AddConsole();

var configuredInstallerDb = builder.Configuration.GetConnectionString("InstallerDb");
var fallbackDataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(fallbackDataDirectory);
var fallbackDbPath = Path.Combine(fallbackDataDirectory, "deployment-poc.db");
var connectionString = !string.IsNullOrWhiteSpace(configuredInstallerDb)
    ? configuredInstallerDb
    : $"Data Source={fallbackDbPath}";

builder.Services.AddDbContext<InstallerDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddSingleton<ArtifactStoreService>();
builder.Services.AddSingleton<ArtifactIngestService>();

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
app.MapFallbackToFile("index.html");

app.Run();
