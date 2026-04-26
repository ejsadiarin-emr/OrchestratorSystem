using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Contracts.Api.WorkloadRuns;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Hubs;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/workload-runs")]
public sealed class WorkloadRunsController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly PolicyEvaluationService _policyEvaluation;
    private readonly IHubContext<AgentRuntimeHub> _hubContext;

    public WorkloadRunsController(InstallerDbContext db, PolicyEvaluationService policyEvaluation, IHubContext<AgentRuntimeHub> hubContext)
    {
        _db = db;
        _policyEvaluation = policyEvaluation;
        _hubContext = hubContext;
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
        var requestHash = ComputeIdempotencyRequestHash(request.WorkloadId, request.RevisionId, mode, distinctNodeIds);
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

        var currentPackagesByNode = new Dictionary<Guid, List<PackageAssignment>>();
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
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            var nodeState = await _db.NodeWorkloadStates
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.NodeId == nodeId && s.WorkloadId == request.WorkloadId, HttpContext.RequestAborted);

            if (nodeState?.CurrentRevisionId is not null && nodeState.CurrentRevisionId != Guid.Empty)
            {
                var currentRevisionPackages = await _db.WorkloadPackages
                    .AsNoTracking()
                    .Where(wp => wp.RevisionId == nodeState.CurrentRevisionId)
                    .OrderBy(wp => wp.PackageIndex)
                    .ToListAsync(HttpContext.RequestAborted);

                var currentPackageIds = currentRevisionPackages.Select(wp => wp.PackageId).ToList();
                var currentPackageEntities = await _db.Packages
                    .AsNoTracking()
                    .Where(p => currentPackageIds.Contains(p.PackageId))
                    .ToListAsync(HttpContext.RequestAborted);

                currentPackagesByNode[nodeId] = BuildPackageAssignments(currentRevisionPackages, currentPackageEntities);
            }
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
        var workloadName = workload.Name;
        var revisionVersion = revision.Version;
        var packageEntities = await _db.Packages
            .AsNoTracking()
            .Where(p => revision.Packages.Select(wp => wp.PackageId).Contains(p.PackageId))
            .ToListAsync();

        var packageAssignments = BuildPackageAssignments(revision.Packages.ToList(), packageEntities);

        foreach (var runEntity in created)
        {
            var currentPackages = currentPackagesByNode.TryGetValue(runEntity.NodeId.GetValueOrDefault(), out var cp)
                ? cp
                : new List<PackageAssignment>();
            var payload = new AssignRunPayload
            {
                RunId = runEntity.RunId,
                WorkloadId = runEntity.WorkloadId,
                WorkloadName = workloadName,
                RevisionId = runEntity.RevisionId,
                RevisionVersion = revisionVersion,
                Mode = runEntity.Mode,
                NodeId = runEntity.NodeId.GetValueOrDefault(),
                Packages = packageAssignments,
                CurrentPackages = currentPackages,
                PreUpgradeActions = new List<string>()
            };

            var envelope = new MessageEnvelope
            {
                MessageType = MessageTypes.AssignRun,
                RunId = runEntity.RunId.ToString(),
                AgentId = runEntity.NodeId.ToString(),
                Sequence = 0,
                Payload = payload
            };

            await _hubContext.Clients.Group($"node-{runEntity.NodeId}")
                .SendAsync("AssignRun", envelope, HttpContext.RequestAborted);
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

    private static List<PackageAssignment> BuildPackageAssignments(List<WorkloadPackageEntity> workloadPackages, List<PackageEntity> packageEntities)
    {
        return workloadPackages
            .OrderBy(p => p.PackageIndex)
            .Select(wp =>
            {
                const string artifactPath = "{artifactPath}";
                var pkg = packageEntities.FirstOrDefault(p => p.PackageId == wp.PackageId);
                var installType = pkg?.InstallType ?? "exe";
                var isMsi = string.Equals(installType, "msi", StringComparison.OrdinalIgnoreCase);
                string command;
                string arguments;
                if (isMsi)
                {
                    command = "msiexec.exe";
                    arguments = $"/i \"{artifactPath}\" {pkg?.InstallArgs ?? ""}";
                }
                else
                {
                    command = artifactPath;
                    arguments = pkg?.InstallArgs ?? "";
                }

                var expectedExitCodes = new List<int>();
                if (!string.IsNullOrWhiteSpace(pkg?.ExpectedExitCodesJson))
                {
                    try
                    {
                        expectedExitCodes = JsonSerializer.Deserialize<List<int>>(pkg.ExpectedExitCodesJson) ?? new List<int>();
                    }
                    catch (JsonException)
                    {
                        expectedExitCodes = new List<int>();
                    }
                }

                if (expectedExitCodes.Count == 0)
                {
                    expectedExitCodes = isMsi ? new List<int> { 0, 3010 } : new List<int> { 0 };
                }

                var timeoutSeconds = pkg?.TimeoutSeconds > 0 ? pkg.TimeoutSeconds : 300;
                return new PackageAssignment
                {
                    PackageIndex = wp.PackageIndex,
                    PackageId = wp.PackageId.ToString(),
                    Name = pkg?.Name ?? "",
                    Version = pkg?.Version ?? "",
                    Channel = "stable",
                    InstallAdapter = new InstallAdapterConfig
                    {
                        Type = installType,
                        Command = command,
                        Arguments = arguments,
                        UninstallArgs = pkg?.UninstallArgs ?? "",
                        ExpectedExitCodes = expectedExitCodes,
                        TimeoutSeconds = timeoutSeconds
                    },
                    Detection = new DetectionConfig
                    {
                        Type = "file",
                        Path = pkg?.Name ?? "",
                        ExpectedVersion = pkg?.Version ?? ""
                    }
                };
            })
            .ToList();
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

    private static string ComputeIdempotencyRequestHash(Guid workloadId, Guid revisionId, string mode, List<Guid> nodeIds)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            workloadId,
            revisionId,
            mode,
            nodeIds
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
