using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InstallController : ControllerBase
{
    private readonly IPipeline<InstallContext> _pipeline;
    private readonly ILogger<InstallController> _logger;

    public InstallController(IPipeline<InstallContext> pipeline, ILogger<InstallController> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Install([FromBody] InstallRequest request)
    {
        _logger.LogInformation("Received install request for {Package}", request.PackageName);

        var context = new InstallContext
        {
            PackageName = request.PackageName,
            TargetMachine = request.TargetMachine,
            Version = request.Version
        };

        var result = await _pipeline.ExecuteAsync(context);

        var response = new
        {
            result.IsSuccessful,
            result.ErrorMessage,
            result.ExecutionLog
        };

        if (!result.IsSuccessful)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}

public class InstallRequest
{
    public string PackageName { get; set; } = string.Empty;
    public string TargetMachine { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
