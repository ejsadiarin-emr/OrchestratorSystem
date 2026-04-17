using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Contracts.Api.Workloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/workloads")]
public sealed class WorkloadsController : ControllerBase
{
    private readonly InstallerDbContext _db;

    public WorkloadsController(InstallerDbContext db)
    {
        _db = db;
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

        return CreatedAtAction(nameof(GetById), new { workloadId = workload.WorkloadId }, MapWorkloadDetail(workload, Array.Empty<WorkloadRevisionEntity>()));
    }

    [HttpPost("{workloadId:guid}/revisions")]
    public async Task<ActionResult<WorkloadRevisionDto>> CreateRevision(Guid workloadId, [FromBody] CreateWorkloadRevisionRequest request)
    {
        var errors = new List<ValidationFieldError>();
        if (!ModelState.IsValid)
        {
            errors.AddRange(ToValidationErrorResponse(ModelState).Errors);
        }

        if (request.Packages.Count < 2 || request.Packages.Count > 3)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "packages",
                Error = "PoC workload revisions must include 2-3 packages"
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
        var existingPackageCount = await _db.Packages.CountAsync(p => packageIds.Contains(p.PackageId));
        if (existingPackageCount != packageIds.Count)
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
            Packages = request.Packages
                .OrderBy(p => p.PackageIndex)
                .Select(p => new WorkloadPackageEntity
                {
                    WorkloadPackageId = Guid.NewGuid(),
                    PackageId = p.PackageId,
                    PackageIndex = p.PackageIndex
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
            Packages = revision.Packages
                .OrderBy(p => p.PackageIndex)
                .Select(p => new WorkloadPackageDto
                {
                    PackageId = p.PackageId,
                    PackageIndex = p.PackageIndex
                })
                .ToList()
        };

        return Created($"/api/workloads/{workloadId}", revisionDto);
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

        var allRevisions = await _db.WorkloadRevisions
            .Where(r => r.WorkloadId == workloadId)
            .ToListAsync();
        foreach (var item in allRevisions)
        {
            item.IsPublished = item.RevisionId == revision.RevisionId;
        }

        workload.PublishedRevisionId = revision.RevisionId;
        workload.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var revisions = await _db.WorkloadRevisions
            .Where(r => r.WorkloadId == workloadId)
            .Include(r => r.Packages)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        return Ok(MapWorkloadDetail(workload, revisions));
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
                UpdatedAtUtc = w.UpdatedAtUtc
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

        return Ok(MapWorkloadDetail(workload, revisions));
    }

    private static WorkloadDetailResponse MapWorkloadDetail(WorkloadDefinitionEntity workload, IEnumerable<WorkloadRevisionEntity> revisions)
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
                    Packages = r.Packages
                        .OrderBy(p => p.PackageIndex)
                        .Select(p => new WorkloadPackageDto
                        {
                            PackageId = p.PackageId,
                            PackageIndex = p.PackageIndex
                        })
                        .ToList()
                })
                .ToList()
        };
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
}
