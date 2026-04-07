using Microsoft.AspNetCore.Mvc;
using EJInstaller.Orchestrator.Models;
using EJInstaller.Orchestrator.Store;
using Microsoft.Extensions.Logging;

namespace EJInstaller.Orchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PackagesController : ControllerBase
{
    private readonly AppStore _store;
    private readonly ILogger<PackagesController> _logger;

    public PackagesController(AppStore store, ILogger<PackagesController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<IEnumerable<Package>> GetAll()
    {
        return Ok(_store.Packages.Values.ToList());
    }

    [HttpGet("{id:guid}")]
    public ActionResult<Package> GetById(Guid id)
    {
        if (_store.Packages.TryGetValue(id, out var package))
        {
            return Ok(package);
        }
        return NotFound(new { message = $"Package {id} not found" });
    }

    [HttpPost]
    public ActionResult<Package> Create([FromBody] CreatePackageRequest request)
    {
        var package = new Package
        {
            Name = request.Name,
            Version = request.Version,
            SourcePath = request.SourcePath,
            InstallType = request.InstallType,
            InstallArgs = request.InstallArgs
        };

        _store.Packages[package.Id] = package;
        _logger.LogInformation("Created package {Name} v{Version}", package.Name, package.Version);
        
        return CreatedAtAction(nameof(GetById), new { id = package.Id }, package);
    }

    [HttpDelete("{id:guid}")]
    public ActionResult Delete(Guid id)
    {
        if (_store.Packages.Remove(id))
        {
            _logger.LogInformation("Deleted package {Id}", id);
            return NoContent();
        }
        return NotFound(new { message = $"Package {id} not found" });
    }
}
