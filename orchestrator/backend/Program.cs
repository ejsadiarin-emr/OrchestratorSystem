using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orchestrator.Configuration;
using Orchestrator.Extensions;
using Orchestrator.Middleware;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((ctx, kestrel) =>
{
    var hostOpts = ctx.Configuration.GetSection("WebHost").Get<WebHostOptions>()!;
    kestrel.Listen(System.Net.IPAddress.Parse(hostOpts.BindAddress), hostOpts.Port);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddOrchestratorOptions(builder.Configuration);
builder.Services.AddOrchestratorServices();

// Add CORS for local development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<AgentAuthMiddleware>();

// Map controllers BEFORE SPA fallback
app.MapControllers();

// SPA fallback for non-API routes
app.MapFallbackToFile("index.html");

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var migrationService = scope.ServiceProvider.GetRequiredService<IDataMigrationService>();
    await migrationService.InitializeAsync();
}

// Create required directories
var artifactPath = app.Services.GetRequiredService<IOptions<ArtifactStoreOptions>>().Value.ResolvePath();
var workloadPath = app.Services.GetRequiredService<IOptions<WorkloadDefinitionStoreOptions>>().Value.Path;
Directory.CreateDirectory(artifactPath);
Directory.CreateDirectory(workloadPath);

app.Run();
