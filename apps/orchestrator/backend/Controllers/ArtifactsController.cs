using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/artifacts")]
public sealed class ArtifactsController : ControllerBase
{
    private readonly ArtifactStoreService _artifactStore;
    private readonly ArtifactIngestService _artifactIngest;

    public ArtifactsController(ArtifactStoreService artifactStore, ArtifactIngestService artifactIngest)
    {
        _artifactStore = artifactStore;
        _artifactIngest = artifactIngest;
    }

    [HttpPost]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> Ingest()
    {
        if (!Request.HasFormContentType)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "request",
                        Error = "multipart/form-data is required"
                    }
                }
            });
        }

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var manifestText = form["manifest"].FirstOrDefault();

        if (file is null)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "file",
                        Error = "file is required"
                    }
                }
            });
        }

        if (string.IsNullOrWhiteSpace(manifestText))
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "manifest",
                        Error = "manifest is required"
                    }
                }
            });
        }

        ArtifactIngestManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ArtifactIngestManifest>(manifestText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ArtifactIngestManifest();
        }
        catch (JsonException)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "manifest",
                        Error = "manifest must be valid JSON"
                    }
                }
            });
        }

        await using var stream = file.OpenReadStream();
        var actorId = User?.Identity?.Name ?? "anonymous";
        var result = _artifactIngest.Ingest(file.FileName, stream, manifest, actorId);

        if (!result.IsValid)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = result.Errors
            });
        }

        await _artifactStore.SaveArtifactAsync(result.ResolvedManifest!.PackageId, result.ResolvedManifest.Version, stream, HttpContext.RequestAborted);
        var resolvedManifestJson = JsonSerializer.Serialize(result.ResolvedManifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await _artifactStore.SaveResolvedManifestAsync(result.ResolvedManifest.PackageId, result.ResolvedManifest.Version, resolvedManifestJson, HttpContext.RequestAborted);

        return StatusCode(StatusCodes.Status201Created, new
        {
            resolvedManifest = result.ResolvedManifest
        });
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
