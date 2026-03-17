using EJInstaller.Orchestrator;
using EJInstaller.Orchestrator.Steps;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add logging
builder.Logging.AddConsole();

// Register pipeline steps
builder.Services.AddTransient<PreConditionCheckStep>();
builder.Services.AddTransient<CopyFilesStep>();

// Register the generic pipeline
builder.Services.AddTransient<IPipeline<InstallContext>>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Pipeline<InstallContext>>>();
    var pipeline = new Pipeline<InstallContext>(logger);
    
    // Add steps in order
    pipeline.AddStep(sp.GetRequiredService<PreConditionCheckStep>());
    pipeline.AddStep(sp.GetRequiredService<CopyFilesStep>());
    
    return pipeline;
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
