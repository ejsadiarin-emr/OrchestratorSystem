using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq.Expressions;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly ILogger<JobsController> _logger;

    public JobsController(InstallerDbContext db, ILogger<JobsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JobDetailResponse>>> GetAll([FromQuery] string? state = null)
    {
        IQueryable<JobEntity> query = _db.Jobs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(state))
        {
            var normalizedState = state.Trim();
            query = query.Where(j => EF.Functions.Collate(j.State, "NOCASE") == normalizedState);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedAtUtc)
            .Select(MapJobDetailResponseExpression)
            .ToListAsync();

        return Ok(jobs);
    }

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobDetailResponse>> GetById(Guid jobId)
    {
        var entity = await _db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.JobId == jobId);

        if (entity is null)
        {
            return NotFound(new { message = $"Job {jobId} not found" });
        }

        return Ok(MapJobDetailResponse(entity));
    }

    [HttpPost]
    public async Task<ActionResult<CreateJobResponse>> Create([FromBody] CreateJobRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (!TryNormalizeMode(request.ExecutionMode, out var normalizedMode))
        {
            return BadRequest(new { message = "ExecutionMode must be one of: install, upgrade, rollback, modify, cancel" });
        }

        var normalizedPackageId = request.PackageId.Trim();
        var normalizedTargetVersion = request.TargetVersion.Trim();
        var normalizedIdempotencyKey = request.IdempotencyKey.Trim();
        var targetIds = request.Targets.Distinct().OrderBy(x => x).ToList();

        var packageIdParsed = Guid.TryParse(normalizedPackageId, out var parsedPackageId);
        var packageExists = packageIdParsed
            ? await _db.Packages.AnyAsync(p =>
                p.PackageId == parsedPackageId ||
                EF.Functions.Collate(p.Name, "NOCASE") == normalizedPackageId)
            : await _db.Packages.AnyAsync(p => EF.Functions.Collate(p.Name, "NOCASE") == normalizedPackageId);
        if (!packageExists)
        {
            return BadRequest(new { message = $"Package '{normalizedPackageId}' was not found" });
        }

        var existingTargetCount = await _db.Nodes.CountAsync(n => targetIds.Contains(n.NodeId));
        if (existingTargetCount != targetIds.Count)
        {
            return BadRequest(new { message = "One or more target nodes were not found" });
        }

        var requestHash = ComputeIdempotencyRequestHash(
            normalizedPackageId,
            normalizedTargetVersion,
            normalizedMode,
            targetIds);

        var existingByIdempotencyKey = await _db.Jobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.IdempotencyKey == normalizedIdempotencyKey);
        if (existingByIdempotencyKey is not null)
        {
            if (!string.Equals(existingByIdempotencyKey.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
            {
                return Conflict(new { message = "IdempotencyKey was already used with a different request payload" });
            }

            return Ok(new CreateJobResponse
            {
                JobId = existingByIdempotencyKey.JobId,
                State = existingByIdempotencyKey.State
            });
        }

        var now = DateTime.UtcNow;

        var jobEntity = new JobEntity
        {
            JobId = Guid.NewGuid(),
            Mode = normalizedMode,
            State = "Queued",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ManifestPackageId = normalizedPackageId,
            ManifestTargetVersion = normalizedTargetVersion,
            TargetNodeIdsCsv = string.Join(',', targetIds),
            IdempotencyKey = normalizedIdempotencyKey,
            IdempotencyRequestHash = requestHash,
            Steps = new List<JobStepEntity>
            {
                new()
                {
                    Name = "PreConditionCheck",
                    StepId = "precondition-check",
                    Status = "Pending",
                    Sequence = 1,
                    StartedAtUtc = now,
                    UpdatedAtUtc = now
                },
                new()
                {
                    Name = "CopyFiles",
                    StepId = "copy-files",
                    Status = "Pending",
                    Sequence = 2,
                    StartedAtUtc = now,
                    UpdatedAtUtc = now
                }
            }
        };

        _db.Jobs.Add(jobEntity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            var existingAfterConflict = await _db.Jobs.AsNoTracking()
                .SingleOrDefaultAsync(j => j.IdempotencyKey == normalizedIdempotencyKey);
            if (existingAfterConflict is not null)
            {
                if (!string.Equals(existingAfterConflict.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
                {
                    return Conflict(new { message = "IdempotencyKey was already used with a different request payload" });
                }

                return Ok(new CreateJobResponse
                {
                    JobId = existingAfterConflict.JobId,
                    State = existingAfterConflict.State
                });
            }

            throw;
        }

        _logger.LogInformation(
            "Created job {JobId} for package {PackageId} on {TargetCount} target nodes",
            jobEntity.JobId,
            normalizedPackageId,
            targetIds.Count);

        var response = new CreateJobResponse
        {
            JobId = jobEntity.JobId,
            State = jobEntity.State
        };

        return CreatedAtAction(nameof(GetById), new { jobId = jobEntity.JobId }, response);
    }

    [HttpGet("{jobId:guid}/steps")]
    public async Task<ActionResult<JobStepListResponse>> GetSteps(Guid jobId)
    {
        var exists = await _db.Jobs.AnyAsync(j => j.JobId == jobId);
        if (!exists)
        {
            return NotFound(new { message = $"Job {jobId} not found" });
        }

        var steps = await _db.JobSteps.AsNoTracking()
            .Where(s => s.JobId == jobId)
            .OrderBy(s => s.Sequence)
            .Select(s => new JobStepDto
            {
                StepId = s.StepId,
                Name = s.Name,
                Status = s.Status,
                Sequence = s.Sequence,
                ReasonCode = s.ReasonCode,
                TelemetryRef = s.TelemetryRef
            })
            .ToListAsync();

        return Ok(new JobStepListResponse { Steps = steps });
    }

    [HttpPost("{jobId:guid}/cancel")]
    public async Task<ActionResult<CancelJobResponse>> Cancel(Guid jobId, [FromBody] CancelJobRequest request)
    {
        var job = await _db.Jobs
            .Include(j => j.Steps)
            .SingleOrDefaultAsync(j => j.JobId == jobId);

        if (job is null)
        {
            return NotFound(new { message = $"Job {jobId} not found" });
        }

        if (job.State is "Completed" or "Failed" or "Cancelled")
        {
            return Ok(new CancelJobResponse
            {
                JobId = job.JobId,
                State = job.State,
                CancelledAtUtc = job.CompletedAtUtc ?? job.UpdatedAtUtc
            });
        }

        var now = DateTime.UtcNow;
        job.State = "Cancelled";
        job.CompletedAtUtc = now;
        job.UpdatedAtUtc = now;
        job.CancelReason = request.Reason.Trim();

        foreach (var step in job.Steps.Where(s => s.Status == "Running" || s.Status == "Pending"))
        {
            step.Status = "Cancelled";
            step.CompletedAtUtc = now;
            step.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Cancelled job {JobId}", jobId);

        return Ok(new CancelJobResponse
        {
            JobId = job.JobId,
            State = job.State,
            CancelledAtUtc = now
        });
    }

    private static readonly Expression<Func<JobEntity, JobDetailResponse>> MapJobDetailResponseExpression = job => new()
    {
        JobId = job.JobId,
        State = job.State,
        Mode = job.Mode,
        ReasonCode = job.ReasonCode,
        CreatedAtUtc = job.CreatedAtUtc,
        UpdatedAtUtc = job.UpdatedAtUtc,
        CompletedAtUtc = job.CompletedAtUtc
    };

    private static JobDetailResponse MapJobDetailResponse(JobEntity entity)
    {
        return new JobDetailResponse
        {
            JobId = entity.JobId,
            State = entity.State,
            Mode = entity.Mode,
            ReasonCode = entity.ReasonCode,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            CompletedAtUtc = entity.CompletedAtUtc
        };
    }

    private static bool TryNormalizeMode(string? mode, out string normalized)
    {
        normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "install" or "upgrade" or "rollback" or "modify" or "cancel" => true,
            _ => false
        };
    }

    private static string ComputeIdempotencyRequestHash(
        string packageId,
        string targetVersion,
        string executionMode,
        List<Guid> targetIds)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            packageId,
            targetVersion,
            executionMode,
            targets = targetIds
        });

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
