using Microsoft.AspNetCore.Mvc;
using Orchestrator.Models;
using Orchestrator.Services;
using System.Text.Json;

namespace Orchestrator.Controllers;

[ApiController]
[Route("api/artifacts")]
public class ArtifactController : ControllerBase
{
    private readonly IArtifactService _artifactService;

    public ArtifactController(IArtifactService artifactService)
    {
        _artifactService = artifactService;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<object>> Upload(IFormFile manifest, IFormFile binary)
    {
        try
        {
            PackageManifest packageManifest;
            await using (var stream = manifest.OpenReadStream())
            {
                packageManifest = await JsonSerializer.DeserializeAsync<PackageManifest>(stream)
                    ?? throw new InvalidOperationException("manifest is empty or invalid");
            }

            var artifact = await _artifactService.UploadAsync(packageManifest, binary);
            return Ok(new
            {
                artifact.Id,
                artifact.PackageId,
                artifact.PackageName,
                artifact.Version,
                artifact.InstallerFile,
                artifact.UploadedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("bulk")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<object>> BulkImport(IFormFile zipFile)
    {
        var (imported, failed) = await _artifactService.ImportZipAsync(zipFile);

        return Ok(new
        {
            imported = imported.Select(a => new
            {
                a.PackageId,
                a.Version,
                a.InstallerFile
            }),
            failed = failed.Select(f => new
            {
                fileName = f.fileName,
                reason = f.reason
            })
        });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var artifacts = await _artifactService.GetAllAsync();
        return Ok(artifacts.Select(a => new
        {
            a.Id,
            a.PackageId,
            a.PackageName,
            a.Version,
            a.InstallerFile,
            a.UploadedAt
        }));
    }
}
