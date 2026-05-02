using DeploymentPoC.Orchestrator.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentDownloadController : ControllerBase
{
    private readonly ILogger<AgentDownloadController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly InstallerDbContext _db;
    private readonly string _agentExePath;

    public AgentDownloadController(ILogger<AgentDownloadController> logger, IWebHostEnvironment env, InstallerDbContext db, IConfiguration configuration)
    {
        _logger = logger;
        _env = env;
        _db = db;
        _agentExePath = configuration["AgentDownload:AgentExePath"]
            ?? Path.Combine(env.ContentRootPath, "data", "agent.exe");
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unauthorized(new { message = "Enrollment token is required." });
        }

        var enrollmentToken = _db.EnrollmentTokens.FirstOrDefault(t => t.Token == token);
        if (enrollmentToken is null || enrollmentToken.Used)
        {
            return Unauthorized(new { message = "Invalid or already used enrollment token." });
        }

        if (enrollmentToken.ExpiresAtUtc < DateTime.UtcNow)
        {
            return StatusCode(410, new { message = "Enrollment token has expired." });
        }

        var agentPath = _agentExePath;
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
