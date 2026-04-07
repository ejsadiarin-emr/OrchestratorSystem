using EJInstaller.Orchestrator;
using EJInstaller.Orchestrator.Steps;
using EJInstaller.Orchestrator.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.Environment.WebRootPath = null;

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Logging.AddConsole();

builder.Services.AddSingleton<AppStore>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".svg"] = "image/svg+xml";
provider.Mappings[".svgz"] = "image/svg+xml";

var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
var fileProvider = new EmbeddedFileProvider(assembly, "Static");

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = fileProvider
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    ContentTypeProvider = provider,
    ServeUnknownFileTypes = false
});

app.UseHttpsRedirection();
app.MapControllers();

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    FileProvider = fileProvider
});

app.Run();
