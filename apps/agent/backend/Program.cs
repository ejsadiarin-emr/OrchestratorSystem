using DeploymentPoC.Agent.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var hostConfig = new HostPlatformConfiguration();
hostConfig.ConfigureHostForPlatform(builder.Host);
builder.Services.AddHostedService<AgentRuntimeService>();

var app = builder.Build();

app.UseRouting();
app.MapGet("/health", () => Results.Ok(new { service = "agent", status = "ok" }));
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
