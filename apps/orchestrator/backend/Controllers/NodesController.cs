using System.Linq;
using System.Text;
using System.Text.Json;
using DeploymentPoC.Contracts.Runtime.Probes;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/nodes")]
public class NodesController : ControllerBase
{
    private readonly InstallerDbContext _db;
    private readonly ILogger<NodesController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public NodesController(InstallerDbContext db, ILogger<NodesController> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<List<Node>>> GetAll()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var nodes = await _db.Nodes
            .OrderBy(n => n.Hostname)
            .Select(n => new Node
            {
                Id = n.NodeId,
                Hostname = n.Hostname,
                DisplayName = n.DisplayName,
                IpAddress = n.IpAddress,
                Status = n.LastSeenUtc >= cutoff ? "online" : "offline",
                LastSeenAt = n.LastSeenUtc,
                FirstConnectedAt = n.FirstConnectedUtc,
                Description = n.Description,
                OsVersion = n.OsVersion,
                AgentVersion = n.AgentVersion,
            })
            .ToListAsync();

        return Ok(nodes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Node>> GetById(Guid id)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        return Ok(new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            IpAddress = entity.IpAddress,
            Description = entity.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc
        });
    }

    [HttpPost]
    public async Task<ActionResult<Node>> Create([FromBody] CreateNodeRequest request)
    {
        var entity = new NodeEntity
        {
            NodeId = Guid.NewGuid(),
            Hostname = request.Hostname,
            IpAddress = request.IpAddress,
            Description = request.Description,
            LastSeenUtc = DateTime.UtcNow
        };

        _db.Nodes.Add(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateHostnameConstraintViolation(ex))
        {
            return Conflict(new { message = $"A node with hostname '{request.Hostname}' already exists" });
        }

        var node = new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            IpAddress = request.IpAddress,
            Description = request.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc
        };

        _logger.LogInformation("Registered node {Hostname} ({IpAddress})", node.Hostname, node.IpAddress);
        
        return CreatedAtAction(nameof(GetById), new { id = node.Id }, node);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Node>> Update(Guid id, [FromBody] UpdateNodeRequest request)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        entity.Hostname = request.Hostname;
        entity.DisplayName = request.DisplayName;
        entity.IpAddress = request.IpAddress;
        entity.Description = request.Description;
        entity.LastSeenUtc = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateHostnameConstraintViolation(ex))
        {
            return Conflict(new { message = $"A node with hostname '{request.Hostname}' already exists" });
        }

        var node = new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            IpAddress = request.IpAddress,
            Description = request.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc
        };

        _logger.LogInformation("Updated node {Hostname}", node.Hostname);
        
        return Ok(node);
    }

    [HttpPatch("{id:guid}/display-name")]
    public async Task<ActionResult<Node>> UpdateDisplayName(Guid id, [FromBody] UpdateNodeDisplayNameRequest request)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        entity.DisplayName = request.DisplayName;
        await _db.SaveChangesAsync();

        return Ok(new Node
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            DisplayName = entity.DisplayName,
            IpAddress = entity.IpAddress,
            Description = entity.Description,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc,
            FirstConnectedAt = entity.FirstConnectedUtc,
            OsVersion = entity.OsVersion,
            AgentVersion = entity.AgentVersion,
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var entity = await _db.Nodes.SingleOrDefaultAsync(n => n.NodeId == id);
        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        _db.Nodes.Remove(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted node {Id}", id);
        return NoContent();
    }

    [HttpGet("workload-states")]
    public async Task<ActionResult<List<NodeWorkloadStateResponse>>> GetWorkloadStates()
    {
        var states = await _db.NodeWorkloadStates
            .AsNoTracking()
            .Include(s => s.CurrentRevision)
            .Include(s => s.Workload)
            .Select(s => new NodeWorkloadStateResponse
            {
                NodeId = s.NodeId,
                WorkloadId = s.WorkloadId,
                WorkloadRevision = s.CurrentRevision != null ? s.CurrentRevision.Version : "",
                CurrentRevisionId = s.CurrentRevisionId,
                RunId = Guid.Empty,
                Status = s.Status,
                UpdatedAt = s.UpdatedAtUtc.ToString("O")
            })
            .ToListAsync();

        return Ok(states);
    }

    [HttpGet("{id:guid}/details")]
    public async Task<ActionResult<NodeDetailResponse>> GetDetails(Guid id)
    {
        var entity = await _db.Nodes
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.Workload)
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.CurrentRevision)
            .SingleOrDefaultAsync(n => n.NodeId == id);

        if (entity is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        var workloads = entity.NodeWorkloadStates.Select(s => new NodeWorkloadAssignment
        {
            WorkloadId = s.WorkloadId,
            Name = s.Workload.Name,
            Status = s.Status,
            CurrentVersion = s.CurrentRevision?.Version ?? ""
        }).ToList();

        var preCheck = BuildReadOnlyPreCheckSummary(entity);

        return Ok(new NodeDetailResponse
        {
            Id = entity.NodeId,
            Hostname = entity.Hostname,
            DisplayName = entity.DisplayName,
            IpAddress = entity.IpAddress,
            Status = entity.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2) ? "online" : "offline",
            LastSeenAt = entity.LastSeenUtc,
            FirstConnectedAt = entity.FirstConnectedUtc,
            Description = entity.Description,
            OsVersion = entity.OsVersion,
            AgentVersion = entity.AgentVersion,
            Workloads = workloads,
            LatestPreCheck = preCheck
        });
    }

    [HttpPost("prechecks")]
    public async Task<ActionResult<List<NodePreCheckResponse>>> RunPreChecks([FromBody] RunPreCheckRequest request)
    {
        if (request.NodeIds is null || request.NodeIds.Count == 0)
        {
            return BadRequest(new { message = "At least one node ID is required" });
        }

        var nodes = await _db.Nodes
            .Where(n => request.NodeIds.Contains(n.NodeId))
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.CurrentRevision)
            .ToListAsync();

        var sharedConfigs = request.WorkloadId.HasValue
            ? await LoadDetectionConfigsByWorkloadAsync(request.WorkloadId)
            : null;

        var results = new List<NodePreCheckResponse>();

        foreach (var node in nodes)
        {
            var nodeConfigs = sharedConfigs ?? await LoadDetectionConfigsByWorkloadAsync(null, node);

            var nodeResult = new NodePreCheckResponse
            {
                NodeId = node.NodeId,
                Hostname = node.Hostname
            };

            var (agentResponse, error) = await ProbeNodeAsync(node, nodeConfigs);

            if (error is not null || agentResponse is null)
            {
                nodeResult.Error = error;
                results.Add(nodeResult);
                continue;
            }

            nodeResult.Summary = await ReconcileProbeResults(node, agentResponse, nodeConfigs);

            results.Add(nodeResult);
        }

        await _db.SaveChangesAsync();
        return Ok(results);
    }

    [HttpPost("prechecks/summary")]
    public async Task<ActionResult<PreCheckSummaryResponse>> RunPreCheckSummary([FromBody] RunPreCheckSummaryRequest request)
    {
        if (request.NodeIds is null || request.NodeIds.Count == 0)
        {
            return BadRequest(new { message = "At least one node ID is required" });
        }

        var nodes = await _db.Nodes
            .Where(n => request.NodeIds.Contains(n.NodeId))
            .ToListAsync();

        var revisionConfigs = await LoadDetectionConfigsForRevisionAsync(request.RevisionId);
        var workloadDetectionConfigs = new Dictionary<Guid, List<DetectionConfigDto>>
        {
            [request.WorkloadId] = revisionConfigs
        };

        var nodeStates = await _db.NodeWorkloadStates
            .Where(s => s.WorkloadId == request.WorkloadId && request.NodeIds.Contains(s.NodeId))
            .ToDictionaryAsync(s => s.NodeId);

        var allPublishedRevisions = await _db.WorkloadRevisions
            .AsNoTracking()
            .Where(r => r.WorkloadId == request.WorkloadId && r.IsPublished)
            .OrderBy(r => r.Version)
            .ToListAsync();

        var allRevisionVersions = allPublishedRevisions.Select(r => r.Version).ToList();

        var response = new PreCheckSummaryResponse
        {
            Nodes = new List<PreCheckSummaryNode>()
        };

        foreach (var node in nodes)
        {
            var summaryNode = new PreCheckSummaryNode
            {
                NodeId = node.NodeId,
                Hostname = node.Hostname
            };

            if (nodeStates.TryGetValue(node.NodeId, out var state))
            {
                summaryNode.WorkloadStatus = state.Status switch
                {
                    "Current" => "Current",
                    "Drifted" => "Drifted",
                    _ => "Unknown"
                };
            }
            else
            {
                summaryNode.WorkloadStatus = "Absent";
            }

            var (agentResponse, error) = await ProbeNodeAsync(node, workloadDetectionConfigs);

            if (error is not null || agentResponse is null)
            {
                _logger.LogWarning("Pre-check probe failed for node {NodeId}: {Error}", node.NodeId, error ?? "Probe failed");
                summaryNode.Action = "Unknown";
                summaryNode.ActionDetail = error ?? "Probe failed";
                response.Nodes.Add(summaryNode);
                continue;
            }

            var resultMap = agentResponse.Results.ToDictionary(r => r.PackageId);

            summaryNode.Packages = revisionConfigs.Select(cfg =>
            {
                if (resultMap.TryGetValue(cfg.PackageId, out var result))
                {
                    return new PreCheckSummaryPackage
                    {
                        PackageId = cfg.PackageId,
                        Name = cfg.Name,
                        Status = result.Status.ToString(),
                        Comparison = ComputeComparison(result.ActualVersion, cfg.Version),
                        ActualVersion = result.ActualVersion,
                        ExpectedVersion = cfg.Version
                    };
                }
                return new PreCheckSummaryPackage
                {
                    PackageId = cfg.PackageId,
                    Name = cfg.Name,
                    Status = "NotPresent",
                    Comparison = null,
                    ExpectedVersion = cfg.Version
                };
            }).ToList();

            // Reconcile DB status from live probe results so Re-run Pre-Checks actually fixes stale data
            var allMatch = revisionConfigs.All(cfg =>
                resultMap.TryGetValue(cfg.PackageId, out var r) &&
                r.Status == PreCheckStatus.AlreadySatisfied &&
                VersionComparisonService.Matches(cfg.Version, r.ActualVersion));
            var hasAnyDetected = revisionConfigs.Any(cfg =>
                resultMap.TryGetValue(cfg.PackageId, out var r) &&
                r.Status != PreCheckStatus.NotPresent);

            if (nodeStates.TryGetValue(node.NodeId, out var trackedState))
            {
                trackedState.Status = allMatch ? "Current" : "Drifted";
                trackedState.PackageStatesJson = BuildPackageStatesJson(revisionConfigs, resultMap);
                trackedState.UpdatedAtUtc = DateTime.UtcNow;
                trackedState.LastProbedAtUtc = DateTime.UtcNow;
                summaryNode.WorkloadStatus = trackedState.Status;
            }
            else if (hasAnyDetected)
            {
                var newState = new NodeWorkloadStateEntity
                {
                    NodeWorkloadStateId = Guid.NewGuid(),
                    NodeId = node.NodeId,
                    WorkloadId = request.WorkloadId,
                    CurrentRevisionId = null,
                    PackageStatesJson = BuildPackageStatesJson(revisionConfigs, resultMap),
                    Status = allMatch ? "Current" : "Drifted",
                    UpdatedAtUtc = DateTime.UtcNow,
                    LastProbedAtUtc = DateTime.UtcNow
                };
                _db.NodeWorkloadStates.Add(newState);
                nodeStates[node.NodeId] = newState;
                summaryNode.WorkloadStatus = newState.Status;
            }

            var packageStatuses = summaryNode.Packages.Select(p => p.Status).ToList();
            var hasWrongVersionOlder = summaryNode.Packages.Any(p =>
                p.Status == "WrongVersion" && p.Comparison == "older");
            var hasWrongVersionNewer = summaryNode.Packages.Any(p =>
                p.Status == "WrongVersion" && p.Comparison == "newer");
            var hasNotPresent = packageStatuses.Contains("NotPresent");
            var allAlreadySatisfied = packageStatuses.All(s => s == "AlreadySatisfied");

            var effectiveWorkloadStatus = summaryNode.WorkloadStatus;
            if (effectiveWorkloadStatus == "Unknown")
            {
                effectiveWorkloadStatus = hasAnyDetected ? "Drifted" : "Absent";
            }

            var isVersionJump = false;
            if (nodeStates.TryGetValue(node.NodeId, out var nodeState) && nodeState.CurrentRevisionId != null)
            {
                var currentRevision = allPublishedRevisions.FirstOrDefault(r => r.RevisionId == nodeState.CurrentRevisionId);
                if (currentRevision != null)
                {
                    var targetRevision = allPublishedRevisions.FirstOrDefault(r => r.RevisionId == request.RevisionId);
                    if (targetRevision != null)
                    {
                        isVersionJump = !VersionComparisonService.IsSequentialRevision(currentRevision.Version, targetRevision.Version, allRevisionVersions);
                    }
                }
            }

            summaryNode.Action = (hasWrongVersionNewer, isVersionJump, effectiveWorkloadStatus, allAlreadySatisfied, hasWrongVersionOlder, hasNotPresent) switch
            {
                (true, _, _, _, _, _) => "BlockedDowngrade",
                (_, true, _, _, _, _) => "BlockedVersionJump",
                (_, _, "Absent", _, _, _) => "FreshInstall",
                (_, _, "Current", true, _, _) => "Skip",
                (_, _, "Drifted", true, _, _) => "Update",
                (_, _, _, _, true, _) => "Update",
                (_, _, _, _, _, true) => "InstallMissing",
                _ => "Unknown"
            };

            if (summaryNode.Action == "Unknown")
            {
                summaryNode.ActionDetail = "Unable to determine action from probe results";
            }

            response.Nodes.Add(summaryNode);
        }

        await _db.SaveChangesAsync();

        return Ok(response);
    }

    [HttpPost("{id:guid}/prechecks")]
    public async Task<ActionResult<NodePreCheckSummary>> RunSinglePreCheck(Guid id, [FromQuery] Guid? workloadId)
    {
        var node = await _db.Nodes
            .Include(n => n.NodeWorkloadStates)
            .ThenInclude(s => s.CurrentRevision)
            .SingleOrDefaultAsync(n => n.NodeId == id);

        if (node is null)
        {
            return NotFound(new { message = $"Node {id} not found" });
        }

        var workloadDetectionConfigs = workloadId.HasValue
            ? await LoadDetectionConfigsByWorkloadAsync(workloadId)
            : await LoadDetectionConfigsByWorkloadAsync(null, node);

        var timeoutSeconds = _configuration.GetValue<int>("AgentProbeTimeoutSeconds", 30);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        NodeDetectResponse? agentResponse = null;
        bool probeSuccessful = false;
        string? error = null;

        try
        {
                var allPackageRequests = workloadDetectionConfigs
                    .SelectMany(kvp => kvp.Value)
                    .Select(c => new PackageDetectionRequest
                    {
                        PackageId = c.PackageId,
                        Name = c.Name,
                        Version = c.Version,
                        Detection = c.Detection
                    })
                    .DistinctBy(c => c.PackageId)
                    .ToList();

                var requestBody = new DetectRequest { Packages = allPackageRequests };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Probing agent on node {NodeId} ({Hostname}) at {IpAddress}",
                node.NodeId, node.Hostname, node.IpAddress);

            var response = await client.PostAsync(
                $"http://{node.IpAddress}:5001/api/detect", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent probe returned non-200 for node {NodeId} ({Hostname}): {StatusCode}",
                    node.NodeId, node.Hostname, (int)response.StatusCode);
                error = $"Agent returned status {(int)response.StatusCode}";
            }
            else
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                agentResponse = JsonSerializer.Deserialize<NodeDetectResponse>(
                    responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (agentResponse is null)
                {
                    _logger.LogWarning(
                        "Failed to deserialize agent response for node {NodeId} ({Hostname})",
                        node.NodeId, node.Hostname);
                    error = "Failed to deserialize agent response";
                }
                else
                {
                    probeSuccessful = true;
                }
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Agent probe timed out for node {NodeId} ({Hostname})",
                node.NodeId, node.Hostname);
            error = "Agent probe timed out";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Agent unreachable for node {NodeId} ({Hostname}): {Error}",
                node.NodeId, node.Hostname, ex.Message);
            error = $"Agent unreachable: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error probing node {NodeId} ({Hostname})",
                node.NodeId, node.Hostname);
            error = $"Unexpected error: {ex.Message}";
        }

        if (!probeSuccessful || agentResponse is null)
        {
            return Ok(new NodePreCheckSummary
            {
                CheckedAt = DateTime.UtcNow,
                Items = new List<PreCheckItem>
                {
                    new PreCheckItem
                    {
                        Category = "error",
                        Name = "Probe Failed",
                        Status = "failed",
                        Detail = error ?? "Unknown error"
                    }
                }
            });
        }

        var summary = await ReconcileProbeResults(node, agentResponse, workloadDetectionConfigs);

        await _db.SaveChangesAsync();
        return Ok(summary);
    }

    private async Task<(NodeDetectResponse? Response, string? Error)> ProbeNodeAsync(
        NodeEntity node,
        Dictionary<Guid, List<DetectionConfigDto>> workloadDetectionConfigs)
    {
        var timeoutSeconds = _configuration.GetValue<int>("AgentProbeTimeoutSeconds", 30);
        var client = _httpClientFactory.CreateClient();

        try
        {
            var allPackageRequests = workloadDetectionConfigs
                .SelectMany(kvp => kvp.Value)
                .Select(c => new PackageDetectionRequest
                {
                    PackageId = c.PackageId,
                    Name = c.Name,
                    Version = c.Version,
                    Detection = c.Detection
                })
                .DistinctBy(c => c.PackageId)
                .ToList();

            var requestBody = new DetectRequest { Packages = allPackageRequests };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Probing agent on node {NodeId} ({Hostname}) at {IpAddress} for {PackageCount} packages",
                node.NodeId, node.Hostname, node.IpAddress, allPackageRequests.Count);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var response = await client.PostAsync(
                $"http://{node.IpAddress}:5001/api/detect", content, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent probe returned non-200 for node {NodeId} ({Hostname}): {StatusCode}",
                    node.NodeId, node.Hostname, (int)response.StatusCode);
                return (null, $"Agent returned status {(int)response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var agentResponse = JsonSerializer.Deserialize<NodeDetectResponse>(
                responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (agentResponse is null)
            {
                _logger.LogWarning(
                    "Failed to deserialize agent response for node {NodeId} ({Hostname})",
                    node.NodeId, node.Hostname);
                return (null, "Failed to deserialize agent response");
            }

            return (agentResponse, null);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Agent probe timed out for node {NodeId} ({Hostname})",
                node.NodeId, node.Hostname);
            return (null, "Agent probe timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Agent unreachable for node {NodeId} ({Hostname}): {Error}",
                node.NodeId, node.Hostname, ex.Message);
            return (null, $"Agent unreachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error probing node {NodeId} ({Hostname})",
                node.NodeId, node.Hostname);
            return (null, $"Unexpected error: {ex.Message}");
        }
    }

    private async Task<List<DetectionConfigDto>> LoadDetectionConfigsForRevisionAsync(Guid revisionId)
    {
        var revision = await _db.WorkloadRevisions
            .Include(r => r.Packages)
            .FirstOrDefaultAsync(r => r.RevisionId == revisionId);

        if (revision == null)
            return new List<DetectionConfigDto>();

        var packageIds = revision.Packages.Select(p => p.PackageId).Distinct().ToList();
        var packages = await _db.Packages
            .Where(p => packageIds.Contains(p.PackageId))
            .ToListAsync();

        var packageMap = packages.ToDictionary(p => p.PackageId);

        var configs = new List<DetectionConfigDto>();
        foreach (var wp in revision.Packages)
        {
            if (!packageMap.TryGetValue(wp.PackageId, out var pkg))
                continue;

            var detection = string.IsNullOrWhiteSpace(pkg.DetectionConfigJson)
                ? new DetectionConfig()
                : JsonSerializer.Deserialize<DetectionConfig>(
                    pkg.DetectionConfigJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DetectionConfig();

            configs.Add(new DetectionConfigDto
            {
                PackageId = pkg.PackageId,
                Name = pkg.Name,
                Version = pkg.Version,
                Detection = detection
            });
        }

        return configs;
    }

    private async Task<NodePreCheckSummary> ReconcileProbeResults(
        NodeEntity node,
        NodeDetectResponse agentResponse,
        Dictionary<Guid, List<DetectionConfigDto>> workloadDetectionConfigs)
    {
        var items = new List<PreCheckItem>();
        var agentResultMap = agentResponse.Results.ToDictionary(r => r.PackageId);

        items.Add(new PreCheckItem
        {
            Category = "os",
            Name = "Operating System",
            Status = "passed",
            Detail = node.OsVersion
        });

        items.Add(new PreCheckItem
        {
            Category = "agent",
            Name = "Agent Version",
            Status = "passed",
            Detail = node.AgentVersion
        });

        items.Add(new PreCheckItem
        {
            Category = "disk",
            Name = "Disk Space",
            Status = agentResponse.DiskInfo.FreeBytes > 0 ? "passed" : "warning",
            Detail = agentResponse.DiskInfo.FreeBytes > 0
                ? $"Free: {FormatBytes(agentResponse.DiskInfo.FreeBytes)} / Total: {FormatBytes(agentResponse.DiskInfo.TotalBytes)}"
                : "Disk info unavailable"
        });

        var existingStateMap = node.NodeWorkloadStates.ToDictionary(s => s.WorkloadId);

        foreach (var kvp in workloadDetectionConfigs)
        {
            var workloadId = kvp.Key;
            var detectionConfigs = kvp.Value;
            var packageIds = detectionConfigs.Select(d => d.PackageId).ToHashSet();

            var hasAnyDetected = detectionConfigs.Any(d =>
                agentResultMap.TryGetValue(d.PackageId, out var r) &&
                r.Status != PreCheckStatus.NotPresent);

            var existingState = existingStateMap.GetValueOrDefault(workloadId);

            if (existingState is null)
            {
                // Unassigned workload — report probe results and create DB state if packages detected
                var presentCount = detectionConfigs.Count(d =>
                    agentResultMap.TryGetValue(d.PackageId, out var r) &&
                    r.Status != PreCheckStatus.NotPresent);
                var totalCount = detectionConfigs.Count;

                items.AddRange(BuildPerPackageItems(detectionConfigs, agentResultMap, "", isUnassigned: true));
                items.Add(new PreCheckItem
                {
                    Category = "package",
                    Name = $"packages: {presentCount}/{totalCount} present",
                    Status = presentCount == totalCount ? "passed" :
                             presentCount == 0 ? "info" : "warning",
                    Detail = presentCount == totalCount ? "all packages present" :
                             presentCount == 0 ? "not installed" : "partially installed"
                });

                if (hasAnyDetected)
                {
                    var allMatch = detectionConfigs.All(d =>
                        agentResultMap.TryGetValue(d.PackageId, out var r) &&
                        r.Status == PreCheckStatus.AlreadySatisfied &&
                        VersionComparisonService.Matches(d.Version, r.ActualVersion));

                    var newState = new NodeWorkloadStateEntity
                    {
                        NodeWorkloadStateId = Guid.NewGuid(),
                        NodeId = node.NodeId,
                        WorkloadId = workloadId,
                        CurrentRevisionId = null,
                        PackageStatesJson = BuildPackageStatesJson(detectionConfigs, agentResultMap),
                        Status = allMatch ? "Current" : "Drifted",
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    _db.NodeWorkloadStates.Add(newState);
                }
            }
            else
            {
                var dbRevisionId = existingState.CurrentRevisionId;
                var dbRevision = existingState.CurrentRevision;
                var dbVersion = dbRevision?.Version ?? "";
                var dbPackageStates = string.IsNullOrEmpty(existingState.PackageStatesJson) || existingState.PackageStatesJson == "{}"
                    ? null
                    : existingState.PackageStatesJson;

                if (!hasAnyDetected)
                {
                    // Scenario B — DB says installed, agent says nothing
                    _db.NodeWorkloadStates.Remove(existingState);
                    items.Add(new PreCheckItem
                    {
                        Category = "package",
                        Name = dbVersion,
                        Status = "failed",
                        Detail = "not installed"
                    });
                }
                else
                {
                    // Check for match or drift
                    var allMatch = detectionConfigs.All(d =>
                        agentResultMap.TryGetValue(d.PackageId, out var r) &&
                        r.Status == PreCheckStatus.AlreadySatisfied &&
                        VersionComparisonService.Matches(d.Version, r.ActualVersion));

                    if (allMatch)
                    {
                        // Scenario A — Match; update PackageStatesJson so
                        // BuildReadOnlyPreCheckSummary reflects current good state
                        existingState.PackageStatesJson = BuildPackageStatesJson(detectionConfigs, agentResultMap);
                        existingState.Status = "Current";
                        existingState.UpdatedAtUtc = DateTime.UtcNow;
                        items.AddRange(BuildPerPackageItems(detectionConfigs, agentResultMap, dbVersion));
                    }
                    else
                    {
                        // Scenario D or E — Drift detected
                        // Update PackageStatesJson to reflect actual state
                        // NEVER auto-promote CurrentRevisionId
                        existingState.PackageStatesJson = BuildPackageStatesJson(detectionConfigs, agentResultMap);
                        existingState.Status = "Drifted";
                        existingState.UpdatedAtUtc = DateTime.UtcNow;

                        var presentCount = detectionConfigs.Count(d =>
                            agentResultMap.TryGetValue(d.PackageId, out var r) &&
                            r.Status != PreCheckStatus.NotPresent);
                        var totalCount = detectionConfigs.Count;

                        items.AddRange(BuildPerPackageItems(detectionConfigs, agentResultMap, dbVersion));
                        items.Add(new PreCheckItem
                        {
                            Category = "package",
                            Name = $"drift: {presentCount}/{totalCount} packages present",
                            Status = "warning",
                            Detail = "drift detected"
                        });
                    }
                }
            }
        }

        return new NodePreCheckSummary
        {
            CheckedAt = DateTime.UtcNow,
            Items = items
        };
    }

    private static string BuildPackageStatesJson(
        List<DetectionConfigDto> detectionConfigs,
        Dictionary<Guid, PackageDetectionResult> agentResultMap)
    {
        var states = new Dictionary<string, Dictionary<string, object>>();
        foreach (var cfg in detectionConfigs)
        {
            var agentResult = agentResultMap.GetValueOrDefault(cfg.PackageId);
            states[cfg.PackageId.ToString()] = new Dictionary<string, object>
            {
                ["name"] = cfg.Name,
                ["actualVersion"] = agentResult?.ActualVersion ?? "",
                ["expectedVersion"] = cfg.Version,
                ["comparison"] = agentResult is not null
                    ? ComputeComparison(agentResult.ActualVersion, cfg.Version)
                    : "unknown",
                ["status"] = agentResult?.Status.ToString() ?? "Unknown",
                ["updatedAt"] = DateTime.UtcNow.ToString("O")
            };
        }
        return JsonSerializer.Serialize(states);
    }

    private static List<PreCheckItem> BuildPerPackageItems(
        List<DetectionConfigDto> detectionConfigs,
        Dictionary<Guid, PackageDetectionResult> agentResultMap,
        string expectedRevisionVersion,
        bool isUnassigned = false)
    {
        var items = new List<PreCheckItem>();
        foreach (var cfg in detectionConfigs)
        {
            var agentResult = agentResultMap.GetValueOrDefault(cfg.PackageId);
            var status = agentResult switch
            {
                null => "error",
                { Status: PreCheckStatus.AlreadySatisfied } => "passed",
                { Status: PreCheckStatus.WrongVersion } => "warning",
                { Status: PreCheckStatus.NotPresent } => isUnassigned ? "info" : "failed",
                _ => "unknown"
            };
            items.Add(new PreCheckItem
            {
                Category = "package",
                Name = cfg.Name,
                ActualVersion = agentResult?.ActualVersion ?? "",
                Status = status,
                Detail = agentResult?.Status switch
                {
                    PreCheckStatus.AlreadySatisfied => "",
                    PreCheckStatus.WrongVersion => $"wrong version: expected {cfg.Version}, found {agentResult?.ActualVersion} ({ComputeComparison(agentResult?.ActualVersion, cfg.Version)})",
                    PreCheckStatus.NotPresent => "not installed",
                    null => "probe failed",
                    _ => ""
                }
            });
        }
        return items;
    }

    private async Task<Dictionary<Guid, List<DetectionConfigDto>>> LoadDetectionConfigsByWorkloadAsync(
        Guid? workloadId, NodeEntity? node = null)
    {
        IQueryable<WorkloadRevisionEntity> revisionQuery = _db.WorkloadRevisions
            .Include(r => r.Packages);

        if (workloadId.HasValue)
        {
            revisionQuery = revisionQuery.Where(r => r.WorkloadId == workloadId.Value);
        }

        var revisions = await revisionQuery.ToListAsync();

        List<WorkloadRevisionEntity> effectiveRevisions;

        if (node is not null && !workloadId.HasValue)
        {
            var assignedRevisionIds = node.NodeWorkloadStates
                .Where(s => s.CurrentRevisionId.HasValue)
                .Select(s => s.CurrentRevisionId!.Value)
                .ToHashSet();

            effectiveRevisions = revisions
                .Where(r => assignedRevisionIds.Contains(r.RevisionId))
                .ToList();

            _logger.LogInformation(
                "LoadDetectionConfigs: using ASSIGNED revisions for node {NodeId}. " +
                "AssignedRevisionIds=[{AssignedIds}], EffectiveIds=[{EffectiveIds}], " +
                "TotalRevisions={TotalRevs}, IsPublishedFilterCount={PublishedCount}",
                node.NodeId,
                string.Join(",", assignedRevisionIds.Select(id => id.ToString()[..8])),
                string.Join(",", effectiveRevisions.Select(r => r.RevisionId.ToString()[..8])),
                revisions.Count,
                revisions.Count(r => r.IsPublished));
        }
        else
        {
            effectiveRevisions = revisions.Where(r => r.IsPublished).ToList();
        }

        var packageIds = effectiveRevisions
            .SelectMany(r => r.Packages)
            .Select(p => p.PackageId)
            .Distinct()
            .ToList();

        var packages = await _db.Packages
            .Where(p => packageIds.Contains(p.PackageId))
            .ToListAsync();

        var packageMap = packages.ToDictionary(p => p.PackageId);

        var result = new Dictionary<Guid, List<DetectionConfigDto>>();

        foreach (var rev in effectiveRevisions)
        {
            var configs = new List<DetectionConfigDto>();
            foreach (var wp in rev.Packages)
            {
                if (!packageMap.TryGetValue(wp.PackageId, out var pkg))
                {
                    continue;
                }

                var detection = string.IsNullOrWhiteSpace(pkg.DetectionConfigJson)
                    ? new DetectionConfig()
                    : JsonSerializer.Deserialize<DetectionConfig>(
                        pkg.DetectionConfigJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DetectionConfig();

                configs.Add(new DetectionConfigDto
                {
                    PackageId = pkg.PackageId,
                    Name = pkg.Name,
                    Version = pkg.Version,
                    Detection = detection
                });
            }

            if (configs.Count > 0)
            {
                result[rev.WorkloadId] = configs;
            }
        }

        return result;
    }

    private static NodePreCheckSummary BuildReadOnlyPreCheckSummary(NodeEntity entity)
    {
        var items = new List<PreCheckItem>
        {
            new PreCheckItem
            {
                Category = "os",
                Name = "Operating System",
                Status = "passed",
                Detail = entity.OsVersion
            },
            new PreCheckItem
            {
                Category = "agent",
                Name = "Agent Version",
                Status = "passed",
                Detail = entity.AgentVersion
            },
            new PreCheckItem
            {
                Category = "disk",
                Name = "Disk Space",
                Status = "warning",
                Detail = "Run pre-check to probe agent"
            }
        };

        foreach (var state in entity.NodeWorkloadStates)
        {
            var hasPackageState = !string.IsNullOrEmpty(state.PackageStatesJson) && state.PackageStatesJson != "{}";

            if (hasPackageState)
            {
                string status = "passed";
                string detail = "";

                try
                {
                    using var doc = JsonDocument.Parse(state.PackageStatesJson);
                    var anyFailed = false;
                    var anyWarning = false;

                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.TryGetProperty("status", out var statusEl))
                        {
                            var s = statusEl.GetString();
                            if (s == "NotPresent") anyFailed = true;
                            else if (s == "WrongVersion") anyWarning = true;
                        }
                    }

                    if (anyFailed) status = "failed";
                    else if (anyWarning) status = "warning";
                }
                catch
                {
                    status = "warning";
                    detail = "Could not parse package state";
                }

                items.Add(new PreCheckItem
                {
                    Category = "package",
                    Name = state.Workload?.Name ?? state.WorkloadId.ToString(),
                    ActualVersion = state.CurrentRevision?.Version ?? "",
                    Status = status,
                    Detail = detail
                });
            }
            else
            {
                items.Add(new PreCheckItem
                {
                    Category = "package",
                    Name = state.Workload?.Name ?? state.WorkloadId.ToString(),
                    ActualVersion = state.CurrentRevision?.Version ?? "",
                    Status = InferStatus(state),
                    Detail = state.Status is not null && !state.Status.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                        ? $"State: {state.Status.ToLowerInvariant()} — Run pre-check to probe agent"
                        : "Run pre-check to probe agent"
                });
            }
        }

        var checkedAt = entity.NodeWorkloadStates.Any()
            ? entity.NodeWorkloadStates.Max(s => s.LastProbedAtUtc ?? s.UpdatedAtUtc)
            : DateTime.UtcNow;

        return new NodePreCheckSummary
        {
            CheckedAt = checkedAt,
            Items = items
        };
    }

    private static string InferStatus(NodeWorkloadStateEntity state)
    {
        if (string.IsNullOrEmpty(state.PackageStatesJson) || state.PackageStatesJson == "{}")
        {
            return state.Status is not null && !state.Status.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                ? state.Status.ToLowerInvariant()
                : "pending";
        }
        return "running";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }

    private static bool IsDuplicateHostnameConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteEx
            && sqliteEx.SqliteErrorCode == 19
            && sqliteEx.Message.Contains("UNIQUE constraint failed: Nodes.Hostname", StringComparison.Ordinal);
    }

    private static string ComputeComparison(string? actualVersion, string expectedVersion)
    {
        var cmp = VersionComparisonService.CompareVersions(actualVersion, expectedVersion);
        return cmp switch
        {
            < 0 => "older",
            > 0 => "newer",
            0 => "same",
            null => "unknown"
        };
    }

    private sealed class DetectionConfigDto
    {
        public Guid PackageId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DetectionConfig Detection { get; set; } = new();
    }
}

