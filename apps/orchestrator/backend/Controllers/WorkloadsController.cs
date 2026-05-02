using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Contracts.Api.Workloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using DeploymentPoC.Orchestrator.Services;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/workloads")]
public sealed class WorkloadsController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly ArtifactStoreService _artifactStore;

    public WorkloadsController(InstallerDbContext db, ArtifactStoreService artifactStore)
    {
        _db = db;
        _artifactStore = artifactStore;
    }

    [HttpPost]
    public async Task<ActionResult<WorkloadDetailResponse>> Create([FromBody] CreateWorkloadRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ToValidationErrorResponse(ModelState));
        }

        var now = DateTime.UtcNow;
        var workload = new WorkloadDefinitionEntity
        {
            WorkloadId = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.WorkloadDefinitions.Add(workload);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { workloadId = workload.WorkloadId }, MapWorkloadDetail(workload, Array.Empty<WorkloadRevisionEntity>(), new Dictionary<Guid, Data.Entities.PackageEntity>()));
    }

    [HttpPost("{workloadId:guid}/revisions")]
    public async Task<ActionResult<WorkloadRevisionDto>> CreateRevision(Guid workloadId, [FromBody] CreateWorkloadRevisionRequest request)
    {
        var errors = new List<ValidationFieldError>();
        if (!ModelState.IsValid)
        {
            errors.AddRange(ToValidationErrorResponse(ModelState).Errors);
        }

        if (request.Packages.Count == 0)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages",
                Error = "Workload revisions must include at least 1 package"
            });
        }

        if (request.Packages.Select(p => p.PackageIndex).Distinct().Count() != request.Packages.Count)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages",
                Error = "PackageIndex values must be unique"
            });
        }

        if (request.Packages.Any(p => p.PackageId == Guid.Empty))
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages.packageId",
                Error = "PackageId must be a non-empty GUID"
            });
        }

        var workload = await _db.WorkloadDefinitions.SingleOrDefaultAsync(w => w.WorkloadId == workloadId);
        if (workload is null)
        {
            return NotFound(new { message = $"Workload {workloadId} not found" });
        }

        var packageIds = request.Packages.Select(p => p.PackageId).Distinct().ToList();
        var existingPackages = await _db.Packages
            .Where(p => packageIds.Contains(p.PackageId))
            .ToDictionaryAsync(p => p.PackageId);
        if (existingPackages.Count != packageIds.Count)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages",
                Error = "One or more package ids were not found"
            });
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ValidationErrorResponse
            {
                Errors = errors
            });
        }

        var revision = new WorkloadRevisionEntity
        {
            RevisionId = Guid.NewGuid(),
            WorkloadId = workloadId,
            Version = request.Version.Trim(),
            IsPublished = false,
            CreatedAtUtc = DateTime.UtcNow,
            PreWorkloadStepsJson = request.PreWorkloadSteps is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(request.PreWorkloadSteps)
                : "[]",
            PostWorkloadStepsJson = request.PostWorkloadSteps is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(request.PostWorkloadSteps)
                : "[]",
            PreUninstallStepsJson = request.PreUninstallSteps is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(request.PreUninstallSteps)
                : "[]",
            PostUninstallStepsJson = request.PostUninstallSteps is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(request.PostUninstallSteps)
                : "[]",
            DefaultShell = string.IsNullOrWhiteSpace(request.DefaultShell) ? "powershell" : request.DefaultShell,
            Packages = request.Packages
                .OrderBy(p => p.PackageIndex)
                .Select(p => new WorkloadPackageEntity
                {
                    WorkloadPackageId = Guid.NewGuid(),
                    PackageId = p.PackageId,
                    PackageIndex = p.PackageIndex,
                    PreInitStepsJson = p.PreInitSteps is { Count: > 0 }
                        ? System.Text.Json.JsonSerializer.Serialize(p.PreInitSteps)
                        : "[]",
                    PostInitStepsJson = p.PostInitSteps is { Count: > 0 }
                        ? System.Text.Json.JsonSerializer.Serialize(p.PostInitSteps)
                        : "[]"
                })
                .ToList()
        };

        _db.WorkloadRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var revisionDto = new WorkloadRevisionDto
        {
            RevisionId = revision.RevisionId,
            Version = revision.Version,
            IsPublished = revision.IsPublished,
            CreatedAtUtc = revision.CreatedAtUtc,
            PreWorkloadSteps = DeserializeStringList(revision.PreWorkloadStepsJson),
            PostWorkloadSteps = DeserializeStringList(revision.PostWorkloadStepsJson),
            PreUninstallSteps = DeserializeStringList(revision.PreUninstallStepsJson),
            PostUninstallSteps = DeserializeStringList(revision.PostUninstallStepsJson),
            DefaultShell = revision.DefaultShell,
            Packages = revision.Packages
                .OrderBy(p => p.PackageIndex)
                .Select(p =>
                {
                    var pkg = existingPackages.GetValueOrDefault(p.PackageId);
                    return new WorkloadPackageDto
                    {
                        PackageId = p.PackageId,
                        PackageIndex = p.PackageIndex,
                        PackageName = pkg?.Name ?? string.Empty,
                        PackageVersion = pkg?.Version ?? string.Empty,
                        PreInitSteps = DeserializeStringList(p.PreInitStepsJson),
                        PostInitSteps = DeserializeStringList(p.PostInitStepsJson)
                    };
                })
                .ToList()
        };

        return Created($"/api/workloads/{workloadId}", revisionDto);
    }

    [HttpPut("{workloadId:guid}/revisions/{revisionId:guid}")]
    public async Task<ActionResult<WorkloadRevisionUpdateResponse>> UpdateRevision(Guid workloadId, Guid revisionId, [FromBody] UpdateWorkloadRevisionRequest request)
    {
        var errors = new List<ValidationFieldError>();
        if (!ModelState.IsValid)
        {
            errors.AddRange(ToValidationErrorResponse(ModelState).Errors);
        }

        if (request.Packages.Count == 0)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages",
                Error = "Workload revisions must include at least 1 package"
            });
        }

        if (request.Packages.Select(p => p.PackageIndex).Distinct().Count() != request.Packages.Count)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages",
                Error = "PackageIndex values must be unique"
            });
        }

        if (request.Packages.Any(p => p.PackageId == Guid.Empty))
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages.packageId",
                Error = "PackageId must be a non-empty GUID"
            });
        }

        var workload = await _db.WorkloadDefinitions.SingleOrDefaultAsync(w => w.WorkloadId == workloadId);
        if (workload is null)
        {
            return NotFound(new { message = $"Workload {workloadId} not found" });
        }

        var revision = await _db.WorkloadRevisions
            .Include(r => r.Packages)
            .SingleOrDefaultAsync(r => r.RevisionId == revisionId && r.WorkloadId == workloadId);
        if (revision is null)
        {
            return NotFound(new { message = $"Revision {revisionId} not found for workload {workloadId}" });
        }

        var packageIds = request.Packages.Select(p => p.PackageId).Distinct().ToList();
        var existingPackages = await _db.Packages
            .Where(p => packageIds.Contains(p.PackageId))
            .ToDictionaryAsync(p => p.PackageId);
        if (existingPackages.Count != packageIds.Count)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages",
                Error = "One or more package ids were not found"
            });
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ValidationErrorResponse
            {
                Errors = errors
            });
        }

        // Diff check
        var newPreWorkloadSteps = request.PreWorkloadSteps is { Count: > 0 }
            ? JsonSerializer.Serialize(request.PreWorkloadSteps)
            : "[]";
        var newPostWorkloadSteps = request.PostWorkloadSteps is { Count: > 0 }
            ? JsonSerializer.Serialize(request.PostWorkloadSteps)
            : "[]";
        var newPreUninstallSteps = request.PreUninstallSteps is { Count: > 0 }
            ? JsonSerializer.Serialize(request.PreUninstallSteps)
            : "[]";
        var newPostUninstallSteps = request.PostUninstallSteps is { Count: > 0 }
            ? JsonSerializer.Serialize(request.PostUninstallSteps)
            : "[]";
        var newDefaultShell = string.IsNullOrWhiteSpace(request.DefaultShell) ? "powershell" : request.DefaultShell;
        var newVersion = request.Version.Trim();

        var orderedNewPackages = request.Packages.OrderBy(p => p.PackageIndex).ToList();
        var orderedOldPackages = revision.Packages.OrderBy(p => p.PackageIndex).ToList();

        bool hasChanges =
            revision.Version != newVersion ||
            revision.DefaultShell != newDefaultShell ||
            !StringJsonListsEqual(revision.PreWorkloadStepsJson, newPreWorkloadSteps) ||
            !StringJsonListsEqual(revision.PostWorkloadStepsJson, newPostWorkloadSteps) ||
            !StringJsonListsEqual(revision.PreUninstallStepsJson, newPreUninstallSteps) ||
            !StringJsonListsEqual(revision.PostUninstallStepsJson, newPostUninstallSteps) ||
            orderedOldPackages.Count != orderedNewPackages.Count;

        if (!hasChanges)
        {
            for (int i = 0; i < orderedOldPackages.Count; i++)
            {
                var oldPkg = orderedOldPackages[i];
                var newPkg = orderedNewPackages[i];
                var newPreInit = newPkg.PreInitSteps is { Count: > 0 } ? JsonSerializer.Serialize(newPkg.PreInitSteps) : "[]";
                var newPostInit = newPkg.PostInitSteps is { Count: > 0 } ? JsonSerializer.Serialize(newPkg.PostInitSteps) : "[]";

                if (oldPkg.PackageId != newPkg.PackageId ||
                    oldPkg.PackageIndex != newPkg.PackageIndex ||
                    !StringJsonListsEqual(oldPkg.PreInitStepsJson, newPreInit) ||
                    !StringJsonListsEqual(oldPkg.PostInitStepsJson, newPostInit))
                {
                    hasChanges = true;
                    break;
                }
            }
        }

        if (!hasChanges)
        {
            var unchangedDto = new WorkloadRevisionDto
            {
                RevisionId = revision.RevisionId,
                Version = revision.Version,
                IsPublished = revision.IsPublished,
                CreatedAtUtc = revision.CreatedAtUtc,
                PreWorkloadSteps = DeserializeStringList(revision.PreWorkloadStepsJson),
                PostWorkloadSteps = DeserializeStringList(revision.PostWorkloadStepsJson),
                PreUninstallSteps = DeserializeStringList(revision.PreUninstallStepsJson),
                PostUninstallSteps = DeserializeStringList(revision.PostUninstallStepsJson),
                DefaultShell = revision.DefaultShell,
                Packages = revision.Packages
                    .OrderBy(p => p.PackageIndex)
                    .Select(p =>
                    {
                        var pkg = existingPackages.GetValueOrDefault(p.PackageId);
                        return new WorkloadPackageDto
                        {
                            PackageId = p.PackageId,
                            PackageIndex = p.PackageIndex,
                            PackageName = pkg?.Name ?? string.Empty,
                            PackageVersion = pkg?.Version ?? string.Empty,
                            PreInitSteps = DeserializeStringList(p.PreInitStepsJson),
                            PostInitSteps = DeserializeStringList(p.PostInitStepsJson)
                        };
                    })
                    .ToList()
            };

            return Ok(new WorkloadRevisionUpdateResponse
            {
                Changed = false,
                Revision = unchangedDto
            });
        }

        // Apply updates
        revision.Version = newVersion;
        revision.DefaultShell = newDefaultShell;
        revision.PreWorkloadStepsJson = newPreWorkloadSteps;
        revision.PostWorkloadStepsJson = newPostWorkloadSteps;
        revision.PreUninstallStepsJson = newPreUninstallSteps;
        revision.PostUninstallStepsJson = newPostUninstallSteps;
        workload.UpdatedAtUtc = DateTime.UtcNow;

        // Remove old packages and add new ones
        _db.WorkloadPackages.RemoveRange(revision.Packages);
        revision.Packages = orderedNewPackages.Select(p => new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = revision.RevisionId,
            PackageId = p.PackageId,
            PackageIndex = p.PackageIndex,
            PreInitStepsJson = p.PreInitSteps is { Count: > 0 } ? JsonSerializer.Serialize(p.PreInitSteps) : "[]",
            PostInitStepsJson = p.PostInitSteps is { Count: > 0 } ? JsonSerializer.Serialize(p.PostInitSteps) : "[]"
        }).ToList();

        await _db.SaveChangesAsync();

        var updatedDto = new WorkloadRevisionDto
        {
            RevisionId = revision.RevisionId,
            Version = revision.Version,
            IsPublished = revision.IsPublished,
            CreatedAtUtc = revision.CreatedAtUtc,
            PreWorkloadSteps = DeserializeStringList(revision.PreWorkloadStepsJson),
            PostWorkloadSteps = DeserializeStringList(revision.PostWorkloadStepsJson),
            PreUninstallSteps = DeserializeStringList(revision.PreUninstallStepsJson),
            PostUninstallSteps = DeserializeStringList(revision.PostUninstallStepsJson),
            DefaultShell = revision.DefaultShell,
            Packages = revision.Packages
                .OrderBy(p => p.PackageIndex)
                .Select(p =>
                {
                    var pkg = existingPackages.GetValueOrDefault(p.PackageId);
                    return new WorkloadPackageDto
                    {
                        PackageId = p.PackageId,
                        PackageIndex = p.PackageIndex,
                        PackageName = pkg?.Name ?? string.Empty,
                        PackageVersion = pkg?.Version ?? string.Empty,
                        PreInitSteps = DeserializeStringList(p.PreInitStepsJson),
                        PostInitSteps = DeserializeStringList(p.PostInitStepsJson)
                    };
                })
                .ToList()
        };

        return Ok(new WorkloadRevisionUpdateResponse
        {
            Changed = true,
            Revision = updatedDto
        });
    }

    private static bool StringJsonListsEqual(string? jsonA, string? jsonB)
    {
        var a = DeserializeStringList(jsonA);
        var b = DeserializeStringList(jsonB);
        return a.Count == b.Count && a.SequenceEqual(b);
    }

    [HttpPost("{workloadId:guid}/publish")]
    public async Task<ActionResult<WorkloadDetailResponse>> Publish(Guid workloadId, [FromBody] PublishWorkloadRequest request)
    {
        if (request.RevisionId == Guid.Empty)
        {
            return BadRequest(new ValidationErrorResponse
            {
                Errors = new List<ValidationFieldError>
                {
                    new()
                    {
                        Field = "revisionId",
                        Error = "RevisionId is required"
                    }
                }
            });
        }

        var workload = await _db.WorkloadDefinitions.SingleOrDefaultAsync(w => w.WorkloadId == workloadId);
        if (workload is null)
        {
            return NotFound(new { message = $"Workload {workloadId} not found" });
        }

        var revision = await _db.WorkloadRevisions
            .Include(r => r.Packages)
            .SingleOrDefaultAsync(r => r.RevisionId == request.RevisionId && r.WorkloadId == workloadId);
        if (revision is null)
        {
            return NotFound(new { message = $"Revision {request.RevisionId} not found for workload {workloadId}" });
        }

        if (request.ReplacePublished)
        {
            var allRevisions = await _db.WorkloadRevisions
                .Where(r => r.WorkloadId == workloadId)
                .ToListAsync();
            foreach (var item in allRevisions)
            {
                item.IsPublished = item.RevisionId == revision.RevisionId;
            }
        }
        else
        {
            revision.IsPublished = true;
        }

        workload.PublishedRevisionId = revision.RevisionId;
        workload.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var revisions = await _db.WorkloadRevisions
            .Where(r => r.WorkloadId == workloadId)
            .Include(r => r.Packages)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        var packageIds = revisions.SelectMany(r => r.Packages).Select(p => p.PackageId).Distinct().ToList();
        var packages = await _db.Packages
            .Where(p => packageIds.Contains(p.PackageId))
            .ToDictionaryAsync(p => p.PackageId);
        return Ok(MapWorkloadDetail(workload, revisions, packages));
    }

    [HttpGet]
    public async Task<ActionResult<WorkloadListResponse>> GetAll()
    {
        var workloads = await _db.WorkloadDefinitions
            .OrderBy(w => w.Name)
            .Select(w => new WorkloadSummaryDto
            {
                WorkloadId = w.WorkloadId,
                Name = w.Name,
                Description = w.Description,
                PublishedRevisionId = w.PublishedRevisionId,
                CreatedAtUtc = w.CreatedAtUtc,
                UpdatedAtUtc = w.UpdatedAtUtc,
                RevisionCount = w.Revisions.Count,
                LatestRevision = w.Revisions
                    .OrderByDescending(r => r.IsPublished)
                    .ThenByDescending(r => r.CreatedAtUtc)
                    .Select(r => new WorkloadRevisionSummaryDto
                    {
                        RevisionId = r.RevisionId,
                        Version = r.Version,
                        IsPublished = r.IsPublished
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(new WorkloadListResponse { Workloads = workloads });
    }

    [HttpGet("{workloadId:guid}")]
    public async Task<ActionResult<WorkloadDetailResponse>> GetById(Guid workloadId)
    {
        var workload = await _db.WorkloadDefinitions.SingleOrDefaultAsync(w => w.WorkloadId == workloadId);
        if (workload is null)
        {
            return NotFound(new { message = $"Workload {workloadId} not found" });
        }

        var revisions = await _db.WorkloadRevisions
            .Where(r => r.WorkloadId == workloadId)
            .Include(r => r.Packages)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        var packageIds = revisions.SelectMany(r => r.Packages).Select(p => p.PackageId).Distinct().ToList();
        var packages = await _db.Packages
            .Where(p => packageIds.Contains(p.PackageId))
            .ToDictionaryAsync(p => p.PackageId);
        return Ok(MapWorkloadDetail(workload, revisions, packages));
    }

    [HttpGet("{workloadId:guid}/installed-revisions")]
    public async Task<ActionResult<List<InstalledRevisionResponse>>> GetInstalledRevisions(Guid workloadId)
    {
        var workload = await _db.WorkloadDefinitions.SingleOrDefaultAsync(w => w.WorkloadId == workloadId);
        if (workload is null)
        {
            return NotFound(new { message = $"Workload {workloadId} not found" });
        }

        var installedRevisions = await _db.NodeWorkloadStates
            .AsNoTracking()
            .Where(s => s.WorkloadId == workloadId && s.CurrentRevisionId != null)
            .Select(s => s.CurrentRevisionId!.Value)
            .Distinct()
            .ToListAsync();

        if (installedRevisions.Count == 0)
        {
            return Ok(new List<InstalledRevisionResponse>());
        }

        var revisionDetails = await _db.WorkloadRevisions
            .AsNoTracking()
            .Where(r => installedRevisions.Contains(r.RevisionId))
            .Select(r => new InstalledRevisionResponse
            {
                RevisionId = r.RevisionId,
                Version = r.Version,
                IsPublished = r.IsPublished
            })
            .ToListAsync();

        return Ok(revisionDetails);
    }

    [HttpDelete("{workloadId:guid}")]
    public async Task<ActionResult> Delete(Guid workloadId)
    {
        var workload = await _db.WorkloadDefinitions
            .SingleOrDefaultAsync(w => w.WorkloadId == workloadId);

        if (workload is null)
        {
            return NotFound(new { message = $"Workload {workloadId} not found" });
        }

        var revisionIds = await _db.WorkloadRevisions
            .Where(r => r.WorkloadId == workloadId)
            .Select(r => r.RevisionId)
            .ToListAsync();

        if (revisionIds.Count > 0)
        {
            await _db.WorkloadPackages
                .Where(wp => revisionIds.Contains(wp.RevisionId))
                .ExecuteDeleteAsync();
        }

        await _db.WorkloadRuns
            .Where(wr => wr.WorkloadId == workloadId)
            .ExecuteDeleteAsync();

        if (revisionIds.Count > 0)
        {
            await _db.WorkloadRevisions
                .Where(r => revisionIds.Contains(r.RevisionId))
                .ExecuteDeleteAsync();
        }

        await _db.NodeWorkloadStates
            .Where(nws => nws.WorkloadId == workloadId)
            .ExecuteDeleteAsync();

        _db.WorkloadDefinitions.Remove(workload);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static WorkloadDetailResponse MapWorkloadDetail(WorkloadDefinitionEntity workload, IEnumerable<WorkloadRevisionEntity> revisions, Dictionary<Guid, Data.Entities.PackageEntity> packages)
    {
        return new WorkloadDetailResponse
        {
            WorkloadId = workload.WorkloadId,
            Name = workload.Name,
            Description = workload.Description,
            PublishedRevisionId = workload.PublishedRevisionId,
            CreatedAtUtc = workload.CreatedAtUtc,
            UpdatedAtUtc = workload.UpdatedAtUtc,
            Revisions = revisions
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new WorkloadRevisionDto
                {
                    RevisionId = r.RevisionId,
                    Version = r.Version,
                    IsPublished = r.IsPublished,
                    CreatedAtUtc = r.CreatedAtUtc,
                    PreWorkloadSteps = DeserializeStringList(r.PreWorkloadStepsJson),
                    PostWorkloadSteps = DeserializeStringList(r.PostWorkloadStepsJson),
                    PreUninstallSteps = DeserializeStringList(r.PreUninstallStepsJson),
                    PostUninstallSteps = DeserializeStringList(r.PostUninstallStepsJson),
                    DefaultShell = r.DefaultShell,
                    Packages = r.Packages
                        .OrderBy(p => p.PackageIndex)
                        .Select(p =>
                        {
                            var pkg = packages.GetValueOrDefault(p.PackageId);
                            return new WorkloadPackageDto
                            {
                                PackageId = p.PackageId,
                                PackageIndex = p.PackageIndex,
                                PackageName = pkg?.Name ?? string.Empty,
                                PackageVersion = pkg?.Version ?? string.Empty,
                                PreInitSteps = DeserializeStringList(p.PreInitStepsJson),
                                PostInitSteps = DeserializeStringList(p.PostInitStepsJson)
                            };
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<string>();
        }
    }

    private static ValidationErrorResponse ToValidationErrorResponse(ModelStateDictionary modelState)
    {
        var errors = new List<ValidationFieldError>();
        foreach (var kvp in modelState)
        {
            if (kvp.Value is null || kvp.Value.Errors.Count == 0)
            {
                continue;
            }

            foreach (var modelError in kvp.Value.Errors)
            {
                errors.Add(new ValidationFieldError
                {
                    Field = string.IsNullOrWhiteSpace(kvp.Key) ? "request" : char.ToLowerInvariant(kvp.Key[0]) + kvp.Key[1..],
                    Error = string.IsNullOrWhiteSpace(modelError.ErrorMessage) ? "Invalid value" : modelError.ErrorMessage
                });
            }
        }

        return new ValidationErrorResponse
        {
            Errors = errors
        };
    }

    private static Guid DeterministicGuid(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private sealed class AdapterResolution
    {
        public string InstallType { get; set; } = "exe";
        public string SourcePath { get; set; } = "{artifactPath}";
        public string InstallArgs { get; set; } = "";
        public string UninstallArgs { get; set; } = "";
        public string UpgradeBehavior { get; set; } = "InPlace";
        public string ExpectedExitCodesJson { get; set; } = "[0]";
        public int TimeoutSeconds { get; set; } = 300;
        public string? DetectionConfigJson { get; set; }
    }

    private async Task<AdapterResolution>
        ResolvePlaceholderAdapter(string packageId, string version)
    {
        var manifestJson = await _artifactStore.GetResolvedManifestAsync(packageId, version);
        if (!string.IsNullOrWhiteSpace(manifestJson))
        {
            try
            {
                var manifest = System.Text.Json.JsonSerializer.Deserialize<ResolvedManifest>(manifestJson, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (manifest?.InstallAdapter is not null)
                {
                    var adapter = manifest.InstallAdapter;
                    string? detectionConfigJson = null;
                    if (manifest.Detection is not null)
                    {
                        var detectionConfig = new DeploymentPoC.Contracts.Runtime.RunPayloads.DetectionConfig
                        {
                            Type = manifest.Detection.Type ?? "version_manifest",
                            Path = manifest.Detection.Path ?? packageId
                        };
                        detectionConfigJson = System.Text.Json.JsonSerializer.Serialize(detectionConfig);
                    }

                    return new AdapterResolution
                    {
                        InstallType = string.IsNullOrWhiteSpace(adapter.Type) ? "exe" : adapter.Type,
                        SourcePath = string.IsNullOrWhiteSpace(adapter.Command) ? "{artifactPath}" : adapter.Command,
                        InstallArgs = adapter.Arguments ?? "",
                        UninstallArgs = adapter.UninstallArgs ?? "",
                        UpgradeBehavior = string.IsNullOrWhiteSpace(adapter.UpgradeBehavior) ? "InPlace" : adapter.UpgradeBehavior,
                        ExpectedExitCodesJson = adapter.ExpectedExitCodes is { Count: > 0 }
                            ? System.Text.Json.JsonSerializer.Serialize(adapter.ExpectedExitCodes)
                            : "[0]",
                        TimeoutSeconds = adapter.TimeoutSeconds > 0 ? adapter.TimeoutSeconds : 300,
                        DetectionConfigJson = detectionConfigJson
                    };
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // fallback to default
            }
        }

        return new AdapterResolution();
    }

    [HttpPost("bulk-import")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<BulkImportResponse>> BulkImport(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "File is required" });
        }

        var fileName = file.FileName.ToLowerInvariant();
        if (!fileName.EndsWith(".json") && !fileName.EndsWith(".jsonc"))
        {
            return BadRequest(new { message = "Only .json or .jsonc files are accepted" });
        }

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();
            var workloads = System.Text.Json.JsonSerializer.Deserialize<List<WorkloadImportModel>>(content, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (workloads is null || workloads.Count == 0)
            {
                return BadRequest(new { message = "No workload definitions found in file" });
            }

            var results = new List<BulkImportResultItem>();
            var now = DateTime.UtcNow;

            foreach (var w in workloads)
            {
                if (string.IsNullOrWhiteSpace(w.Name) || string.IsNullOrWhiteSpace(w.Slug))
                {
                    results.Add(new BulkImportResultItem
                    {
                        Name = w.Name ?? "unknown",
                        Slug = w.Slug ?? "unknown",
                        Status = "failed",
                        Reason = "Name and slug are required"
                    });
                    continue;
                }

                if (w.RawPackages is null || w.RawPackages.Count == 0)
                {
                    results.Add(new BulkImportResultItem
                    {
                        Name = w.Name,
                        Slug = w.Slug,
                        Status = "failed",
                        Reason = "Workloads must have at least 1 package"
                    });
                    continue;
                }

                var parsedPackages = new List<(string PackageId, string Version, List<string> PreInitSteps, List<string> PostInitSteps)>();
                var packageValidationFailed = false;

                for (int pi = 0; pi < w.RawPackages.Count; pi++)
                {
                    var element = w.RawPackages[pi];

                    if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var packageSlug = element.GetString()!;
                        var lastHyphen = packageSlug.LastIndexOf('-');
                        if (lastHyphen <= 0 || lastHyphen == packageSlug.Length - 1)
                        {
                            results.Add(new BulkImportResultItem
                            {
                                Name = w.Name,
                                Slug = w.Slug,
                                Status = "failed",
                                Reason = $"Invalid package format '{packageSlug}': expected {{packageId}}-{{version}}"
                            });
                            packageValidationFailed = true;
                            break;
                        }

                        var packageId = packageSlug[..lastHyphen];
                        var version = packageSlug[(lastHyphen + 1)..];
                        parsedPackages.Add((packageId, version, new List<string>(), new List<string>()));
                    }
                    else if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var nameProp = element.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.IsNullOrWhiteSpace(nameProp))
                        {
                            results.Add(new BulkImportResultItem
                            {
                                Name = w.Name,
                                Slug = w.Slug,
                                Status = "failed",
                                Reason = $"Package at index {pi} is missing required 'name' property"
                            });
                            packageValidationFailed = true;
                            break;
                        }

                        var lastHyphen = nameProp.LastIndexOf('-');
                        if (lastHyphen <= 0 || lastHyphen == nameProp.Length - 1)
                        {
                            results.Add(new BulkImportResultItem
                            {
                                Name = w.Name,
                                Slug = w.Slug,
                                Status = "failed",
                                Reason = $"Invalid package name format '{nameProp}': expected {{packageId}}-{{version}}"
                            });
                            packageValidationFailed = true;
                            break;
                        }

                        var packageId = nameProp[..lastHyphen];
                        var version = nameProp[(lastHyphen + 1)..];

                        var preInitSteps = ParseInitSteps(element, "preInitSteps");
                        var postInitSteps = ParseInitSteps(element, "postInitSteps");

                        if (preInitSteps is null || postInitSteps is null)
                        {
                            results.Add(new BulkImportResultItem
                            {
                                Name = w.Name,
                                Slug = w.Slug,
                                Status = "failed",
                                Reason = $"Package at index {pi} has invalid init steps (empty string or command exceeds 4096 characters)"
                            });
                            packageValidationFailed = true;
                            break;
                        }

                        parsedPackages.Add((packageId, version, preInitSteps, postInitSteps));
                    }
                    else
                    {
                        results.Add(new BulkImportResultItem
                        {
                            Name = w.Name,
                            Slug = w.Slug,
                            Status = "failed",
                            Reason = $"Package at index {pi} must be a string or object"
                        });
                        packageValidationFailed = true;
                        break;
                    }
                }

                if (packageValidationFailed)
                {
                    continue;
                }

                var workloadPreWorkloadSteps = ParseInitStepsList(w.PreWorkloadSteps);
                var workloadPostWorkloadSteps = ParseInitStepsList(w.PostWorkloadSteps);

                if (workloadPreWorkloadSteps is null || workloadPostWorkloadSteps is null)
                {
                    results.Add(new BulkImportResultItem
                    {
                        Name = w.Name,
                        Slug = w.Slug,
                        Status = "failed",
                        Reason = "Workload-level init steps contain validation errors (empty string or command exceeds 4096 characters)"
                    });
                    continue;
                }

                var workloadPreUninstallSteps = ParseInitStepsList(w.PreUninstallSteps);
                var workloadPostUninstallSteps = ParseInitStepsList(w.PostUninstallSteps);

                if (workloadPreUninstallSteps is null || workloadPostUninstallSteps is null)
                {
                    results.Add(new BulkImportResultItem
                    {
                        Name = w.Name,
                        Slug = w.Slug,
                        Status = "failed",
                        Reason = "Workload-level uninstall steps contain validation errors (empty string or command exceeds 4096 characters)"
                    });
                    continue;
                }

                var workloadName = w.Name.Trim();
                var workloadVersion = string.IsNullOrWhiteSpace(w.Version) ? "1.0.0" : w.Version.Trim();
                var existingWorkload = await _db.WorkloadDefinitions
                    .SingleOrDefaultAsync(wd => wd.Name == workloadName);

                WorkloadDefinitionEntity workload;
                if (existingWorkload is not null)
                {
                    var existingRevision = await _db.WorkloadRevisions
                        .SingleOrDefaultAsync(r => r.WorkloadId == existingWorkload.WorkloadId && r.Version == workloadVersion);

                    if (existingRevision is not null)
                    {
                        results.Add(new BulkImportResultItem
                        {
                            Name = w.Name,
                            Slug = w.Slug,
                            Status = "skipped",
                            Reason = "Workload with this name and version already exists"
                        });
                        continue;
                    }

                    workload = existingWorkload;
                    workload.UpdatedAtUtc = now;
                }
                else
                {
                    workload = new WorkloadDefinitionEntity
                    {
                        WorkloadId = Guid.NewGuid(),
                        Name = workloadName,
                        Description = string.IsNullOrWhiteSpace(w.Description) ? null : w.Description.Trim(),
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    _db.WorkloadDefinitions.Add(workload);
                }

                var revision = new WorkloadRevisionEntity
                {
                    RevisionId = Guid.NewGuid(),
                    WorkloadId = workload.WorkloadId,
                    Version = workloadVersion,
                    IsPublished = true,
                    CreatedAtUtc = now,
                    PreWorkloadStepsJson = JsonSerializeList(workloadPreWorkloadSteps),
                    PostWorkloadStepsJson = JsonSerializeList(workloadPostWorkloadSteps),
                    PreUninstallStepsJson = JsonSerializeList(workloadPreUninstallSteps),
                    PostUninstallStepsJson = JsonSerializeList(workloadPostUninstallSteps),
                    DefaultShell = string.IsNullOrWhiteSpace(w.DefaultShell) ? "powershell" : w.DefaultShell
                };

                _db.WorkloadRevisions.Add(revision);

                for (int i = 0; i < parsedPackages.Count; i++)
                {
                    var (packageId, version, preInitSteps, postInitSteps) = parsedPackages[i];
                    var deterministicId = DeterministicGuid($"{packageId}-{version}");

                    var existingPackage = await _db.Packages
                        .Where(p => p.PackageId == deterministicId)
                        .FirstOrDefaultAsync();

                    if (existingPackage is null)
                    {
                        var adapter = await ResolvePlaceholderAdapter(packageId, version);
                        var packageEntity = new PackageEntity
                        {
                            PackageId = deterministicId,
                            Name = packageId,
                            Version = version,
                            SourcePath = adapter.SourcePath,
                            InstallType = adapter.InstallType,
                            InstallArgs = adapter.InstallArgs,
                            UninstallArgs = adapter.UninstallArgs,
                            ExpectedExitCodesJson = adapter.ExpectedExitCodesJson,
                            TimeoutSeconds = adapter.TimeoutSeconds,
                            DetectionConfigJson = adapter.DetectionConfigJson ?? "",
                            UpgradeBehavior = adapter.UpgradeBehavior,
                            CreatedAtUtc = now
                        };
                        _db.Packages.Add(packageEntity);

                        revision.Packages.Add(new WorkloadPackageEntity
                        {
                            WorkloadPackageId = Guid.NewGuid(),
                            RevisionId = revision.RevisionId,
                            PackageId = packageEntity.PackageId,
                            PackageIndex = i + 1,
                            PreInitStepsJson = JsonSerializeList(preInitSteps),
                            PostInitStepsJson = JsonSerializeList(postInitSteps)
                        });
                    }
                    else
                    {
                        revision.Packages.Add(new WorkloadPackageEntity
                        {
                            WorkloadPackageId = Guid.NewGuid(),
                            RevisionId = revision.RevisionId,
                            PackageId = existingPackage.PackageId,
                            PackageIndex = i + 1,
                            PreInitStepsJson = JsonSerializeList(preInitSteps),
                            PostInitStepsJson = JsonSerializeList(postInitSteps)
                        });
                    }
                }

                await _db.SaveChangesAsync();
                workload.PublishedRevisionId = revision.RevisionId;
                await _db.SaveChangesAsync();

                results.Add(new BulkImportResultItem
                {
                    Name = w.Name,
                    Slug = w.Slug,
                    Status = "success"
                });
            }

            return Ok(new BulkImportResponse { Results = results });
        }
        catch (System.Text.Json.JsonException ex)
        {
            return BadRequest(new { message = $"Invalid JSON format: {ex.Message}" });
        }
    }

    private static List<string>? ParseInitSteps(System.Text.Json.JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arr) || arr.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }
            var cmd = item.GetString() ?? "";
            if (string.IsNullOrEmpty(cmd))
            {
                return null;
            }
            if (cmd.Length > 4096)
            {
                return null;
            }
            result.Add(cmd);
        }
        return result;
    }

    private static List<string>? ParseInitStepsList(List<System.Text.Json.JsonElement>? elements)
    {
        if (elements is null || elements.Count == 0)
        {
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var item in elements)
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }
            var cmd = item.GetString() ?? "";
            if (string.IsNullOrEmpty(cmd))
            {
                return null;
            }
            if (cmd.Length > 4096)
            {
                return null;
            }
            result.Add(cmd);
        }
        return result;
    }

    private static string JsonSerializeList(List<string>? list)
    {
        if (list is null || list.Count == 0)
        {
            return "[]";
        }
        return System.Text.Json.JsonSerializer.Serialize(list);
    }
}

public sealed class WorkloadImportModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string Slug { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("packages")]
    public List<System.Text.Json.JsonElement>? RawPackages { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("preWorkloadSteps")]
    public List<System.Text.Json.JsonElement>? PreWorkloadSteps { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("postWorkloadSteps")]
    public List<System.Text.Json.JsonElement>? PostWorkloadSteps { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("preUninstallSteps")]
    public List<System.Text.Json.JsonElement>? PreUninstallSteps { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("postUninstallSteps")]
    public List<System.Text.Json.JsonElement>? PostUninstallSteps { get; set; }

    public string? DefaultShell { get; set; }
}

public sealed class BulkImportResponse
{
    public List<BulkImportResultItem> Results { get; set; } = new();
}

public sealed class BulkImportResultItem
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
