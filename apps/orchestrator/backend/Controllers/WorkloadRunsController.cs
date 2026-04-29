using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Contracts.Api.WorkloadRuns;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/workload-runs")]
public sealed class WorkloadRunsController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly PolicyEvaluationService _policyEvaluation;
    private readonly WorkloadRunDispatcher _dispatcher;
    private readonly ArtifactStoreService _artifactStore;
    private readonly ILogger<WorkloadRunsController> _logger;

    public WorkloadRunsController(InstallerDbContext db, PolicyEvaluationService policyEvaluation, WorkloadRunDispatcher dispatcher, ArtifactStoreService artifactStore, ILogger<WorkloadRunsController> logger)
    {
        _db = db;
        _policyEvaluation = policyEvaluation;
        _dispatcher = dispatcher;
        _artifactStore = artifactStore;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CreateWorkloadRunResponse>> Create([FromBody] CreateWorkloadRunRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ToValidationErrorResponse(ModelState));
        }

        var errors = new List<ValidationFieldError>();
        if (!TryNormalizeMode(request.Mode, out var mode))
        {
            errors.Add(new ValidationFieldError
            {
                Field = "mode",
                Error = "Mode must be one of: install, update, rollback"
            });
        }

        if (request.NodeIds.Any(n => n == Guid.Empty))
        {
            errors.Add(new ValidationFieldError
            {
                Field = "nodeIds",
                Error = "NodeIds must be non-empty GUIDs"
            });
        }

        var workload = await _db.WorkloadDefinitions.SingleOrDefaultAsync(w => w.WorkloadId == request.WorkloadId);
        if (workload is null)
        {
            return NotFound(new { message = $"Workload {request.WorkloadId} not found" });
        }

        var revision = await _db.WorkloadRevisions
            .Include(r => r.Packages)
            .SingleOrDefaultAsync(r => r.RevisionId == request.RevisionId && r.WorkloadId == request.WorkloadId);
        if (revision is null)
        {
            return NotFound(new { message = $"Revision {request.RevisionId} not found for workload {request.WorkloadId}" });
        }

        if (!revision.IsPublished)
        {
            return BadRequest(new { message = "Cannot create a run for an unpublished revision" });
        }

        var distinctNodeIds = request.NodeIds.Distinct().OrderBy(x => x).ToList();
        var existingNodeCount = await _db.Nodes.CountAsync(n => distinctNodeIds.Contains(n.NodeId));
        if (existingNodeCount != distinctNodeIds.Count)
        {
            errors.Add(new ValidationFieldError
            {
                Field = "nodeIds",
                Error = "One or more node ids were not found"
            });
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ValidationErrorResponse { Errors = errors });
        }

        var normalizedKey = request.IdempotencyKey.Trim();
        var requestHash = ComputeIdempotencyRequestHash(request.WorkloadId, request.RevisionId, mode, distinctNodeIds, request.ForceInstall);
        var existingByIdempotency = await _db.WorkloadRuns.AsNoTracking()
            .Where(r => r.IdempotencyKey == normalizedKey)
            .OrderBy(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (existingByIdempotency is not null)
        {
            if (!string.Equals(existingByIdempotency.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
            {
                return Conflict(new { message = "IdempotencyKey was already used with a different request payload" });
            }

            return Ok(new CreateWorkloadRunResponse
            {
                RunId = existingByIdempotency.RunId,
                State = existingByIdempotency.State,
                RiskLevel = existingByIdempotency.RiskLevel
            });
        }

        var activeStates = new[] { "Queued", "Running" };
        var hasActiveRun = await _db.WorkloadRuns
            .AsNoTracking()
            .Where(r => r.WorkloadId == request.WorkloadId
                && r.NodeId.HasValue && distinctNodeIds.Contains(r.NodeId.Value)
                && activeStates.Contains(r.State))
            .AnyAsync();

        if (hasActiveRun)
        {
            return Conflict(new { message = "An active run already exists for this workload on one or more nodes" });
        }

        var riskLevel = await _policyEvaluation.EvaluateRunRiskAsync(request.RevisionId, _db, HttpContext.RequestAborted);
        var now = DateTime.UtcNow;
        var runId = Guid.NewGuid();
        var snapshotJson = JsonSerializer.Serialize(revision.Packages
            .OrderBy(p => p.PackageIndex)
            .Select(p => new { p.PackageId, p.PackageIndex }));
        var nodesMap = await _db.Nodes
            .AsNoTracking()
            .Where(n => distinctNodeIds.Contains(n.NodeId))
            .ToDictionaryAsync(n => n.NodeId);

        var created = new List<WorkloadRunEntity>();
        foreach (var nodeId in distinctNodeIds)
        {
            var nodeDisplayName = nodesMap.TryGetValue(nodeId, out var n) ? n.DisplayName : string.Empty;
            created.Add(new WorkloadRunEntity
            {
                WorkloadRunRecordId = Guid.NewGuid(),
                RunId = runId,
                WorkloadId = request.WorkloadId,
                RevisionId = request.RevisionId,
                RevisionSnapshotJson = snapshotJson,
                NodeId = nodeId,
                NodeDisplayName = nodeDisplayName,
                Mode = mode,
                State = "Queued",
                IdempotencyKey = normalizedKey,
                IdempotencyRequestHash = requestHash,
                RiskLevel = riskLevel,
                ForceInstall = request.ForceInstall,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        _db.WorkloadRuns.AddRange(created);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsActiveRunConstraintViolation(ex))
        {
            return Conflict(new { message = "An active run already exists for this workload on one or more nodes" });
        }
        catch (DbUpdateException)
        {
            var existingAfterConflict = await _db.WorkloadRuns.AsNoTracking()
                .Where(r => r.IdempotencyKey == normalizedKey)
                .OrderBy(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync();
            if (existingAfterConflict is not null)
            {
                if (!string.Equals(existingAfterConflict.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
                {
                    return Conflict(new { message = "IdempotencyKey was already used with a different request payload" });
                }

                return Ok(new CreateWorkloadRunResponse
                {
                    RunId = existingAfterConflict.RunId,
                    State = existingAfterConflict.State,
                    RiskLevel = existingAfterConflict.RiskLevel
                });
            }

            throw;
        }

        // Send AssignRun via SignalR to each node's group
        foreach (var runEntity in created)
        {
            await _dispatcher.DispatchAsync(runEntity, HttpContext.RequestAborted);
        }

        return CreatedAtAction(nameof(GetById), new { runId }, new CreateWorkloadRunResponse
        {
            RunId = runId,
            State = "Queued",
            RiskLevel = riskLevel
        });
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkloadRunDetailResponse>>> GetAll([FromQuery] string? status)
    {
        var query = _db.WorkloadRuns.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = query.Where(r => r.State.ToLower() == normalized);
        }

        var runs = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        var runGroups = runs.GroupBy(r => r.RunId).ToList();
        var workloadIds = runGroups.Select(g => g.First().WorkloadId).Distinct().ToList();
        var revisionIds = runGroups.Select(g => g.First().RevisionId).Distinct().ToList();

        var workloads = await _db.WorkloadDefinitions
            .AsNoTracking()
            .Where(w => workloadIds.Contains(w.WorkloadId))
            .ToDictionaryAsync(w => w.WorkloadId);

        var revisions = await _db.WorkloadRevisions
            .AsNoTracking()
            .Where(r => revisionIds.Contains(r.RevisionId))
            .ToDictionaryAsync(r => r.RevisionId);

        var result = runGroups.Select(g =>
        {
            var first = g.First();
            var workload = workloads.GetValueOrDefault(first.WorkloadId);
            var revision = revisions.GetValueOrDefault(first.RevisionId);
            return new WorkloadRunDetailResponse
            {
                RunId = first.RunId,
                WorkloadId = first.WorkloadId,
                RevisionId = first.RevisionId,
                WorkloadVersion = revision?.Version ?? string.Empty,
                Mode = first.Mode,
                State = AggregateState(g.Select(r => r.State)),
                CreatedAtUtc = g.Min(r => r.CreatedAtUtc),
                UpdatedAtUtc = g.Max(r => r.UpdatedAtUtc),
                CompletedAtUtc = g.All(r => r.CompletedAtUtc.HasValue) ? g.Max(r => r.CompletedAtUtc) : null,
                RiskLevel = first.RiskLevel,
                NodeIds = g.Where(r => r.NodeId.HasValue).Select(r => r.NodeId.Value).Distinct().ToList()
            };
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<WorkloadRunDetailResponse>> GetById(Guid runId)
    {
        var runs = await _db.WorkloadRuns
            .AsNoTracking()
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync();

        if (runs.Count == 0)
        {
            return NotFound(new { message = $"Run {runId} not found" });
        }

        var first = runs[0];
        var revision = await _db.WorkloadRevisions
            .AsNoTracking()
            .SingleAsync(r => r.RevisionId == first.RevisionId);

        return Ok(new WorkloadRunDetailResponse
        {
            RunId = first.RunId,
            WorkloadId = first.WorkloadId,
            RevisionId = first.RevisionId,
            WorkloadVersion = revision.Version,
            Mode = first.Mode,
            State = AggregateState(runs.Select(r => r.State)),
            CreatedAtUtc = runs.Min(r => r.CreatedAtUtc),
            UpdatedAtUtc = runs.Max(r => r.UpdatedAtUtc),
            CompletedAtUtc = runs.All(r => r.CompletedAtUtc.HasValue) ? runs.Max(r => r.CompletedAtUtc) : null,
            RiskLevel = first.RiskLevel,
            NodeIds = runs.Where(r => r.NodeId.HasValue).Select(r => r.NodeId.Value).Distinct().ToList()
        });
    }

    [HttpGet("{runId:guid}/steps")]
    public async Task<ActionResult<WorkloadRunStepsResponse>> GetSteps(Guid runId)
    {
        var first = await _db.WorkloadRuns.AsNoTracking().Where(r => r.RunId == runId).OrderBy(r => r.CreatedAtUtc).FirstOrDefaultAsync();
        if (first is null)
        {
            return NotFound(new { message = $"Run {runId} not found" });
        }

        List<RevisionSnapshotEntry> currentPackages;
        if (!string.IsNullOrEmpty(first.RevisionSnapshotJson))
        {
            var snapshot = JsonSerializer.Deserialize<List<RevisionSnapshotEntry>>(first.RevisionSnapshotJson);
            currentPackages = (snapshot ?? new List<RevisionSnapshotEntry>()).OrderBy(p => p.PackageIndex).ToList();
        }
        else
        {
            var packages = await _db.WorkloadPackages
                .AsNoTracking()
                .Where(p => p.RevisionId == first.RevisionId)
                .OrderBy(p => p.PackageIndex)
                .ToListAsync();

            currentPackages = packages.Select(p => new RevisionSnapshotEntry
            {
                PackageId = p.PackageId,
                PackageIndex = p.PackageIndex
            }).ToList();
        }

        List<WorkloadRunStepDto> stepDtos;

        if (first.Mode == "update")
        {
            var previousRevisionId = await _db.WorkloadRevisions
                .AsNoTracking()
                .Where(r => r.WorkloadId == first.WorkloadId && r.RevisionId != first.RevisionId && r.CreatedAtUtc < first.CreatedAtUtc)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => r.RevisionId)
                .FirstOrDefaultAsync();

            List<RevisionSnapshotEntry> previousPackages;
            if (previousRevisionId != Guid.Empty)
            {
                previousPackages = await _db.WorkloadPackages
                    .AsNoTracking()
                    .Where(p => p.RevisionId == previousRevisionId)
                    .OrderBy(p => p.PackageIndex)
                    .Select(p => new RevisionSnapshotEntry
                    {
                        PackageId = p.PackageId,
                        PackageIndex = p.PackageIndex
                    })
                    .ToListAsync();
            }
            else
            {
                previousPackages = new List<RevisionSnapshotEntry>();
            }

            var previousById = previousPackages.ToDictionary(p => p.PackageIndex);
            var steps = new List<WorkloadRunStepDto>();
            var seq = 1;

            foreach (var pkg in currentPackages)
            {
                if (!previousById.TryGetValue(pkg.PackageIndex, out var prev))
                {
                    steps.Add(new WorkloadRunStepDto
                    {
                        PackageId = pkg.PackageId,
                        PackageIndex = pkg.PackageIndex,
                        StepId = "install-or-upgrade",
                        Sequence = seq++,
                        Action = "add"
                    });
                }
                else if (prev.PackageId != pkg.PackageId)
                {
                    steps.Add(new WorkloadRunStepDto
                    {
                        PackageId = pkg.PackageId,
                        PackageIndex = pkg.PackageIndex,
                        StepId = "install-or-upgrade",
                        Sequence = seq++,
                        Action = "change"
                    });
                }
            }

            var currentByIndex = currentPackages.ToDictionary(p => p.PackageIndex);
            foreach (var prev in previousPackages)
            {
                if (!currentByIndex.ContainsKey(prev.PackageIndex))
                {
                    steps.Add(new WorkloadRunStepDto
                    {
                        PackageId = prev.PackageId,
                        PackageIndex = prev.PackageIndex,
                        StepId = "remove",
                        Sequence = seq++,
                        Action = "remove"
                    });
                }
            }

            stepDtos = steps;
        }
        else
        {
            stepDtos = currentPackages.Select((p, idx) => new WorkloadRunStepDto
            {
                PackageId = p.PackageId,
                PackageIndex = p.PackageIndex,
                StepId = "install-or-upgrade",
                Sequence = idx + 1,
                Action = "install"
            }).ToList();
        }

        return Ok(new WorkloadRunStepsResponse
        {
            Steps = stepDtos
        });
    }

    [HttpGet("pending")]
    public async Task<ActionResult<List<PendingWorkloadRunResponse>>> GetPending([FromQuery(Name = "agent_id")] Guid agentId)
    {
        var runs = await _db.WorkloadRuns
            .AsNoTracking()
            .Where(r => r.NodeId == agentId && r.State == "Queued")
            .Include(r => r.Workload)
            .Include(r => r.Revision)
            .ThenInclude(rev => rev.Packages)
            .ToListAsync();

        var packageIds = runs.SelectMany(r => r.Revision.Packages.Select(p => p.PackageId)).Distinct().ToList();
        var packages = await _db.Packages
            .AsNoTracking()
            .Where(p => packageIds.Contains(p.PackageId))
            .ToDictionaryAsync(p => p.PackageId);

        var workloadIds = runs.Select(r => r.WorkloadId).Distinct().ToList();
        var nodeStates = await _db.NodeWorkloadStates
            .AsNoTracking()
            .Where(s => s.NodeId == agentId && workloadIds.Contains(s.WorkloadId))
            .ToDictionaryAsync(s => s.WorkloadId);

        var currentRevisionIds = nodeStates.Values
            .Where(s => s.CurrentRevisionId.HasValue && s.CurrentRevisionId.Value != Guid.Empty)
            .Select(s => s.CurrentRevisionId!.Value)
            .Distinct()
            .ToList();

        var currentWorkloadPackages = await _db.WorkloadPackages
            .AsNoTracking()
            .Where(wp => currentRevisionIds.Contains(wp.RevisionId))
            .ToListAsync();

        var currentPackageIds = currentWorkloadPackages.Select(wp => wp.PackageId).Distinct().ToList();
        var currentPackageEntities = await _db.Packages
            .AsNoTracking()
            .Where(p => currentPackageIds.Contains(p.PackageId))
            .ToDictionaryAsync(p => p.PackageId);

        var responses = runs.Select(r => new PendingWorkloadRunResponse
        {
            RunId = r.RunId,
            WorkloadId = r.WorkloadId,
            WorkloadName = r.Workload?.Name ?? string.Empty,
            Mode = r.Mode,
            Packages = r.Revision.Packages
                .OrderBy(p => p.PackageIndex)
                .Select(p => BuildPendingPackageDto(p, packages.GetValueOrDefault(p.PackageId), r.RunId))
                .ToList(),
            CurrentPackages = nodeStates.TryGetValue(r.WorkloadId, out var state) && state.CurrentRevisionId.HasValue && state.CurrentRevisionId.Value != Guid.Empty
                ? currentWorkloadPackages
                    .Where(wp => wp.RevisionId == state.CurrentRevisionId.Value)
                    .OrderBy(wp => wp.PackageIndex)
                    .Select(wp => BuildPendingPackageDto(wp, currentPackageEntities.GetValueOrDefault(wp.PackageId), r.RunId, isCurrentPackage: true))
                    .ToList()
                : new List<PendingPackageDto>()
        }).ToList();

        // Heartbeat: refresh node last-seen timestamp on every poll
        var node = await _db.Nodes.FindAsync(agentId);
        if (node is not null)
        {
            node.LastSeenUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(responses);
    }

    [HttpPatch("{runId:guid}")]
    public async Task<IActionResult> UpdateStatus(Guid runId, [FromBody] RunStatusUpdateRequest request)
    {
        var now = DateTime.UtcNow;
        var isFinal = request.Status is "Completed" or "Failed" or "Cancelled";

        // Atomic claim: only allow Queued -> Running transition when still Queued
        if (request.Status == "Running")
        {
            var updated = await _db.WorkloadRuns
                .Where(r => r.RunId == runId && r.State == "Queued")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.State, request.Status)
                    .SetProperty(r => r.UpdatedAtUtc, now)
                    .SetProperty(r => r.CompletedAtUtc, r => r.CompletedAtUtc));

            if (updated == 0)
            {
                return Conflict(new { message = "Run already claimed or not found" });
            }

            return NoContent();
        }

        // Final status update: allow from Running or Queued -> Completed/Failed/Cancelled
        var run = await _db.WorkloadRuns.FirstOrDefaultAsync(r => r.RunId == runId);
        if (run == null)
        {
            return NotFound();
        }

        run.State = request.Status;
        run.UpdatedAtUtc = now;
        if (isFinal)
        {
            run.CompletedAtUtc = now;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{runId:guid}/timeline")]
    public async Task<IActionResult> AddTimelineEvent(Guid runId, [FromBody] TimelineEventRequest request, [FromQuery(Name = "agent_id")] Guid agentId)
    {
        var entity = new WorkloadRunTimelineEntity
        {
            RunId = runId,
            NodeId = agentId,
            MessageType = "step",
            Sequence = 0,
            StepName = request.Step,
            Status = request.Status,
            Detail = request.Message,
            AtUtc = DateTime.UtcNow
        };

        _db.WorkloadRunTimelines.Add(entity);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created);
    }

    [HttpPost("{runId:guid}/cancel")]
    public async Task<ActionResult<CancelWorkloadRunResponse>> Cancel(Guid runId, [FromBody] CancelWorkloadRunRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ToValidationErrorResponse(ModelState));
        }

        var rows = await _db.WorkloadRuns.Where(r => r.RunId == runId).ToListAsync();
        if (rows.Count == 0)
        {
            return NotFound(new { message = $"Run {runId} not found" });
        }

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            if (row.State is "Completed" or "Failed" or "Cancelled")
            {
                continue;
            }

            row.State = "Cancelled";
            row.CancelReason = request.Reason.Trim();
            row.CompletedAtUtc = now;
            row.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync();

        return Ok(new CancelWorkloadRunResponse
        {
            RunId = runId,
            State = AggregateState(rows.Select(r => r.State)),
            CancelledAtUtc = now
        });
    }

    private static bool TryNormalizeMode(string? mode, out string normalized)
    {
        normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "install" or "update" or "rollback" => true,
            _ => false
        };
    }

    private static string ComputeIdempotencyRequestHash(Guid workloadId, Guid revisionId, string mode, List<Guid> nodeIds, bool forceInstall)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            workloadId,
            revisionId,
            mode,
            nodeIds,
            forceInstall
        });
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string AggregateState(IEnumerable<string> states)
    {
        var set = states.ToHashSet(StringComparer.Ordinal);
        if (set.Contains("Failed"))
        {
            return "Failed";
        }

        if (set.Contains("Running"))
        {
            return "Running";
        }

        if (set.All(s => s == "Cancelled"))
        {
            return "Cancelled";
        }

        if (set.All(s => s == "Completed"))
        {
            return "Completed";
        }

        return "Queued";
    }

    private PendingPackageDto BuildPendingPackageDto(WorkloadPackageEntity wp, PackageEntity? pkg, Guid runId, bool isCurrentPackage = false)
    {
        _artifactStore.TryGetArtifactFileName(pkg?.Name ?? "", pkg?.Version ?? "", out var fn);

        var installType = string.IsNullOrWhiteSpace(pkg?.InstallType) || pkg.InstallType.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? "exe"
            : pkg.InstallType;

        var installArgs = pkg?.InstallArgs ?? "";
        if (string.IsNullOrWhiteSpace(installArgs))
        {
            installArgs = installType.ToLowerInvariant() switch
            {
                "msi" => "/quiet /norestart",
                "exe" => "/S",
                _ => installArgs
            };
        }

        var expectedExitCodes = new List<int>();
        if (!string.IsNullOrWhiteSpace(pkg?.ExpectedExitCodesJson))
        {
            try { expectedExitCodes = JsonSerializer.Deserialize<List<int>>(pkg.ExpectedExitCodesJson) ?? new List<int> { 0 }; }
            catch { expectedExitCodes = new List<int> { 0 }; }
        }
        if (expectedExitCodes.Count == 0) expectedExitCodes = new List<int> { 0 };

        var timeoutSeconds = pkg?.TimeoutSeconds > 0 ? pkg.TimeoutSeconds : 300;

        DetectionConfig detection;
        try { detection = string.IsNullOrWhiteSpace(pkg?.DetectionConfigJson) ? null : JsonSerializer.Deserialize<DetectionConfig>(pkg.DetectionConfigJson); }
        catch { detection = null; }
        detection ??= new DetectionConfig { Type = "version_manifest", Path = pkg?.Name ?? "", ExpectedVersion = pkg?.Version ?? "" };

        var hasArtifact = pkg is not null && _artifactStore.HasArtifactFile(pkg.Name, pkg.Version);
        if (!hasArtifact && !isCurrentPackage)
        {
            _logger.LogWarning("Pending run {RunId} package {PackageId} ({PackageName}@{PackageVersion}) has no artifact in store. Skipping download URL.",
                runId, wp.PackageId, pkg?.Name ?? "?", pkg?.Version ?? "?");
        }

        var expectedSha256 = (hasArtifact && pkg is not null)
            ? _artifactStore.ComputeSha256(pkg.Name, pkg.Version)
            : null;

        return new PendingPackageDto
        {
            PackageEntityId = wp.PackageId,
            Name = pkg?.Name ?? "",
            Version = pkg?.Version ?? "",
            Filename = fn ?? string.Empty,
            DownloadUrl = hasArtifact ? $"/api/artifacts/{wp.PackageId}/download" : string.Empty,
            ExpectedSha256 = expectedSha256,
            InstallAdapter = new InstallAdapterConfig
            {
                Type = installType,
                Command = pkg?.SourcePath ?? "{artifactPath}",
                Arguments = installArgs,
                UninstallArgs = pkg?.UninstallArgs ?? "",
                ExpectedExitCodes = expectedExitCodes,
                TimeoutSeconds = timeoutSeconds
            },
            Detection = detection
        };
    }

    private sealed class RevisionSnapshotEntry
    {
        public Guid PackageId { get; set; }
        public int PackageIndex { get; set; }
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

    private static bool IsActiveRunConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteEx
            && sqliteEx.SqliteErrorCode == 19
            && sqliteEx.Message.Contains("WorkloadRuns.NodeId, WorkloadRuns.WorkloadId", StringComparison.Ordinal);
    }
}
