using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
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
    public async Task<ActionResult<IEnumerable<InstallJob>>> GetAll([FromQuery] string? status = null)
    {
        var query = _db.Jobs
            .Include(j => j.Steps)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim();
            query = query.Where(j => EF.Functions.Collate(j.State, "NOCASE") == normalizedStatus);
        }

        var jobEntities = await query
            .OrderByDescending(j => j.CreatedAtUtc)
            .ToListAsync();

        var packageNameById = await ResolvePackageNamesAsync(jobEntities);
        var nodeHostnameById = await ResolveNodeHostnamesAsync(jobEntities);

        var jobs = jobEntities.Select(j => MapJob(j, packageNameById, nodeHostnameById)).ToList();

        return Ok(jobs);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InstallJob>> GetById(Guid id)
    {
        var entity = await _db.Jobs
            .Include(j => j.Steps)
            .SingleOrDefaultAsync(j => j.JobId == id);

        if (entity is null)
        {
            return NotFound(new { message = $"Job {id} not found" });
        }

        var packageNameById = await ResolvePackageNamesAsync(entity);
        var nodeHostnameById = await ResolveNodeHostnamesAsync(entity);

        return Ok(MapJob(entity, packageNameById, nodeHostnameById));
    }

    [HttpPost]
    public async Task<ActionResult<InstallJob>> Create([FromBody] CreateJobRequest request)
    {
        if (request.PackageId == Guid.Empty)
        {
            return BadRequest(new { message = "PackageId must be a non-empty GUID" });
        }

        if (request.TargetNodeId == Guid.Empty)
        {
            return BadRequest(new { message = "TargetNodeId must be a non-empty GUID" });
        }

        var package = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == request.PackageId);
        if (package is null)
        {
            return BadRequest(new { message = $"Package {request.PackageId} not found" });
        }

        var node = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == request.TargetNodeId);
        if (node is null)
        {
            return BadRequest(new { message = $"Node {request.TargetNodeId} not found" });
        }

        var jobEntity = new JobEntity
        {
            JobId = Guid.NewGuid(),
            Mode = "install",
            State = "Running",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ManifestPackageId = request.PackageId.ToString(),
            ManifestTargetVersion = string.Empty,
            TargetNodeIdsCsv = node.NodeId.ToString(),
            Steps = new List<JobStepEntity>
            {
                new() { Name = "PreConditionCheck", Status = "Running", Sequence = 1, StepId = "precondition-check", StartedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
                new() { Name = "CopyFiles", Status = "Pending", Sequence = 2, StepId = "copy-files", StartedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow }
            }
        };

        _db.Jobs.Add(jobEntity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created job {JobId} for package {Package} on node {Node}",
            jobEntity.JobId, request.PackageId, node.Hostname);

        var job = MapJob(
            jobEntity,
            new Dictionary<Guid, string> { [package.PackageId] = package.Name },
            new Dictionary<Guid, string> { [node.NodeId] = node.Hostname });

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Cancel(Guid id)
    {
        var job = await _db.Jobs
            .Include(j => j.Steps)
            .SingleOrDefaultAsync(j => j.JobId == id);

        if (job is null)
        {
            return NotFound(new { message = $"Job {id} not found" });
        }

        if (job.State is "Completed" or "Failed")
        {
            return BadRequest(new { message = "Cannot cancel completed or failed job" });
        }

        job.State = "Cancelled";
        job.CompletedAtUtc = DateTime.UtcNow;
        job.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var step in job.Steps.Where(s => s.Status == "Running" || s.Status == "Pending"))
        {
            step.Status = "Cancelled";
            step.CompletedAtUtc = DateTime.UtcNow;
            step.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Cancelled job {JobId}", id);

        var packageNameById = await ResolvePackageNamesAsync(job);
        var nodeHostnameById = await ResolveNodeHostnamesAsync(job);

        return Ok(MapJob(job, packageNameById, nodeHostnameById));
    }

    private static InstallJob MapJob(
        JobEntity jobEntity,
        IReadOnlyDictionary<Guid, string> packageNameById,
        IReadOnlyDictionary<Guid, string> nodeHostnameById)
    {
        var orderedSteps = jobEntity.Steps.OrderBy(s => s.Sequence).ToList();
        var parsedPackageId = ParseGuidOrEmpty(jobEntity.ManifestPackageId);
        var parsedTargetNodeId = GetPrimaryTargetNodeId(jobEntity.TargetNodeIdsCsv);

        var packageName = ResolveDisplayName(packageNameById, parsedPackageId);
        var targetNodeHostname = ResolveDisplayName(nodeHostnameById, parsedTargetNodeId);

        return new InstallJob
        {
            Id = jobEntity.JobId,
            PackageId = parsedPackageId,
            PackageName = packageName,
            TargetNodeId = parsedTargetNodeId,
            TargetNodeHostname = targetNodeHostname,
            Status = jobEntity.State,
            CurrentStep = orderedSteps.Count(s => s.Status == "Completed") + (orderedSteps.Any(s => s.Status == "Running") ? 1 : 0),
            TotalSteps = orderedSteps.Count,
            StartedAt = jobEntity.CreatedAtUtc,
            CompletedAt = jobEntity.CompletedAtUtc,
            Steps = orderedSteps.Select(s => new JobStep
            {
                Name = s.Name,
                Status = s.Status,
                Duration = s.CompletedAtUtc.HasValue ? $"{(s.CompletedAtUtc.Value - s.StartedAtUtc).TotalSeconds:0.0}s" : null
            }).ToList()
        };
    }

    private async Task<Dictionary<Guid, string>> ResolvePackageNamesAsync(IEnumerable<JobEntity> jobs)
    {
        var packageIds = jobs
            .Select(j => ParseGuidOrEmpty(j.ManifestPackageId))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        return await ResolvePackageNamesByIdsAsync(packageIds);
    }

    private Task<Dictionary<Guid, string>> ResolvePackageNamesAsync(JobEntity job)
    {
        var packageId = ParseGuidOrEmpty(job.ManifestPackageId);
        return ResolvePackageNamesByIdsAsync(packageId == Guid.Empty ? [] : [packageId]);
    }

    private async Task<Dictionary<Guid, string>> ResolveNodeHostnamesAsync(IEnumerable<JobEntity> jobs)
    {
        var nodeIds = jobs
            .Select(j => GetPrimaryTargetNodeId(j.TargetNodeIdsCsv))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        return await ResolveNodeHostnamesByIdsAsync(nodeIds);
    }

    private Task<Dictionary<Guid, string>> ResolveNodeHostnamesAsync(JobEntity job)
    {
        var nodeId = GetPrimaryTargetNodeId(job.TargetNodeIdsCsv);
        return ResolveNodeHostnamesByIdsAsync(nodeId == Guid.Empty ? [] : [nodeId]);
    }

    private async Task<Dictionary<Guid, string>> ResolvePackageNamesByIdsAsync(List<Guid> packageIds)
    {
        if (packageIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _db.Packages
            .Where(p => packageIds.Contains(p.PackageId))
            .Select(p => new { Id = p.PackageId, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name);
    }

    private async Task<Dictionary<Guid, string>> ResolveNodeHostnamesByIdsAsync(List<Guid> nodeIds)
    {
        if (nodeIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _db.Nodes
            .Where(n => nodeIds.Contains(n.NodeId))
            .Select(n => new { Id = n.NodeId, n.Hostname })
            .ToDictionaryAsync(n => n.Id, n => n.Hostname);
    }

    private static Guid ParseGuidOrEmpty(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;
    }

    private static Guid GetPrimaryTargetNodeId(string? targetNodeIdsCsv)
    {
        var firstTargetNodeId = targetNodeIdsCsv?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return ParseGuidOrEmpty(firstTargetNodeId);
    }

    private static string ResolveDisplayName(IReadOnlyDictionary<Guid, string> map, Guid id)
    {
        return map.TryGetValue(id, out var value) ? value : string.Empty;
    }
}
