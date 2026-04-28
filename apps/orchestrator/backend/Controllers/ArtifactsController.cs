using System.Security.Cryptography;
using System.Text;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/artifacts")]
public sealed class ArtifactsController : ControllerBase
{
    private readonly ArtifactStoreService _artifactStore;
    private readonly ArtifactIngestService _artifactIngest;
    private readonly UploadSessionService _uploadSession;
    private readonly ArtifactZipService _artifactZip;
    private readonly InstallerDbContext _db;

    public ArtifactsController(ArtifactStoreService artifactStore, ArtifactIngestService artifactIngest, UploadSessionService uploadSession, ArtifactZipService artifactZip, InstallerDbContext db)
    {
        _artifactStore = artifactStore;
        _artifactIngest = artifactIngest;
        _uploadSession = uploadSession;
        _artifactZip = artifactZip;
        _db = db;
    }

    [HttpPost]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
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

        await using var stream = file.OpenReadStream();
        var actorId = User?.Identity?.Name ?? "anonymous";

        bool isZip = file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || IsZipByMagicBytes(stream);

        if (isZip)
        {
            var extraction = _artifactZip.ExtractAndValidateSingleZip(stream);
            if (extraction.Errors.Count > 0)
            {
                return BadRequest(new Contracts.Api.ValidationErrorResponse
                {
                    Errors = extraction.Errors.Select(e => new Contracts.Api.ValidationFieldError
                    {
                        Field = "file",
                        Error = e
                    }).ToList()
                });
            }

            var extracted = extraction.Artifacts[0];
            var result = _artifactIngest.Ingest(extracted.MediaFileName, extracted.MediaStream, extracted.Manifest, actorId);

            if (!result.IsValid)
            {
                return BadRequest(new Contracts.Api.ValidationErrorResponse
                {
                    Errors = result.Errors
                });
            }

            var resolvedManifestJson = JsonSerializer.Serialize(result.ResolvedManifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var packageId = result.ResolvedManifest!.PackageId;
            var version = result.ResolvedManifest.Version;
            if (_artifactStore.ExistsAny(packageId, version))
            {
                return Conflict(new { message = $"Artifact '{packageId}' version '{version}' already exists." });
            }

            await _artifactStore.SaveArtifactAndManifestAsync(packageId, version, extracted.MediaStream, resolvedManifestJson, HttpContext.RequestAborted, fileName: extracted.MediaFileName);

            var packageEntityId = DeterministicGuid($"{packageId}-{version}");
            var existingPackage = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId, HttpContext.RequestAborted);
            if (existingPackage is null)
            {
                _db.Packages.Add(new PackageEntity
                {
                    PackageId = packageEntityId,
                    Name = result.ResolvedManifest.PackageId,
                    Version = result.ResolvedManifest.Version,
                    SourcePath = result.ResolvedManifest.InstallAdapter?.Command ?? string.Empty,
                    InstallType = result.ResolvedManifest.InstallAdapter?.Type ?? "exe",
                    InstallArgs = result.ResolvedManifest.InstallAdapter?.Arguments ?? string.Empty,
                    ExpectedExitCodesJson = JsonSerializer.Serialize(
                        result.ResolvedManifest.InstallAdapter?.ExpectedExitCodes ?? new List<int> { 0, 3010 }),
                    DetectionConfigJson = JsonSerializer.Serialize(result.ResolvedManifest.Detection),
                    TimeoutSeconds = result.ResolvedManifest.InstallAdapter?.TimeoutSeconds ?? 300,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(HttpContext.RequestAborted);
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                resolvedManifest = result.ResolvedManifest,
                packageEntityId
            });
        }

        var manifestText = form["manifest"].FirstOrDefault();

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

        var ingestResult = _artifactIngest.Ingest(file.FileName, stream, manifest, actorId);

        if (!ingestResult.IsValid)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = ingestResult.Errors
            });
        }

        var resolvedManifestJson2 = JsonSerializer.Serialize(ingestResult.ResolvedManifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var packageId2 = ingestResult.ResolvedManifest!.PackageId;
        var version2 = ingestResult.ResolvedManifest.Version;
        if (_artifactStore.ExistsAny(packageId2, version2))
        {
            return Conflict(new { message = $"Artifact '{packageId2}' version '{version2}' already exists." });
        }

        await _artifactStore.SaveArtifactAndManifestAsync(packageId2, version2, stream, resolvedManifestJson2, HttpContext.RequestAborted, fileName: file.FileName);

        var packageEntityId2 = DeterministicGuid($"{packageId2}-{version2}");
        var existingPackage2 = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId2, HttpContext.RequestAborted);
        if (existingPackage2 is null)
        {
            _db.Packages.Add(new PackageEntity
            {
                PackageId = packageEntityId2,
                Name = ingestResult.ResolvedManifest.PackageId,
                Version = ingestResult.ResolvedManifest.Version,
                SourcePath = ingestResult.ResolvedManifest.InstallAdapter?.Command ?? string.Empty,
                InstallType = ingestResult.ResolvedManifest.InstallAdapter?.Type ?? "exe",
                InstallArgs = ingestResult.ResolvedManifest.InstallAdapter?.Arguments ?? string.Empty,
                ExpectedExitCodesJson = JsonSerializer.Serialize(
                    ingestResult.ResolvedManifest.InstallAdapter?.ExpectedExitCodes ?? new List<int> { 0, 3010 }),
                DetectionConfigJson = JsonSerializer.Serialize(ingestResult.ResolvedManifest.Detection),
                TimeoutSeconds = ingestResult.ResolvedManifest.InstallAdapter?.TimeoutSeconds ?? 300,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(HttpContext.RequestAborted);
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            resolvedManifest = ingestResult.ResolvedManifest,
            packageEntityId = packageEntityId2
        });
    }

    private static bool IsZipByMagicBytes(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
            return false;

        var position = stream.Position;
        var buffer = new byte[2];
        var read = stream.Read(buffer, 0, 2);
        stream.Position = position;
        return read == 2 && buffer[0] == 0x50 && buffer[1] == 0x4B;
    }

    private async Task<List<object>> ProcessBulkArtifactsAsync(Stream stream, string actorId, CancellationToken cancellationToken)
    {
        var extraction = _artifactZip.ExtractAndValidateBulkZip(stream);
        var results = new List<object>();

        foreach (var artifact in extraction.Artifacts)
        {
            var result = _artifactIngest.Ingest(artifact.MediaFileName, artifact.MediaStream, artifact.Manifest, actorId);
            if (!result.IsValid)
            {
                results.Add(new
                {
                    fileName = artifact.MediaFileName,
                    status = "failed",
                    reason = string.Join("; ", result.Errors.Select(e => e.Error)),
                    artifact = (object?)null
                });
                continue;
            }

            var resolvedManifestJson = JsonSerializer.Serialize(result.ResolvedManifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var packageId = result.ResolvedManifest!.PackageId;
            var version = result.ResolvedManifest.Version;
            if (_artifactStore.ExistsAny(packageId, version))
            {
                results.Add(new
                {
                    fileName = artifact.MediaFileName,
                    status = "skipped",
                    reason = $"Artifact '{packageId}' version '{version}' already exists.",
                    artifact = (object?)null
                });
                continue;
            }

            await _artifactStore.SaveArtifactAndManifestAsync(packageId, version, artifact.MediaStream, resolvedManifestJson, cancellationToken, fileName: artifact.MediaFileName);

            var packageEntityId = DeterministicGuid($"{packageId}-{version}");
            var existingPackage = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId, cancellationToken);
            if (existingPackage is null)
            {
                _db.Packages.Add(new PackageEntity
                {
                    PackageId = packageEntityId,
                    Name = result.ResolvedManifest.PackageId,
                    Version = result.ResolvedManifest.Version,
                    SourcePath = result.ResolvedManifest.InstallAdapter?.Command ?? string.Empty,
                    InstallType = result.ResolvedManifest.InstallAdapter?.Type ?? "exe",
                    InstallArgs = result.ResolvedManifest.InstallAdapter?.Arguments ?? string.Empty,
                    ExpectedExitCodesJson = JsonSerializer.Serialize(
                        result.ResolvedManifest.InstallAdapter?.ExpectedExitCodes ?? new List<int> { 0, 3010 }),
                    DetectionConfigJson = JsonSerializer.Serialize(result.ResolvedManifest.Detection),
                    TimeoutSeconds = result.ResolvedManifest.InstallAdapter?.TimeoutSeconds ?? 300,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            results.Add(new
            {
                fileName = artifact.MediaFileName,
                status = "success",
                reason = (string?)null,
                artifact = new
                {
                    packageId = result.ResolvedManifest.PackageId,
                    version = result.ResolvedManifest.Version
                }
            });
        }

        foreach (var error in extraction.Errors)
        {
            results.Add(new
            {
                fileName = (string?)null,
                status = "failed",
                reason = error,
                artifact = (object?)null
            });
        }

        return results;
    }

    [HttpPost("bulk")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> BulkIngest()
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

        await using var stream = file.OpenReadStream();
        var actorId = User?.Identity?.Name ?? "anonymous";
        var results = await ProcessBulkArtifactsAsync(stream, actorId, HttpContext.RequestAborted);
        return Ok(new { results });
    }

    [HttpPost("upload-sessions")]
    public async Task<IActionResult> CreateUploadSession()
    {
        ArtifactIngestManifest? manifest = null;
        if (Request.ContentLength > 0)
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    manifest = JsonSerializer.Deserialize<ArtifactIngestManifest>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
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
            }
        }

        var session = _uploadSession.CreateSession(manifest);
        return StatusCode(StatusCodes.Status201Created, new { sessionId = session.SessionId });
    }

    [HttpPost("upload-sessions/{sessionId}/chunks")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadChunk(string sessionId)
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

        var indexStr = Request.Query["index"].FirstOrDefault();
        var totalStr = Request.Query["totalChunks"].FirstOrDefault();

        if (!int.TryParse(indexStr, out var index) || index < 0)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "index",
                        Error = "index is required and must be a non-negative integer"
                    }
                }
            });
        }

        if (!int.TryParse(totalStr, out var totalChunks) || totalChunks <= 0)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "totalChunks",
                        Error = "totalChunks is required and must be a positive integer"
                    }
                }
            });
        }

        var form = await Request.ReadFormAsync();
        var chunkFile = form.Files.GetFile("chunk");

        if (chunkFile is null)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "chunk",
                        Error = "chunk file is required"
                    }
                }
            });
        }

        try
        {
            await using var chunkStream = chunkFile.OpenReadStream();
            await _uploadSession.ReceiveChunk(sessionId, index, totalChunks, chunkStream);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "sessionId",
                        Error = ex.Message
                    }
                }
            });
        }

        return Ok();
    }

    [HttpPost("upload-sessions/{sessionId}/complete")]
    public async Task<IActionResult> CompleteUploadSession(string sessionId)
    {
        string assembledPath;
        try
        {
            assembledPath = _uploadSession.CompleteSession(sessionId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "sessionId",
                        Error = ex.Message
                    }
                }
            });
        }

        var session = _uploadSession.GetSession(sessionId);
        if (session is null)
        {
            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = new List<Contracts.Api.ValidationFieldError>
                {
                    new()
                    {
                        Field = "sessionId",
                        Error = "Upload session not found."
                    }
                }
            });
        }

        await using var stream = System.IO.File.OpenRead(assembledPath);
        var actorId = User?.Identity?.Name ?? "anonymous";

        // Detect bulk ZIP by attempting extraction; if valid artifacts found, process as bulk
        var isBulkZip = false;
        try
        {
            var bulkExtraction = _artifactZip.ExtractAndValidateBulkZip(stream);
            if (bulkExtraction.Artifacts.Count > 0 && bulkExtraction.Errors.Count == 0)
            {
                isBulkZip = true;
                stream.Position = 0;
                var results = await ProcessBulkArtifactsAsync(stream, actorId, HttpContext.RequestAborted);

                try
                {
                    _uploadSession.DeleteSession(sessionId);
                }
                catch
                {
                    // never fail cleanup
                }

                return Ok(new { results });
            }
        }
        catch
        {
            // not a valid bulk zip, fall through to single-artifact ingest
        }

        if (isBulkZip)
        {
            // already handled above
            return Ok(new { results = new List<object>() });
        }

        var manifest = session.Manifest ?? new ArtifactIngestManifest();
        var result = _artifactIngest.Ingest(Path.GetFileName(assembledPath), stream, manifest, actorId);

        if (!result.IsValid)
        {
            try
            {
                _uploadSession.DeleteSession(sessionId);
            }
            catch
            {
                // never fail cleanup
            }

            return BadRequest(new Contracts.Api.ValidationErrorResponse
            {
                Errors = result.Errors
            });
        }

        var resolvedManifestJson = JsonSerializer.Serialize(result.ResolvedManifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var packageId = result.ResolvedManifest!.PackageId;
        var version = result.ResolvedManifest.Version;
        if (_artifactStore.ExistsAny(packageId, version))
        {
            return Conflict(new { message = $"Artifact '{packageId}' version '{version}' already exists." });
        }

        await _artifactStore.SaveArtifactAndManifestAsync(packageId, version, stream, resolvedManifestJson, HttpContext.RequestAborted, fileName: Path.GetFileName(assembledPath));

        var packageEntityId = DeterministicGuid($"{packageId}-{version}");
        var existingPackage = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId, HttpContext.RequestAborted);
        if (existingPackage is null)
        {
            _db.Packages.Add(new PackageEntity
            {
                PackageId = packageEntityId,
                Name = result.ResolvedManifest.PackageId,
                Version = result.ResolvedManifest.Version,
                SourcePath = result.ResolvedManifest.InstallAdapter?.Command ?? string.Empty,
                InstallType = result.ResolvedManifest.InstallAdapter?.Type ?? "exe",
                InstallArgs = result.ResolvedManifest.InstallAdapter?.Arguments ?? string.Empty,
                ExpectedExitCodesJson = JsonSerializer.Serialize(
                    result.ResolvedManifest.InstallAdapter?.ExpectedExitCodes ?? new List<int> { 0, 3010 }),
                DetectionConfigJson = JsonSerializer.Serialize(result.ResolvedManifest.Detection),
                TimeoutSeconds = result.ResolvedManifest.InstallAdapter?.TimeoutSeconds ?? 300,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(HttpContext.RequestAborted);
        }

        try
        {
            _uploadSession.DeleteSession(sessionId);
        }
        catch
        {
            // never fail cleanup
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            resolvedManifest = result.ResolvedManifest,
            packageEntityId
        });
    }

    private static Guid DeterministicGuid(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
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

    [HttpGet]
    public IActionResult GetAll()
    {
        var artifacts = _artifactStore.ListArtifacts();
        foreach (var artifact in artifacts)
        {
            artifact.PackageEntityId = DeterministicGuid($"{artifact.PackageId}-{artifact.Version}");
        }
        return Ok(artifacts);
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

    [HttpGet("{packageEntityId:guid}/download")]
    [HttpHead("{packageEntityId:guid}/download")]
    public async Task<IActionResult> DownloadByPackageEntityId(Guid packageEntityId)
    {
        var package = await _db.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageId == packageEntityId, HttpContext.RequestAborted);

        if (package is null)
        {
            return NotFound();
        }

        try
        {
            var stream = _artifactStore.OpenRead(package.Name, package.Version);
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

    [HttpDelete("{packageId}/{version}")]
    public async Task<IActionResult> Delete(string packageId, string version)
    {
        try
        {
            if (!_artifactStore.ExistsAny(packageId, version))
            {
                return NotFound();
            }

            var deleted = _artifactStore.DeleteArtifactAsync(packageId, version);
            if (!deleted)
            {
                return Problem(title: "Artifact delete failed", statusCode: StatusCodes.Status500InternalServerError);
            }

            var packageEntityId = DeterministicGuid($"{packageId}-{version}");
            var existingPackage = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId, HttpContext.RequestAborted);
            if (existingPackage is not null)
            {
                _db.Packages.Remove(existingPackage);
                await _db.SaveChangesAsync(HttpContext.RequestAborted);
            }

            return NoContent();
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
    }
}
