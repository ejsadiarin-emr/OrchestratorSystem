using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        _logger.LogWarning("Deprecated endpoint called: POST /api/jobs");
        return StatusCode(StatusCodes.Status410Gone, new DeprecatedEndpointResponse());
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
        _logger.LogWarning("Deprecated endpoint called: POST /api/jobs/{JobId}/cancel", jobId);
        return StatusCode(StatusCodes.Status410Gone, new DeprecatedEndpointResponse());
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

}
