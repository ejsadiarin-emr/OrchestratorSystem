using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;
using DeploymentPoC.Orchestrator.Validation;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PackagesController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly ILogger<PackagesController> _logger;

    public PackagesController(InstallerDbContext db, ILogger<PackagesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Package>>> GetAll()
    {
        var entities = await _db.Packages
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync();

        var packages = entities.Select(p =>
        {
            var detectionConfig = string.IsNullOrWhiteSpace(p.DetectionConfigJson)
                ? new DetectionConfig()
                : System.Text.Json.JsonSerializer.Deserialize<DetectionConfig>(p.DetectionConfigJson) ?? new DetectionConfig();

            return new Package
            {
                Id = p.PackageId,
                Name = p.Name,
                Version = p.Version,
                SourcePath = p.SourcePath,
                InstallType = p.InstallType,
                InstallArgs = p.InstallArgs,
                UninstallCommand = p.UninstallCommand,
                UninstallArgs = p.UninstallArgs,
                UpgradeBehavior = p.UpgradeBehavior,
                DetectionType = detectionConfig.Type,
                DetectionPath = detectionConfig.Path,
                ExpectedVersion = detectionConfig.ExpectedVersion,
                CreatedAt = p.CreatedAtUtc
            };
        }).ToList();

        return Ok(packages);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Package>> GetById(Guid id)
    {
        var entity = await _db.Packages
            .Where(p => p.PackageId == id)
            .SingleOrDefaultAsync();

        if (entity is null)
        {
            return NotFound(new { message = $"Package {id} not found" });
        }

        var detectionConfig = string.IsNullOrWhiteSpace(entity.DetectionConfigJson)
            ? new DetectionConfig()
            : System.Text.Json.JsonSerializer.Deserialize<DetectionConfig>(entity.DetectionConfigJson) ?? new DetectionConfig();

        var package = new Package
        {
            Id = entity.PackageId,
            Name = entity.Name,
            Version = entity.Version,
            SourcePath = entity.SourcePath,
            InstallType = entity.InstallType,
            InstallArgs = entity.InstallArgs,
            UninstallCommand = entity.UninstallCommand,
            UninstallArgs = entity.UninstallArgs,
            UpgradeBehavior = entity.UpgradeBehavior,
            DetectionType = detectionConfig.Type,
            DetectionPath = detectionConfig.Path,
            ExpectedVersion = detectionConfig.ExpectedVersion,
            CreatedAt = entity.CreatedAtUtc
        };

        return Ok(package);
    }

    [HttpPost]
    public async Task<ActionResult<Package>> Create([FromBody] CreatePackageRequest request)
    {
        var validation = UpgradeBehaviorValidator.Validate(request.UpgradeBehavior);
        if (!validation.IsValid)
        {
            return BadRequest(new { message = validation.Error });
        }

        var entity = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = request.Name,
            Version = request.Version,
            SourcePath = request.SourcePath,
            InstallType = request.InstallType,
            InstallArgs = request.InstallArgs,
            UninstallCommand = request.UninstallCommand,
            UninstallArgs = request.UninstallArgs,
            UpgradeBehavior = UpgradeBehaviorValidator.Normalize(request.UpgradeBehavior),
            DetectionConfigJson = string.IsNullOrWhiteSpace(request.DetectionType) && string.IsNullOrWhiteSpace(request.DetectionPath) && string.IsNullOrWhiteSpace(request.ExpectedVersion)
                ? string.Empty
                : System.Text.Json.JsonSerializer.Serialize(new DetectionConfig
                {
                    Type = request.DetectionType,
                    Path = request.DetectionPath,
                    ExpectedVersion = request.ExpectedVersion
                }),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Packages.Add(entity);
        await _db.SaveChangesAsync();

        var package = new Package
        {
            Id = entity.PackageId,
            Name = request.Name,
            Version = request.Version,
            SourcePath = request.SourcePath,
            InstallType = request.InstallType,
            InstallArgs = request.InstallArgs,
            UninstallCommand = request.UninstallCommand,
            UninstallArgs = request.UninstallArgs,
            UpgradeBehavior = entity.UpgradeBehavior,
            DetectionType = request.DetectionType,
            DetectionPath = request.DetectionPath,
            ExpectedVersion = request.ExpectedVersion,
            CreatedAt = entity.CreatedAtUtc
        };

        _logger.LogInformation("Created package {Name} v{Version} with UpgradeBehavior={UpgradeBehavior}", package.Name, package.Version, package.UpgradeBehavior);
        
        return CreatedAtAction(nameof(GetById), new { id = package.Id }, package);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var entity = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == id);
        if (entity is not null)
        {
            _db.Packages.Remove(entity);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Deleted package {Id}", id);
            return NoContent();
        }

        return NotFound(new { message = $"Package {id} not found" });
    }
}
