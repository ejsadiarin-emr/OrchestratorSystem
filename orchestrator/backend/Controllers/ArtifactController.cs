using Microsoft.AspNetCore.Mvc;
using Orchestrator.Services;

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

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<object>> Upload(
        [FromForm] string packageId,
        [FromForm] string version,
        [FromForm] string packageName,
        IFormFile file)
    {
        var artifact = await _artifactService.UploadAsync(packageId, version, packageName, file);
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

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<object>> Import(List<IFormFile> files)
    {
        var imported = await _artifactService.ImportAsync(files);
        var failed = new List<object>();

        return Ok(new
        {
            imported = imported.Select(a => new
            {
                a.PackageId,
                a.Version,
                a.InstallerFile
            }),
            failed
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
