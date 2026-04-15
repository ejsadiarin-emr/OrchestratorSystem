using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/artifacts")]
public sealed class ArtifactsController : ControllerBase
{
    private readonly ArtifactStoreService _artifactStore;

    public ArtifactsController(ArtifactStoreService artifactStore)
    {
        _artifactStore = artifactStore;
    }

    [HttpHead("{packageId}/{version}")]
    public IActionResult Head(string packageId, string version)
    {
        try
        {
            if (!_artifactStore.TryGetMetadata(packageId, version, out var metadata))
            {
                return NotFound();
            }

            Response.Headers.ContentLength = metadata.Length;
            Response.Headers.ETag = $"W/\"{metadata.Length:x}-{metadata.LastWriteUtcTicks:x}\"";
            return Ok();
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound();
        }
        catch (IOException)
        {
            return Problem(title: "Artifact read failure", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{packageId}/{version}")]
    public IActionResult Get(string packageId, string version)
    {
        try
        {
            if (!_artifactStore.Exists(packageId, version))
            {
                return NotFound();
            }

            var stream = _artifactStore.OpenRead(packageId, version);
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound();
        }
        catch (IOException)
        {
            return Problem(title: "Artifact read failure", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
