using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentDownloadController : ControllerBase
{
    private readonly ILogger<AgentDownloadController> _logger;
    private readonly IWebHostEnvironment _env;

    public AgentDownloadController(ILogger<AgentDownloadController> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { message = "Enrollment token is required." });
        }

        // TODO: Validate token exists and is unused before allowing download (W3-02a follow-up)
        // For PoC, we serve a placeholder agent binary.

        var agentPath = Path.Combine(_env.ContentRootPath, "data", "agent.exe");
        if (!System.IO.File.Exists(agentPath))
        {
            // Create a minimal placeholder binary so the download endpoint works in PoC
            Directory.CreateDirectory(Path.GetDirectoryName(agentPath)!);
            var placeholder = new byte[] { 0x4D, 0x5A }; // MZ header for Windows executables
            System.IO.File.WriteAllBytes(agentPath, placeholder);
            _logger.LogInformation("Created placeholder agent binary at {Path}", agentPath);
        }

        _logger.LogInformation("Serving agent download for token {Token}", token);

        return PhysicalFile(agentPath, "application/octet-stream", "agent.exe");
    }
}
