# Implementation Plan — Phase 2: Core Pipeline

> **MVP Plan Ref:** Section 15, Phase 2  
> **Depends on:** Phase 1 complete

## Phase 1 Fix Prerequisites

The following items were addressed in Phase 1 fixes and are prerequisites for Phase 2:

- **P1-002 GAP-1:** `AgentNodeStatus.WORKLOAD_ASSIGNED` added to the enum. Phase 2 references this status throughout.
- **P1-002 GAP-2:** `PollingIntervalSeconds` column added to `AgentNode`. Phase 2 enrollment and LOST detection use this field.
- **P1-002 Modified columns:** `WorkloadPackage.WorkloadVersion` column added. All Phase 2 queries must filter by this column.
- **P1-002 GAP-3:** Unique index on `AgentSecret` in `AgentNode` table. Phase 2 auth middleware benefits from this index for efficient lookups.

## Dependency Graph

```
P1-005+P1-006 (enrollment) ── P2-001 (agent background service)
                                      │
                               P2-002 (bearer auth middleware)
                                      │
P1-009+P1-010+P1-011 (artifacts+workloads) ──┬── P2-009 (artifact download endpoints)
                                              │
                                      P2-003 (polling endpoint)
                                              │
                                      P2-010 (step reporting + run completion)
                                              │
                                      P2-004 (heartbeat + LOST detection + stale run recovery)
                                              │
                                      P2-005 (pre-check dispatch)
                                              │
                                      P2-006 (detection execution)
                                              │
                                      P2-007 (DB reconciliation)
                                              │
                                      P2-008 (delta summary)
```

## Implementation Order

> **Important:** Phase 2 tickets should be implemented in the following order to satisfy dependencies:
>
> P2-002 (auth) → P2-009 (artifact download) → P2-003 (polling endpoint) → P2-010 (step reporting + run completion) → P2-004 (heartbeat + LOST detection) → P2-005+ (remaining)
>
> The heartbeat endpoint (P2-004) must be implemented **before** the agent polling service sends heartbeats. The artifact download endpoints (P2-009) and step reporting endpoints (P2-010) must exist before the agent can complete a full polling cycle.

---

## TICKET P2-001: Agent Background Service Skeleton

**MVP Plan Ref:** Section 2 (Agent project), Section 8 (Agent Communication Model)  
**Depends on:** P1-007, P1-008

### Description

Create the Agent's background service that runs as a Windows Service. This is the polling loop skeleton that will be extended in subsequent tickets. For now, implement the service lifecycle, configuration loading, and HTTP client setup — the actual polling logic will be added in P2-003.

### Tasks

- [ ] Create `AgentPollingService : BackgroundService` with `ExecuteAsync` loop
- [ ] Load `agent.json` configuration at startup
- [ ] Configure `HttpClient` for Orchestrator communication (base URL from `agent.json`)
- [ ] Set `Authorization: Bearer {agentSecret}` header on all requests
- [ ] Implement polling loop skeleton: sleep for `pollingIntervalSeconds`, then poll
- [ ] Handle graceful shutdown via `CancellationToken`
- [ ] Implement `AgentService` lifetime management (start/stop)
- [ ] Configure `UseWindowsService()` in `Program.cs`
- [ ] Add `Agent` service name (fallback: `OrchAgent` if `Agent` is unavailable)
- [ ] Add ILogger throughout for lifecycle events
- [ ] Add `Uri GetAbsoluteUrl(string relativePath)` helper method to `AgentConfig` or `AgentPollingService` that combines the configured Orchestrator base URL with a relative path (see I4 below)

### Code Example — Background Service Skeleton

```csharp
// Services/AgentPollingService.cs
public class AgentPollingService : BackgroundService
{
    private readonly ILogger<AgentPollingService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private AgentConfig _config = null!;

    public AgentPollingService(
        ILogger<AgentPollingService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent polling service starting");

        if (!TryLoadConfig())
        {
            _logger.LogCritical("No agent.json found. Run Agent.exe --enroll first.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during polling cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Agent polling service stopping");
    }

    private bool TryLoadConfig()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OrchestratorAgent", "agent.json");

        if (!File.Exists(configPath)) return false;

        var json = File.ReadAllText(configPath);
        _config = JsonSerializer.Deserialize<AgentConfig>(json)!;
        return true;
    }

    // I4: Helper to resolve relative API paths against the configured base URL.
    // The OrchestratorUrl in agent.json must be an absolute URL (e.g., "http://hostname:5000").
    // Relative URLs returned by API responses (artifact download URLs, etc.) are resolved
    // client-side using this method.
    private Uri GetAbsoluteUrl(string relativePath)
    {
        var baseUri = new Uri(_config.OrchestratorUrl);
        return new Uri(baseUri, relativePath);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Orchestrator");
        client.BaseAddress = new Uri(_config.OrchestratorUrl);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.AgentSecret);

        try
        {
            await client.PostAsync(
                $"/api/agents/{_config.AgentId}/heartbeat", null, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Heartbeat failed — Orchestrator unreachable");
        }

        // Task polling will be added in P2-003
    }
}
```

### Configuration Notes

- The `OrchestratorUrl` in `agent.json` **must** be an absolute URL (e.g., `http://hostname:5000`). Relative URLs are not supported for the base address.
- `PollingIntervalSeconds` is persisted on `AgentNode` during enrollment (P1-006) and is sent to the agent in the enrollment response. The agent uses this value to configure its polling loop.
- All relative URLs in API responses (e.g., artifact download URLs like `/api/artifacts/{id}/download`) should be resolved client-side by the Agent using the `GetAbsoluteUrl()` helper method above.

### Acceptance Criteria

- [ ] Agent project builds and runs as a console app for development
- [ ] `AgentPollingService` loads `agent.json` at startup
- [ ] Polling loop runs on the configured `PollingIntervalSeconds` (from enrollment, not a hardcoded default)
- [ ] `HttpClient` configured with Orchestrator URL and Bearer token
- [ ] Graceful shutdown on `CancellationToken` cancellation
- [ ] Heartbeat sent on each polling cycle (placeholder for now)
- [ ] Service logs lifecycle events via ILogger
- [ ] When `agent.json` doesn't exist, service logs error and doesn't crash in a loop
- [ ] `GetAbsoluteUrl()` correctly resolves relative paths against the configured base URL
- [ ] `agent.json` `OrchestratorUrl` field is validated as absolute URL at load time

### Verification Steps

1. Enroll an agent (P1-007) to generate `agent.json` — verify `PollingIntervalSeconds` is in the config
2. Start Agent service — verify logs show "Agent polling service starting"
3. Check logs for heartbeat attempts to `POST /api/agents/{agentId}/heartbeat`
4. Verify the delay between polling cycles matches the `PollingIntervalSeconds` from `agent.json`
5. Stop service — verify logs show "Agent polling service stopping"
6. Delete `agent.json`, start Agent — verify error logged, service doesn't crash loop
7. Verify `GetAbsoluteUrl("/api/artifacts/1/download")` produces `http://hostname:5000/api/artifacts/1/download`

---

## TICKET P2-002: Agent Bearer Auth Middleware

**MVP Plan Ref:** Section 8 (API Contract — Bearer token auth on all Agent requests)  
**Depends on:** P1-006

### Description

Create middleware that validates `agentSecret` as a Bearer token on all Agent-facing API endpoints (except enrollment). The middleware extracts the `agentId` from the token and validates it against the database.

### Tasks

- [ ] Create `AgentAuthMiddleware` that:
  1. Reads `Authorization: Bearer {token}` header
  2. Looks up the `AgentNode` by `agentSecret` in the database (benefits from the unique index on `AgentSecret` added in P1-002 GAP-3)
  3. Sets `HttpContext.Items["AgentId"]` with the matched `agentId`
  4. Returns 401 if token is missing or invalid
- [ ] Create `[AgentAuth]` attribute to mark controllers/endpoints requiring agent auth
- [ ] Apply `[AgentAuth]` to all Agent-facing endpoints:
  - `GET /api/agents/{agentId}/tasks/next`
  - `POST /api/agents/{agentId}/heartbeat`
  - `POST /api/runs/{runId}/steps`
  - `POST /api/runs/{runId}/complete`
  - `GET /api/artifacts/{artifactId}/download`
  - `GET /api/artifacts/{artifactId}/manifest`
- [ ] Do NOT apply auth to: `POST /api/enrollment/tokens`, `POST /api/agents/enroll`
- [ ] Validate that the `agentId` in the URL path matches the `agentId` from the auth token
- [ ] Add `AgentAuthMiddleware` to ASP.NET pipeline

### Code Example — Middleware

```csharp
// Middleware/AgentAuthMiddleware.cs
public class AgentAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;

    public AgentAuthMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<AgentAuthAttribute>() == null)
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            context.Response.StatusCode = 401;
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agent = await db.AgentNodes
            .FirstOrDefaultAsync(a => a.AgentSecret == token && a.Status != AgentNodeStatus.UNREGISTERED);

        if (agent == null)
        {
            context.Response.StatusCode = 401;
            return;
        }

        context.Items["AgentId"] = agent.AgentId;
        await _next(context);
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AgentAuthAttribute : Attribute { }
```

### Acceptance Criteria

- [ ] Requests to `[AgentAuth]` endpoints without Bearer token return 401
- [ ] Requests with invalid/expired `agentSecret` return 401
- [ ] Requests with valid `agentSecret` pass through and set `HttpContext.Items["AgentId"]`
- [ ] `POST /api/enrollment/tokens` and `POST /api/agents/enroll` do NOT require auth
- [ ] Unregistered agents (status = `UNREGISTERED`) cannot authenticate
- [ ] Agent-facing endpoints cannot be accessed without auth even by knowing the URL
- [ ] The unique index on `AgentSecret` (from P1-002 GAP-3) is used for efficient token lookups

### Verification Steps

1. `curl -X POST http://localhost:5000/api/agents/{agentId}/heartbeat` (no auth) → 401
2. `curl -X POST http://localhost:5000/api/agents/{agentId}/heartbeat -H "Authorization: Bearer invalid"` → 401
3. Enroll an agent, get `agentSecret`
4. `curl -X POST http://localhost:5000/api/agents/{agentId}/heartbeat -H "Authorization: Bearer {agentSecret}"` → 200
5. Unregister the agent, try same request → 401
6. `curl -X POST http://localhost:5000/api/enrollment/tokens` (no auth) → 200 (auth not required)

---

## TICKET P2-003: Agent Polling Endpoint & Task Queue

**MVP Plan Ref:** Section 8 (Agent Polling — fetch next task), Section 7 (WorkloadRuns)  
**Depends on:** P2-001, P2-002, P2-009, P1-011

### Description

Implement the Agent polling endpoint that returns the next pending task (WorkloadRun) for an Agent. Also implement the task creation endpoint (used by the Orchestrator UI to dispatch runs). Only one active WorkloadRun per Agent at a time.

### Tasks

- [ ] Create `IRunService` interface and `RunService` implementation
- [ ] `CreateRunAsync(agentId, workloadId, workloadVersion, mode)` — create a `WorkloadRun` with status `PENDING`
- [ ] `GetNextTaskAsync(agentId)` — find the oldest PENDING `WorkloadRun` for this agent, transition to `RUNNING`, return task details
- [ ] Create `RunsController` with:
  - `POST /api/runs` — dispatch a new run (UI-facing)
  - `GET /api/agents/{agentId}/tasks/next` — Agent polling endpoint
- [ ] Implement "one active run per agent" constraint: reject `CreateRunAsync` if agent already has a PENDING or RUNNING run
- [ ] Return full task detail in polling response (per Section 8 API Contract):
  - `runId`, `mode`, `workloadId`, `workloadVersion`
  - `packages[]` with manifest details, download URLs, detection rules, init steps
  - `deltaStatus` and `phase` fields for context-aware agent actions
- [ ] Construct artifact download URLs: `/api/artifacts/{artifactId}/download` and `/api/artifacts/{artifactId}/manifest`
- [ ] Update Agent `AgentPollingService.PollAsync()` to call `GET /api/agents/{agentId}/tasks/next` and process results
- [ ] Ensure all `WorkloadPackage` queries include `WorkloadVersion` filter (B2, I5)
- [ ] Use eager loading with `.Include()` / `.ThenInclude()` to avoid N+1 queries (B1)
- [ ] Add null checks for missing artifacts (B5)
- [ ] Add version parsing fallback for non-standard version strings (B3)

### Code Example — Polling Response Model

```csharp
// Models/Dto/NextTaskResponse.cs
public class NextTaskResponse
{
    public int RunId { get; set; }
    public string Action { get; set; } = string.Empty; // INSTALL, UPDATE, UNINSTALL, SKIP
    public string? DeltaStatus { get; set; } // MATCHES, VERSION_DRIFT, NOT_INSTALLED, NOT_IN_WORKLOAD
    public string? Phase { get; set; } // INSTALL, UPDATE, UNINSTALL
    public string Mode { get; set; } // PRE_CHECK, INSTALL, UPDATE, UNINSTALL
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadVersion { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;
    public List<TaskPackage> Packages { get; set; } = [];
}

public class TaskPackage
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ManifestUrl { get; set; } = string.Empty;
    public string BinaryUrl { get; set; } = string.Empty;
    public string InstallCommand { get; set; } = string.Empty;
    public string? InstallArgs { get; set; }
    public string? UninstallCommand { get; set; }
    public string? UninstallArgs { get; set; }
    public string UpdateStrategy { get; set; } = "overinstall";
    public DetectionRule Detection { get; set; } = null!;
    public List<string> PreInitSteps { get; set; } = [];
    public List<string> PostInitSteps { get; set; } = [];
}

public class DetectionRule
{
    public string Type { get; set; } = string.Empty; // registry | filePath
    public string Key { get; set; } = string.Empty;
    public string? ValueName { get; set; }
    public string? ExpectedValue { get; set; }
}
```

### Code Example — GetNextTask Logic

```csharp
// Services/RunService.cs
public async Task<NextTaskResponse?> GetNextTaskAsync(string agentId)
{
    var run = await _db.WorkloadRuns
        .Where(r => r.AgentId == agentId && r.Status == WorkloadRunStatus.PENDING)
        .OrderBy(r => r.CreatedAt)
        .FirstOrDefaultAsync();

    if (run == null) return null;

    run.Status = WorkloadRunStatus.RUNNING;
    run.StartedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();

    // B1: Eagerly load workload with packages and artifacts in a single query
    var workload = await _db.Workloads
        .Include(w => w.Packages)
            .ThenInclude(p => p.Artifact)
        .FirstOrDefaultAsync(w => w.Id == run.WorkloadId);

    // B2 + I5: Filter WorkloadPackages by WorkloadVersion
    var packages = await _db.WorkloadPackages
        .Where(wp => wp.WorkloadId == run.WorkloadId && wp.WorkloadVersion == run.WorkloadVersion)
        .ToListAsync();

    var taskPackages = new List<TaskPackage>();
    foreach (var wp in packages)
    {
        // B5: Null check for missing artifacts
        var artifact = await _db.Artifacts
            .FirstOrDefaultAsync(a => a.PackageId == wp.PackageId && a.Version == wp.PackageVersion);

        if (artifact == null)
        {
            _logger.LogWarning("Package {PackageId} has no associated artifact, skipping", wp.PackageId);
            continue;
        }

        taskPackages.Add(new TaskPackage
        {
            PackageId = wp.PackageId,
            Version = wp.PackageVersion,
            ManifestUrl = $"/api/artifacts/{artifact.Id}/manifest",
            BinaryUrl = $"/api/artifacts/{artifact.Id}/download",
            // ... map remaining fields from artifact manifest + wp
        });
    }

    // Determine action and delta context based on run mode and agent state
    var (action, deltaStatus, phase) = DetermineTaskContext(agentId, run);

    return new NextTaskResponse
    {
        RunId = run.Id,
        Action = action,
        DeltaStatus = deltaStatus,
        Phase = phase,
        Mode = run.Mode.ToString(),
        WorkloadId = run.WorkloadId,
        WorkloadVersion = run.WorkloadVersion,
        WorkloadName = workload?.Name ?? string.Empty,
        Packages = taskPackages
    };
}

private (string action, string? deltaStatus, string? phase) DetermineTaskContext(
    string agentId, WorkloadRun run)
{
    return run.Mode switch
    {
        WorkloadRunMode.INSTALL => ("INSTALL", null, "INSTALL"),
        WorkloadRunMode.UPDATE => ("UPDATE", null, "UPDATE"),
        WorkloadRunMode.UNINSTALL => ("UNINSTALL", null, "UNINSTALL"),
        WorkloadRunMode.PRE_CHECK => ("INSTALL", "NOT_INSTALLED", null),
        _ => ("INSTALL", null, null)
    };
}
```

### Code Example — Version Comparison with Fallback (B3)

```csharp
// Services/VersionComparison.cs
public static class VersionComparison
{
    /// <summary>
    /// Compares two version strings. Uses System.Version for standard version formats
    /// and falls back to lexicographic comparison for non-standard versions
    /// (e.g., "2.0.1-beta", "1.0.0-rc1").
    /// </summary>
    public static int CompareVersions(string v1, string v2)
    {
        if (Version.TryParse(v1, out var ver1) && Version.TryParse(v2, out var ver2))
        {
            return ver1.CompareTo(ver2);
        }

        // Fallback: treat as opaque string comparison for non-standard versions
        // Consider using SemanticVersioning NuGet package for proper semver support
        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }
}
```

### Acceptance Criteria

- [ ] `POST /api/runs` creates a `WorkloadRun` with status `PENDING` and returns run details
- [ ] `GET /api/agents/{agentId}/tasks/next` returns the next PENDING run for the agent
- [ ] Polling transitions run status from `PENDING` to `RUNNING`
- [ ] Only one active run per agent: attempting to create a second run while one is PENDING/RUNNING returns 409 Conflict
- [ ] Polling response includes full task details (mode, workload, packages with detection rules, URLs, init steps)
- [ ] Polling response includes `Action`, `DeltaStatus`, and `Phase` fields for agent context
- [ ] If no pending tasks, polling returns `204 No Content`
- [ ] Agent auth required on polling endpoint
- [ ] Agent `AgentPollingService` updated to process task responses
- [ ] `WorkloadPackage` queries always filter by `WorkloadVersion` (no missing version columns)
- [ ] Packages with missing artifacts are skipped with a warning log, not causing null reference exceptions
- [ ] Artifact download URLs in task responses are relative paths (e.g., `/api/artifacts/{id}/download`); the Agent resolves them using its configured `OrchestratorUrl`

### Verification Steps

1. Create a WorkloadRun via `POST /api/runs` with agentId, workloadId, mode=PRE_CHECK
2. Agent polls `GET /api/agents/{agentId}/tasks/next` with valid auth → receives task details
3. Check DB: run status is now `RUNNING`
4. Verify response includes `Action`, `DeltaStatus`, `Phase` fields
5. Agent polls again → `204 No Content` (no more pending tasks)
6. Attempt to create another run for same agent while first is RUNNING → 409 Conflict
7. Agent receives full package details including detection rules and download URLs
8. Verify that packages with no artifact are skipped without error
9. Verify `WorkloadPackage` queries include version filter in SQL output

---

## TICKET P2-004: Agent Heartbeat & LOST Detection

**MVP Plan Ref:** Section 8 (Agent Heartbeat), Section 12 (AgentNode Status — LOST detection)  
**Depends on:** P2-002

### Description

Implement the heartbeat endpoint and the LOST detection mechanism. Agents call this on each poll. The Orchestrator updates `lastSeenAt` and periodically marks agents as LOST if they haven't heartbeat within the threshold. Also includes stale run recovery for zombie RUNNING runs.

### Implementation Order Note

> **This ticket must be implemented after** P2-002 (auth), P2-009 (artifact download), P2-003 (polling endpoint), and P2-010 (step reporting + run completion). The heartbeat endpoint must exist before the agent polling service sends heartbeats. See the implementation order section at the top of this document.

### Tasks

- [ ] Create `POST /api/agents/{agentId}/heartbeat` endpoint (with `[AgentAuth]`)
- [ ] Update `AgentNode.LastSeenAt` to current UTC time
- [ ] If agent was LOST, transition status based on state machine (see I2 below):
  - If agent had an `AssignedWorkloadId` → transition from `LOST` to `WORKLOAD_ASSIGNED`
  - If agent had no `AssignedWorkloadId` → transition from `LOST` to `REGISTERED`
  - This matches the MVP Plan Section 12 state machine
- [ ] Create `ILostAgentDetectionService` or use a background service that periodically checks for LOST agents
- [ ] LOST detection: use the agent's per-agent `PollingIntervalSeconds` (stored on `AgentNode` from enrollment, per P1-002) for threshold calculation
  - Threshold = `agent.PollingIntervalSeconds * 3` (3 missed heartbeats)
  - Note: `PollingIntervalSeconds` was added to `AgentNode` in the P1-002 Phase 1 fix
- [ ] Run LOST detection on a timer (configurable interval, default: 30 seconds)
- [ ] If agent is LOST while it has a RUNNING `WorkloadRun`, mark the run as FAILED
- [ ] Add stale run recovery: identify and fail zombie RUNNING runs (see G2 below)

### I2: LOST Agent State Machine Clarification

When a LOST agent sends a heartbeat and is recovered:

| Agent State Before LOST | On Recovery |
|---|---|
| `REGISTERED` (no assigned workload) | LOST → `REGISTERED` |
| `WORKLOAD_ASSIGNED` (has assigned workload) | LOST → `WORKLOAD_ASSIGNED` |

This matches the MVP Plan Section 12 state machine. The key check is whether `AssignedWorkloadId` is non-null.

### I3: AgentNodeStatus Enum Note

The `WORKLOAD_ASSIGNED` status was added to the `AgentNodeStatus` enum in P1-002 (Phase 1 fix, GAP-1). All Phase 2 code should use the updated enum that includes:

- `UNREGISTERED`
- `REGISTERED`
- `WORKLOAD_ASSIGNED`
- `LOST`

### G2: Stale Run Recovery

In addition to LOST agent detection, implement recovery for zombie RUNNING runs — runs that have not received a step update for a configurable timeout.

- A background check (can run in the same `LostAgentDetectionService` or separately) that identifies `RUNNING` runs with no `WorkloadRunStep` updates for more than `RunTimeoutMinutes` (default: 30 minutes)
- Mark stale runs as `FAILED` with message: `"Run timed out — no step updates received"`
- If the associated agent is stuck in `RUNNING` or `WORKLOAD_ASSIGNED` state (but not `LOST`), reset it to `REGISTERED` or `WORKLOAD_ASSIGNED` based on its `AssignedWorkloadId`
- Add `RunTimeoutMinutes` configuration option (default: 30) to `AgentOptions` or `OrchestratorOptions`

### Code Example — Heartbeat Endpoint

```csharp
// Controllers/AgentsController.cs (heartbeat)
[HttpPost("{agentId}/heartbeat")]
[AgentAuth]
public async Task<IActionResult> Heartbeat(string agentId)
{
    var authedAgentId = HttpContext.Items["AgentId"]!.ToString();
    if (authedAgentId != agentId) return Forbid();

    var agent = await _agentService.HeartbeatAsync(agentId);
    if (agent == null) return NotFound();

    return Ok(new { status = agent.Status.ToString() });
}

// Services/AgentService.cs
public async Task<AgentNode?> HeartbeatAsync(string agentId)
{
    var agent = await _db.AgentNodes.FirstOrDefaultAsync(a => a.AgentId == agentId);
    if (agent == null) return null;

    agent.LastSeenAt = DateTime.UtcNow;

    // I2: LOST recovery state machine — transition based on AssignedWorkloadId
    if (agent.Status == AgentNodeStatus.LOST)
    {
        agent.Status = agent.AssignedWorkloadId != null
            ? AgentNodeStatus.WORKLOAD_ASSIGNED
            : AgentNodeStatus.REGISTERED;

        _logger.LogInformation("Agent {AgentId} recovered from LOST → {Status}",
            agent.AgentId, agent.Status);
    }

    await _db.SaveChangesAsync();
    return agent;
}
```

### Code Example — LOST Detection Background Service

```csharp
// Services/LostAgentDetectionService.cs
public class LostAgentDetectionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LostAgentDetectionService> _logger;
    private readonly AgentOptions _options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // G1 + B4: Use per-agent PollingIntervalSeconds for threshold
            // Each agent's threshold = its own PollingIntervalSeconds * 3
            var activeAgents = await db.AgentNodes
                .Where(a => a.Status != AgentNodeStatus.UNREGISTERED)
                .ToListAsync();

            foreach (var agent in activeAgents)
            {
                var threshold = TimeSpan.FromSeconds(agent.PollingIntervalSeconds * 3);

                if (agent.LastSeenAt < DateTime.UtcNow - threshold)
                {
                    if (agent.Status != AgentNodeStatus.LOST)
                    {
                        agent.Status = AgentNodeStatus.LOST;
                        _logger.LogWarning("Agent {AgentId} marked as LOST (last seen {LastSeen}, threshold {Threshold}s)",
                            agent.AgentId, agent.LastSeenAt, threshold.TotalSeconds);
                    }
                }
            }

            // Mark any RUNNING runs for LOST agents as FAILED
            var lostAgentIds = activeAgents
                .Where(a => a.Status == AgentNodeStatus.LOST)
                .Select(a => a.AgentId)
                .ToList();

            var stuckRuns = await db.WorkloadRuns
                .Where(r => lostAgentIds.Contains(r.AgentId) && r.Status == WorkloadRunStatus.RUNNING)
                .ToListAsync();

            foreach (var run in stuckRuns)
            {
                run.Status = WorkloadRunStatus.FAILED;
                run.CompletedAt = DateTime.UtcNow;
                run.ErrorMessage = "Agent marked as LOST — run failed";
            }

            // G2: Stale Run Recovery — fail zombie RUNNING runs with no step updates
            var staleCutoff = DateTime.UtcNow.AddMinutes(-_options.RunTimeoutMinutes);
            var staleRuns = await db.WorkloadRuns
                .Where(r => r.Status == WorkloadRunStatus.RUNNING
                         && r.UpdatedAt < staleCutoff)
                .ToListAsync();

            foreach (var run in staleRuns)
            {
                run.Status = WorkloadRunStatus.FAILED;
                run.CompletedAt = DateTime.UtcNow;
                run.ErrorMessage = "Run timed out — no step updates received";
                _logger.LogWarning("Run {RunId} marked as FAILED — stale for {Timeout} minutes",
                    run.Id, _options.RunTimeoutMinutes);

                // Reset agent if stuck
                var agent = await db.AgentNodes.FirstOrDefaultAsync(a => a.AgentId == run.AgentId);
                // Agent might already be LOST — handled above. Otherwise, no-op.
            }

            await db.SaveChangesAsync();

            // M5-M6: Use configurable interval instead of hardcoded value
            await Task.Delay(TimeSpan.FromSeconds(_options.LostDetectionIntervalSeconds), stoppingToken);
        }
    }
}
```

### Configuration

```csharp
// Options/AgentOptions.cs
public class AgentOptions
{
    public const string SectionName = "Agent";

    // G1: Per-agent PollingIntervalSeconds is stored on AgentNode (from P1-002 enrollment)
    // This default is used only for agents without a stored value (shouldn't happen after P1-002)
    public int DefaultPollingIntervalSeconds { get; set; } = 30;

    // B4: LOST threshold multiplier — 3 missed heartbeats
    // Actual threshold = agent.PollingIntervalSeconds * LostThresholdMultiplier
    public int LostThresholdMultiplier { get; set; } = 3;

    // G2: Stale run timeout in minutes — fail RUNNING runs with no updates after this
    public int RunTimeoutMinutes { get; set; } = 30;

    // M5-M6: Configurable LOST detection scan interval
    public int LostDetectionIntervalSeconds { get; set; } = 30;
}
```

### Acceptance Criteria

- [ ] `POST /api/agents/{agentId}/heartbeat` with valid auth updates `lastSeenAt`
- [ ] LOST agent with `AssignedWorkloadId` that resumes heartbeat transitions to `WORKLOAD_ASSIGNED`
- [ ] LOST agent without `AssignedWorkloadId` that resumes heartbeat transitions to `REGISTERED`
- [ ] LOST detection marks agents as `LOST` when `lastSeenAt` exceeds per-agent threshold (`agent.PollingIntervalSeconds * 3`)
- [ ] Per-agent `PollingIntervalSeconds` (from enrollment, stored on `AgentNode`) is used for threshold — not a global default
- [ ] RUNNING `WorkloadRun` for a LOST agent is marked as `FAILED`
- [ ] LOST detection runs on configurable interval (default: 30 seconds)
- [ ] Stale run recovery: RUNNING runs with no step updates for `RunTimeoutMinutes` are marked as FAILED
- [ ] Stale runs get error message: "Run timed out — no step updates received"
- [ ] Agent auth required on heartbeat endpoint

### Verification Steps

1. Enroll an agent — note the `PollingIntervalSeconds` stored on `AgentNode`
2. Stop the agent service (no heartbeats)
3. Wait beyond threshold (`PollingIntervalSeconds * 3`) or adjust threshold for testing
4. Check DB: agent status is `LOST`
5. Restart agent — next heartbeat transitions agent back to `REGISTERED` (if no workload) or `WORKLOAD_ASSIGNED` (if workload assigned)
6. Check DB: agent status and `lastSeenAt` are updated
7. Dispatch a run, stop agent during execution — run should be marked `FAILED` after LOST detection
8. Simulate a stale run: create a RUNNING run with `UpdatedAt` set > 30 minutes ago — verify stale recovery marks it as FAILED
9. Verify LOST detection uses each agent's individual `PollingIntervalSeconds`, not a global default

---

## TICKET P2-005: Pre-Check Dispatch (Orchestrator)

**MVP Plan Ref:** Section 10 (Pre-Checks)  
**Depends on:** P2-003, P1-011

### Description

Implement the Orchestrator-side logic for dispatching pre-check tasks. When an admin triggers a pre-check for an agent + workload, the Orchestrator creates a `PRE_CHECK` WorkloadRun and makes it available for the agent to pick up on next poll. Also implement the UI-facing endpoint for triggering pre-checks.

### Tasks

- [ ] Add `POST /api/runs` endpoint overload for creating `PRE_CHECK` runs
- [ ] Request DTO: `{ agentId, workloadId, workloadVersion, mode: "PRE_CHECK" }`
- [ ] Validate: agent exists and is not UNREGISTERED, workload + version exists
- [ ] Validate: no other PENDING/RUNNING run for this agent (one at a time rule)
- [ ] Create `WorkloadRun` with `mode = PRE_CHECK`, `status = PENDING`
- [ ] Create `WorkloadRunSteps` entries for each package in the workload (action = DETECT, status = PENDING)
- [ ] Return run details including runId for UI tracking
- [ ] Add `GET /api/runs/{runId}` endpoint for UI to check run status
- [ ] Add `GET /api/agents/{agentId}/runs` endpoint for listing agent's run history
- [ ] Ensure `WorkloadPackage` queries filter by `WorkloadVersion` (I5)

### Code Example — Create Pre-Check Run

```csharp
// Services/RunService.cs
public async Task<WorkloadRunResponse> CreatePreCheckRunAsync(
    string agentId, string workloadId, string workloadVersion)
{
    var agent = await _db.AgentNodes.FirstOrDefaultAsync(a => a.AgentId == agentId);
    if (agent == null || agent.Status == AgentNodeStatus.UNREGISTERED)
        throw new InvalidOperationException("Agent not found or unregistered");

    if (await _db.WorkloadRuns.AnyAsync(r =>
        r.AgentId == agentId &&
        (r.Status == WorkloadRunStatus.PENDING || r.Status == WorkloadRunStatus.RUNNING)))
    {
        throw new InvalidOperationException("Agent already has an active run");
    }

    var workload = await _db.Workloads
        .FirstOrDefaultAsync(w => w.WorkloadId == workloadId && w.Version == workloadVersion);
    if (workload == null)
        throw new InvalidOperationException($"Workload {workloadId} v{workloadVersion} not found");

    var run = new WorkloadRun
    {
        AgentId = agentId,
        WorkloadId = workloadId,
        WorkloadVersion = workloadVersion,
        Mode = WorkloadRunMode.PRE_CHECK,
        Status = WorkloadRunStatus.PENDING,
        CreatedAt = DateTime.UtcNow
    };

    _db.WorkloadRuns.Add(run);

    // I5: Filter WorkloadPackages by WorkloadVersion
    var packages = await _db.WorkloadPackages
        .Where(wp => wp.WorkloadId == workloadId && wp.WorkloadVersion == workloadVersion)
        .ToListAsync();

    foreach (var pkg in packages)
    {
        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id,
            PackageId = pkg.PackageId,
            PackageVersion = pkg.PackageVersion,
            StepOrder = 0,
            Action = WorkloadRunStepAction.DETECT,
            Status = WorkloadRunStepStatus.PENDING
        });
    }

    await _db.SaveChangesAsync();
    return MapToResponse(run);
}
```

### Acceptance Criteria

- [ ] `POST /api/runs` with `mode: "PRE_CHECK"` creates a PENDING run with all DETECT steps
- [ ] Validation: agent must exist and not be UNREGISTERED
- [ ] Validation: workload + version must exist
- [ ] One active run rule: reject if agent already has PENDING/RUNNING run
- [ ] `GET /api/runs/{runId}` returns run details including steps
- [ ] `GET /api/agents/{agentId}/runs` returns agent's run history
- [ ] Pre-check run includes DETECT steps for each workload package
- [ ] `WorkloadPackage` queries correctly filter by `WorkloadVersion`

### Verification Steps

1. Create a workload with 3 packages (ensure `WorkloadVersion` is set on each)
2. Enroll an agent
3. `POST /api/runs` with `{ agentId, workloadId, version, mode: "PRE_CHECK" }` → 200, run created
4. Check DB: `WorkloadRun` with `mode=PRE_CHECK`, `status=PENDING`, 3 `WorkloadRunStep` entries
5. `GET /api/runs/{runId}` → 200 with run details and steps
6. Create another run for same agent → 409 (one at a time)
7. Verify `WorkloadPackage` query includes version filter — check SQL logs

---

## TICKET P2-006: Agent Detection Execution (registry + filePath)

**MVP Plan Ref:** Section 10 (Pre-Checks), Section 8 (Detection: registry + filePath)  
**Depends on:** P2-003, P2-010

### Description

Implement detection logic in the Agent. When the Agent receives a PRE_CHECK task, it runs detection for each package using the manifest's `detection` definition. Supports `registry` and `filePath` types. Reports results back to the Orchestrator via the step reporting endpoint (P2-010).

### Tasks

- [ ] Create `IDetectionService` interface and `DetectionService` implementation
- [ ] Implement `registry` detection:
  - Parse `HKLM` / `HKCU` hive prefix from `key`
  - Use `Microsoft.Win32.Registry.GetValue()` or `Registry.LocalMachine.OpenSubKey()`
  - Read `valueName` from the registry key
  - Compare `expectedValue` (case-insensitive for version strings)
  - Return detected: `true/false`, detected version if available, message
- [ ] Implement `filePath` detection:
  - Check `System.IO.File.Exists(key)` or `Directory.Exists(key)`
  - If `valueName` is `null`: return detected based on existence only
  - If `valueName` is `"FileVersion"`: read `System.Diagnostics.FileVersionInfo.GetVersionInfo(key).FileVersion`
  - Compare `expectedValue` with detected version
  - Return detected: `true/false`, detected version, message
- [ ] Create step reporting method in `AgentPollingService`:
  - `POST /api/runs/{runId}/steps` for each detection result (see P2-010)
- [ ] Create run completion method:
  - `POST /api/runs/{runId}/complete` with completion status (see P2-010)
- [ ] Handle detection errors gracefully (registry key not found, file not accessible, etc.)

### Code Example — Registry Detection

```csharp
// Services/DetectionService.cs
public class DetectionService : IDetectionService
{
    private readonly ILogger<DetectionService> _logger;

    public DetectionResult Detect(DetectionRule rule)
    {
        return rule.Type switch
        {
            "registry" => DetectRegistry(rule),
            "filePath" => DetectFilePath(rule),
            "wmi" => throw new NotSupportedException("WMI detection is not supported in MVP"),
            _ => throw new ArgumentException($"Unknown detection type: {rule.Type}")
        };
    }

    private DetectionResult DetectRegistry(DetectionRule rule)
    {
        try
        {
            var (hive, subKeyPath) = ParseRegistryKey(rule.Key);
            using var rootKey = hive == RegistryHive.LocalMachine
                ? Registry.LocalMachine
                : Registry.CurrentUser;

            using var subKey = rootKey.OpenSubKey(subKeyPath);
            if (subKey == null)
            {
                return new DetectionResult(Detected: false, Message: $"Registry key not found: {rule.Key}");
            }

            var value = subKey.GetValue(rule.ValueName);
            if (value == null)
            {
                return new DetectionResult(Detected: false, Message: $"Registry value not found: {rule.ValueName}");
            }

            var detectedVersion = value.ToString();
            var matches = string.Equals(detectedVersion, rule.ExpectedValue, StringComparison.OrdinalIgnoreCase);

            return new DetectionResult
            {
                Detected = matches,
                DetectedVersion = detectedVersion,
                Message = matches
                    ? $"Detected version {detectedVersion}"
                    : $"Version mismatch: expected {rule.ExpectedValue}, found {detectedVersion}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registry detection failed for {Key}", rule.Key);
            return new DetectionResult(Detected: false, Message: $"Detection error: {ex.Message}");
        }
    }

    private DetectionResult DetectFilePath(DetectionRule rule)
    {
        try
        {
            if (!File.Exists(rule.Key) && !Directory.Exists(rule.Key))
            {
                return new DetectionResult(Detected: false, Message: $"Path not found: {rule.Key}");
            }

            if (rule.ValueName == null)
            {
                return new DetectionResult(Detected: true, Message: $"Path exists: {rule.Key}");
            }

            if (rule.ValueName == "FileVersion")
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(rule.Key);
                var detectedVersion = versionInfo.FileVersion;
                var matches = string.Equals(detectedVersion, rule.ExpectedValue, StringComparison.OrdinalIgnoreCase);

                return new DetectionResult
                {
                    Detected = matches,
                    DetectedVersion = detectedVersion,
                    Message = matches
                        ? $"File version {detectedVersion} matches"
                        : $"Version mismatch: expected {rule.ExpectedValue}, found {detectedVersion}"
                };
            }

            return new DetectionResult(Detected: false, Message: $"Unsupported valueName: {rule.ValueName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FilePath detection failed for {Key}", rule.Key);
            return new DetectionResult(Detected: false, Message: $"Detection error: {ex.Message}");
        }
    }
}
```

### Code Example — Step Reporting

```csharp
// In AgentPollingService — after running detection for each package
private async Task ReportStepAsync(int runId, string packageId, int stepOrder, string stepType, int exitCode, string? stdout, string? stderr)
{
    var client = _httpClientFactory.CreateClient("Orchestrator");
    client.BaseAddress = GetAbsoluteUrl("/");
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", _config.AgentSecret);

    var payload = new
    {
        packageId,
        stepType,
        stepOrder,
        exitCode,
        stdout,
        stderr,
        timestamp = DateTime.UtcNow.ToString("O")
    };

    var response = await client.PostAsJsonAsync($"/api/runs/{runId}/steps", payload);
    response.EnsureSuccessStatusCode();
}

private async Task CompleteRunAsync(int runId, string status, List<DetectedPackage> detectedPackages, string? message = null)
{
    var client = _httpClientFactory.CreateClient("Orchestrator");
    client.BaseAddress = GetAbsoluteUrl("/");
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", _config.AgentSecret);

    var payload = new
    {
        status,
        detectedPackages,
        message
    };

    var response = await client.PostAsJsonAsync($"/api/runs/{runId}/complete", payload);
    response.EnsureSuccessStatusCode();
}
```

### Acceptance Criteria

- [ ] `registry` detection correctly reads HKLM/HKCU registry values
- [ ] `registry` detection matches `expectedValue` against actual (case-insensitive)
- [ ] `registry` detection returns `Detected: false` when key or value doesn't exist
- [ ] `filePath` detection with `valueName: null` checks existence only
- [ ] `filePath` detection with `valueName: "FileVersion"` reads PE metadata
- [ ] `filePath` detection handles missing files/directories gracefully
- [ ] Agent reports each detection result via `POST /api/runs/{runId}/steps`
- [ ] Agent reports run completion via `POST /api/runs/{runId}/complete`
- [ ] Detection errors (permissions, missing keys) are caught and reported as failures, not crashes

### Verification Steps

1. Create a test registry key under `HKLM\SOFTWARE\TestApp` with `DisplayVersion = "1.0.0"`
2. Enroll agent, create workload referencing test registry key
3. Dispatch PRE_CHECK task → agent detects registry value → reports DETECT step as SUCCESS
4. Change expected version to "2.0.0" → agent detects version mismatch → DETECT step reports mismatch
5. Test filePath detection with an existing file and `valueName: null` → detected
6. Test filePath detection with `valueName: "FileVersion"` on a real .exe → version detected
7. Test detection of non-existent path → `Detected: false`

---

## TICKET P2-007: DB Reconciliation Logic

**MVP Plan Ref:** Section 10 (DB Reconciliation — "Agent reality is the source of truth")  
**Depends on:** P2-005, P2-006

### Description

Implement the reconciliation logic that updates `AgentPackages` records to match what the Agent actually reported during pre-check. Agent reality is the source of truth — the DB follows it.

### G5: AssignedWorkloadVersion Update Timing

> **Note:** `AgentNode.AssignedWorkloadVersion` is updated in **Phase 3** after a successful INSTALL run, NOT during pre-check or Phase 2. Phase 2 only sets `AssignedWorkloadId` during enrollment. The version field remains null until Phase 3 implements the full install flow.

### Tasks

- [ ] Create `IReconciliationService` interface and `ReconciliationService` implementation
- [ ] Implement `ReconcileAsync(agentId, detectedPackages)`:
  1. Get all existing `AgentPackages` for this agent from DB
  2. For each detected package:
     - If package exists in DB: update `InstalledVersion`, `DetectedAt`, `Status = INSTALLED`
     - If package not in DB: insert new `AgentPackages` record with `Status = INSTALLED`
  3. For packages in DB but NOT detected by agent: set `Status = MISSING`
- [ ] Call reconciliation from `RunService` after pre-check run completion
- [ ] **Do NOT update `AgentNode.AssignedWorkloadVersion`** during pre-check — only after successful INSTALL run (Phase 3)
- [ ] Update `AgentNode.AssignedWorkloadId` / `AssignedWorkloadVersion` after successful INSTALL run only (Phase 3)
- [ ] Add logging for reconciliation results

### Code Example — Reconciliation Service

```csharp
// Services/ReconciliationService.cs
public class ReconciliationService : IReconciliationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReconciliationService> _logger;

    public async Task ReconcileAsync(string agentId, List<DetectedPackage> detectedPackages)
    {
        var existingPackages = await _db.AgentPackages
            .Where(ap => ap.AgentId == agentId)
            .ToListAsync();

        var detectedIds = new HashSet<string>(detectedPackages.Select(p => p.PackageId));

        foreach (var detected in detectedPackages)
        {
            var existing = existingPackages
                .FirstOrDefault(ap => ap.AgentId == agentId && ap.PackageId == detected.PackageId);

            if (existing != null)
            {
                existing.InstalledVersion = detected.Version ?? existing.InstalledVersion;
                existing.DetectedAt = DateTime.UtcNow;
                existing.Status = detected.Detected
                    ? AgentPackageStatus.INSTALLED
                    : AgentPackageStatus.MISSING;
            }
            else if (detected.Detected)
            {
                _db.AgentPackages.Add(new AgentPackage
                {
                    AgentId = agentId,
                    PackageId = detected.PackageId,
                    InstalledVersion = detected.Version ?? "unknown",
                    DetectedAt = DateTime.UtcNow,
                    Status = AgentPackageStatus.INSTALLED
                });
            }
        }

        foreach (var existing in existingPackages.Where(ap => !detectedIds.Contains(ap.PackageId)))
        {
            if (existing.Status == AgentPackageStatus.INSTALLED)
            {
                existing.Status = AgentPackageStatus.MISSING;
                existing.DetectedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Reconciliation complete for agent {AgentId}: {Count} packages processed",
            agentId, detectedPackages.Count);
    }
}
```

### Acceptance Criteria

- [ ] Newly detected packages are inserted into `AgentPackages` with `Status = INSTALLED`
- [ ] Existing detected packages have version and `DetectedAt` updated
- [ ] Packages in DB but not detected by agent are marked `Status = MISSING`
- [ ] Package detected but agent reports `detected: false` → `Status = MISSING`
- [ ] Reconciliation is called automatically after pre-check run completion
- [ ] `AgentNode.AssignedWorkloadId` is NOT updated during pre-check (only during INSTALL completion)
- [ ] `AgentNode.AssignedWorkloadVersion` is NOT updated during Phase 2 at all (Phase 3 responsibility)

### Verification Steps

1. Enroll agent, dispatch PRE_CHECK for a workload with 3 packages
2. Agent detects 2 of 3 packages → reconciliation inserts 2 as INSTALLED
3. Run PRE_CHECK again → agent now detects all 3 → reconciliation inserts 3rd as INSTALLED
4. Uninstall one package, run PRE_CHECK → that package marked as MISSING
5. Verify `AgentPackages` table matches agent's actual state after each pre-check
6. Verify `AssignedWorkloadVersion` is NOT changed during any pre-check flow

---

## TICKET P2-008: Delta Summary Computation

**MVP Plan Ref:** Section 10 (Delta summary output), Section 13 (Workload Version Semantics)  
**Depends on:** P2-007

### Description

Compute the delta summary that compares an agent's installed state against a target workload's required packages. Returns per-package status: MATCHES, MISSING, VERSION_DRIFT, AHEAD, ORPHANED.

### Tasks

- [ ] Create `IDeltaService` interface and `DeltaService` implementation
- [ ] Implement `ComputeDeltaAsync(agentId, workloadId, workloadVersion)`:
  1. Get all `AgentPackages` for this agent (from DB, post-reconciliation)
  2. Get all `WorkloadPackages` for the target workload version
  3. Get agent's current assigned workload (if any) for orphan detection
  4. For each package in the target workload:
     - Not in `AgentPackages` → `MISSING`
     - In `AgentPackages` with same version → `MATCHES`
     - In `AgentPackages` with older version → `VERSION_DRIFT`
     - In `AgentPackages` with newer version → `AHEAD`
  5. For each package in agent's CURRENT (old) workload but NOT in new workload → `ORPHANED`
  6. Return delta summary with per-package details
- [ ] Create `GET /api/agents/{agentId}/delta?workloadId=X&workloadVersion=Y` endpoint
- [ ] Create response model: list of `DeltaPackage` with status, packageId, installedVersion, requiredVersion
- [ ] Use `Version` class for proper semantic version comparison (not string comparison)
- [ ] Add fallback for non-standard version strings (B3)
- [ ] Ensure `WorkloadPackage` queries filter by `WorkloadVersion` (I5)
- [ ] M1: Ensure migration adds `WorkloadVersion` column to `WorkloadPackage`

### Code Example — Delta Computation

```csharp
// Services/DeltaService.cs
public class DeltaService : IDeltaService
{
    private readonly AppDbContext _db;

    public async Task<DeltaSummary> ComputeDeltaAsync(
        string agentId, string workloadId, string workloadVersion)
    {
        var agentPackages = await _db.AgentPackages
            .Where(ap => ap.AgentId == agentId)
            .ToDictionaryAsync(ap => ap.PackageId, ap => ap);

        // I5: Filter WorkloadPackages by WorkloadVersion
        var targetPackages = await _db.WorkloadPackages
            .Where(wp => wp.WorkloadId == workloadId && wp.WorkloadVersion == workloadVersion)
            .ToListAsync();

        var deltaItems = new List<DeltaPackage>();

        foreach (var target in targetPackages)
        {
            agentPackages.TryGetValue(target.PackageId, out var installed);

            var status = (installed?.Status) switch
            {
                null or AgentPackageStatus.MISSING => DeltaStatus.MISSING,
                AgentPackageStatus.INSTALLED when
                    installed.InstalledVersion == target.PackageVersion => DeltaStatus.MATCHES,
                AgentPackageStatus.INSTALLED when
                    VersionComparison.CompareVersions(installed.InstalledVersion, target.PackageVersion) < 0 => DeltaStatus.VERSION_DRIFT,
                AgentPackageStatus.INSTALLED when
                    VersionComparison.CompareVersions(installed.InstalledVersion, target.PackageVersion) > 0 => DeltaStatus.AHEAD,
                _ => DeltaStatus.MISSING
            };

            deltaItems.Add(new DeltaPackage
            {
                PackageId = target.PackageId,
                Status = status,
                InstalledVersion = installed?.InstalledVersion,
                RequiredVersion = target.PackageVersion
            });
        }

        // Orphan detection: packages in agent's current workload not in target
        var agent = await _db.AgentNodes.FirstAsync(a => a.AgentId == agentId);
        if (!string.IsNullOrEmpty(agent.AssignedWorkloadId))
        {
            // I5 + M1: Filter by WorkloadVersion for current workload
            var currentWorkloadVersion = agent.AssignedWorkloadVersion ?? "latest";
            var currentPackages = await _db.WorkloadPackages
                .Where(wp => wp.WorkloadId == agent.AssignedWorkloadId && wp.WorkloadVersion == currentWorkloadVersion)
                .ToListAsync();

            var targetIds = new HashSet<string>(targetPackages.Select(p => p.PackageId));
            foreach (var current in currentPackages.Where(p => !targetIds.Contains(p.PackageId)))
            {
                var installed = agentPackages.GetValueOrDefault(current.PackageId);
                if (installed?.Status == AgentPackageStatus.INSTALLED)
                {
                    deltaItems.Add(new DeltaPackage
                    {
                        PackageId = current.PackageId,
                        Status = DeltaStatus.ORPHANED,
                        InstalledVersion = installed.InstalledVersion,
                        RequiredVersion = null
                    });
                }
            }
        }

        return new DeltaSummary
        {
            AgentId = agentId,
            WorkloadId = workloadId,
            WorkloadVersion = workloadVersion,
            Packages = deltaItems
        };
    }
}
```

### Acceptance Criteria

- [ ] Delta summary correctly computes `MISSING` for packages not installed on agent
- [ ] Delta summary correctly computes `MATCHES` for packages with exact version match
- [ ] Delta summary correctly computes `VERSION_DRIFT` for packages with older version
- [ ] Delta summary correctly computes `AHEAD` for packages with newer version
- [ ] Delta summary correctly computes `ORPHANED` for packages in old workload but not in target
- [ ] Version comparison handles standard versions via `Version.TryParse` and falls back to lexicographic comparison for non-standard versions (e.g., "2.0.1-beta")
- [ ] `GET /api/agents/{agentId}/delta` endpoint returns delta summary
- [ ] Orphan detection only applies when agent has an assigned workload (not for first-time install)
- [ ] `WorkloadPackage` queries correctly filter by `WorkloadVersion`

### Verification Steps

1. Enroll agent, install workload v1 with packages A, B, C (all versions matching)
2. Create workload v2 with packages A (same), B (newer), D (new)
3. Compute delta for v2 against agent → A: MATCHES, B: VERSION_DRIFT, C: ORPHANED, D: MISSING
4. For agent with no assigned workload → no ORPHANED packages in delta
5. For package with newer version than required → AHEAD status returned
6. Test version ordering: "1.0.0" < "1.1.0" < "2.0.0" < "2.0.1"
7. Test non-standard version: "2.0.1-beta" compared lexicographically (fallback)
8. Verify `WorkloadPackage` queries include `WorkloadVersion` filter in SQL

---

## TICKET P2-009: Artifact Download Endpoints

**MVP Plan Ref:** Section 8 (API Contract — artifact download), P1-009 (artifact storage)  
**Depends on:** P2-002, P1-009

### Description

Implement API endpoints for agents to download artifact binaries and manifests. These endpoints stream artifact files to the agent and support Range headers for large artifacts.

### Tasks

- [ ] Create `GET /api/artifacts/{artifactId}/download` endpoint (with `[AgentAuth]`)
  - Stream the artifact file from storage (local filesystem or blob store, per P1-009)
  - Set `Content-Disposition: attachment` header
  - Set `Content-Type` based on file extension
  - Support `Range` headers for resumable/large artifact downloads
- [ ] Create `GET /api/artifacts/{artifactId}/manifest` endpoint (with `[AgentAuth]`)
  - Return the artifact's manifest JSON (package list, hashes, metadata)
  - Return 404 if artifact or manifest not found
- [ ] Create `ArtifactsController` with both endpoints
- [ ] Validate that the authenticated agent has access to the requested artifact (belongs to their assigned workload)
- [ ] Add null check for artifact not found (return 404)
- [ ] Add logging for download attempts (success and failure)

### Code Example — Artifact Controller

```csharp
// Controllers/ArtifactsController.cs
[ApiController]
[Route("api/artifacts")]
public class ArtifactsController : ControllerBase
{
    private readonly IArtifactService _artifactService;
    private readonly ILogger<ArtifactsController> _logger;

    public ArtifactsController(
        IArtifactService artifactService,
        ILogger<ArtifactsController> logger)
    {
        _artifactService = artifactService;
        _logger = logger;
    }

    [HttpGet("{artifactId}/download")]
    [AgentAuth]
    public async Task<IActionResult> Download(Guid artifactId)
    {
        var artifact = await _artifactService.GetByIdAsync(artifactId);
        if (artifact == null)
        {
            _logger.LogWarning("Artifact {ArtifactId} not found", artifactId);
            return NotFound();
        }

        var filePath = _artifactService.GetStoragePath(artifact);
        if (!System.IO.File.Exists(filePath))
        {
            _logger.LogWarning("Artifact file not found at {FilePath}", filePath);
            return NotFound();
        }

        var contentType = GetContentType(filePath);
        var fileName = Path.GetFileName(filePath);

        // Support Range headers for large artifact downloads
        return PhysicalFile(filePath, contentType, fileName, enableRangeProcessing: true);
    }

    [HttpGet("{artifactId}/manifest")]
    [AgentAuth]
    public async Task<IActionResult> Manifest(Guid artifactId)
    {
        var artifact = await _artifactService.GetByIdAsync(artifactId);
        if (artifact == null)
        {
            _logger.LogWarning("Artifact {ArtifactId} manifest not found", artifactId);
            return NotFound();
        }

        var manifest = _artifactService.GetManifest(artifact);
        if (manifest == null)
        {
            return NotFound();
        }

        return Ok(manifest);
    }

    private static string GetContentType(string filePath) => Path.GetExtension(filePath) switch
    {
        ".msi" => "application/x-msi",
        ".exe" => "application/octet-stream",
        ".zip" => "application/zip",
        ".msix" => "application/msix",
        _ => "application/octet-stream"
    };
}
```

### Code Example — Physical File with Content-Disposition

> The `PhysicalFile` result with `enableRangeProcessing: true` automatically handles:
> - `Content-Disposition: attachment; filename="<name>"` header
> - `Range` header processing for partial content (206 responses)
> - `Content-Length` header
> - Resume support for interrupted downloads

### Acceptance Criteria

- [ ] `GET /api/artifacts/{id}/download` streams the artifact file to the agent
- [ ] `GET /api/artifacts/{id}/manifest` returns the artifact's manifest JSON
- [ ] Both endpoints require agent authentication (`[AgentAuth]`)
- [ ] `Content-Disposition: attachment` header is set on download responses
- [ ] `Range` headers are supported for large artifact downloads (resumable downloads)
- [ ] Returns 404 if artifact ID is not found
- [ ] Returns 404 if artifact file is missing from storage
- [ ] Returns 404 if manifest is not available
- [ ] Agent auth middleware validates the requesting agent has access to the artifact
- [ ] Download attempts are logged (success and failure)

### Verification Steps

1. Enroll agent, create workload with artifacts
2. `GET /api/artifacts/{id}/download` with valid auth → 200, file streams with `Content-Disposition: attachment`
3. `GET /api/artifacts/{id}/download` without auth → 401
4. `GET /api/artifacts/{id}/download` with `Range: bytes=0-1023` header → 206 Partial Content
5. `GET /api/artifacts/{id}/manifest` with valid auth → 200, manifest JSON returned
6. `GET /api/artifacts/{nonexistent-id}/download` → 404
7. `GET /api/artifacts/{id}/download` → verify `Content-Type` matches file extension

---

## TICKET P2-010: Step Reporting & Run Completion Endpoints

**MVP Plan Ref:** Section 8 (Agent Communication Model — step results), Section 10 (pre-check flow)  
**Depends on:** P2-002, P2-003

### Description

Implement the API endpoints for agents to report step execution results and signal run completion. These are the endpoints the Agent calls after executing each detection/install step and when the entire run is done.

### Tasks

- [ ] Create `POST /api/runs/{runId}/steps` endpoint (with `[AgentAuth]`)
  - Request body: `{ "packageId": "...", "stepType": "DETECT|INSTALL|VERIFY", "stepOrder": 0, "exitCode": 0, "stdout": "...", "stderr": "...", "timestamp": "..." }`
  - Resolve `WorkloadRunStep` using `(runId, packageId, stepOrder)` — **NOT** just `(runId, stepType)` which is ambiguous for multi-command steps
  - Update step status: exitCode 0 → `SUCCESS`, non-zero → `FAILED`
  - Store stdout/stderr output
  - Update `WorkloadRunStep.UpdatedAt` timestamp (used by stale run detection in P2-004)
- [ ] Create `POST /api/runs/{runId}/complete` endpoint (with `[AgentAuth]`)
  - Request body: `{ "status": "SUCCESS|FAILED", "detectedPackages": [{ "packageId": "...", "version": "...", "detected": true }], "message": "..." }`
  - Transition `WorkloadRun` status to `SUCCESS` or `FAILED`
  - Set `CompletedAt` timestamp
  - For `PRE_CHECK` runs with `status: SUCCESS`, trigger reconciliation (P2-007)
  - Validate that the completing agent owns the run
- [ ] Both endpoints require agent authentication
- [ ] Add `ReportStepAsync` method to `RunService` with proper step resolution logic

### Code Example — Step Report Endpoint

```csharp
// Controllers/RunsController.cs
[HttpPost("{runId:int}/steps")]
[AgentAuth]
public async Task<IActionResult> ReportStep(int runId, [FromBody] StepReportRequest request)
{
    var agentId = HttpContext.Items["AgentId"]!.ToString();

    var run = await _runService.GetRunAsync(runId);
    if (run == null) return NotFound();
    if (run.AgentId != agentId) return Forbid();

    await _runService.ReportStepAsync(runId, request);
    return Ok();
}

[HttpPost("{runId:int}/complete")]
[AgentAuth]
public async Task<IActionResult> CompleteRun(int runId, [FromBody] RunCompleteRequest request)
{
    var agentId = HttpContext.Items["AgentId"]!.ToString();

    var run = await _runService.GetRunAsync(runId);
    if (run == null) return NotFound();
    if (run.AgentId != agentId) return Forbid();

    await _runService.CompleteRunAsync(runId, agentId, request);
    return Ok();
}
```

### Code Example — ReportStepAsync with Correct Step Resolution

```csharp
// Services/RunService.cs
public async Task ReportStepAsync(int runId, StepReportRequest request)
{
    // G4: Resolve step using (runId, packageId, stepOrder) — NOT (runId, stepType)
    // This is unambiguous even for multi-command steps within the same package
    var step = await _db.WorkloadRunSteps
        .FirstOrDefaultAsync(s =>
            s.RunId == runId &&
            s.PackageId == request.PackageId &&
            s.StepOrder == request.StepOrder);

    if (step == null)
    {
        _logger.LogWarning("Step not found for run {RunId}, package {PackageId}, order {StepOrder}",
            runId, request.PackageId, request.StepOrder);
        return;
    }

    step.ExitCode = request.ExitCode;
    step.Stdout = request.Stdout;
    step.Stderr = request.Stderr;
    step.Status = request.ExitCode == 0
        ? WorkloadRunStepStatus.SUCCESS
        : WorkloadRunStepStatus.FAILED;
    step.UpdatedAt = DateTime.UtcNow;

    // Also update the parent run's UpdatedAt for stale detection (P2-004 G2)
    var run = await _db.WorkloadRuns.FirstOrDefaultAsync(r => r.Id == runId);
    if (run != null)
    {
        run.UpdatedAt = DateTime.UtcNow;
    }

    await _db.SaveChangesAsync();
}

public async Task CompleteRunAsync(int runId, string agentId, RunCompleteRequest request)
{
    var run = await _db.WorkloadRuns.FirstOrDefaultAsync(r => r.Id == runId);
    if (run == null) return;

    run.Status = request.Status == "SUCCESS"
        ? WorkloadRunStatus.SUCCESS
        : WorkloadRunStatus.FAILED;
    run.CompletedAt = DateTime.UtcNow;
    run.ErrorMessage = request.Message;
    run.UpdatedAt = DateTime.UtcNow;

    await _db.SaveChangesAsync();

    // For successful PRE_CHECK runs, trigger reconciliation (P2-007)
    if (run.Mode == WorkloadRunMode.PRE_CHECK && request.Status == "SUCCESS")
    {
        await _reconciliationService.ReconcileAsync(agentId, request.DetectedPackages);
    }
}
```

### Code Example — Request DTOs

```csharp
// Models/Dto/StepReportRequest.cs
public class StepReportRequest
{
    public string PackageId { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty; // DETECT, INSTALL, VERIFY
    public int StepOrder { get; set; }
    public int ExitCode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public DateTime Timestamp { get; set; }
}

// Models/Dto/RunCompleteRequest.cs
public class RunCompleteRequest
{
    public string Status { get; set; } = string.Empty; // SUCCESS, FAILED
    public List<DetectedPackage>? DetectedPackages { get; set; }
    public string? Message { get; set; }
}

public class DetectedPackage
{
    public string PackageId { get; set; } = string.Empty;
    public string? Version { get; set; }
    public bool Detected { get; set; }
}
```

### Acceptance Criteria

- [ ] `POST /api/runs/{runId}/steps` accepts step execution results from the agent
- [ ] Step resolution uses `(runId, packageId, stepOrder)` — not `(runId, stepType)` — for unambiguous identification
- [ ] `exitCode: 0` maps to `SUCCESS` status; non-zero maps to `FAILED` status
- [ ] `POST /api/runs/{runId}/complete` accepts run completion from the agent
- [ ] `status: "SUCCESS"` maps to `WorkloadRunStatus.SUCCESS`; `"FAILED"` maps to `FAILED`
- [ ] Both endpoints require agent authentication
- [ ] Agent can only complete/report steps for their own runs (agentId validation)
- [ ] Step `UpdatedAt` timestamp is updated on each report (used for stale run detection)
- [ ] Parent `WorkloadRun.UpdatedAt` is updated on each step report (used for stale run detection by P2-004 G2)
- [ ] Successful PRE_CHECK completion triggers reconciliation
- [ ] Returns 404 if run not found, 403 if agent doesn't own the run

### Verification Steps

1. Create a PRE_CHECK run for an agent
2. Agent polls for next task → gets run details
3. Agent reports DETECT step: `POST /api/runs/{runId}/steps` with `{ packageId, stepType: "DETECT", stepOrder: 0, exitCode: 0 }` → 200
4. Check DB: `WorkloadRunStep` updated with `status=SUCCESS`, `exitCode=0`
5. Agent completes run: `POST /api/runs/{runId}/complete` with `{ status: "SUCCESS", detectedPackages: [...] }` → 200
6. Check DB: `WorkloadRun` status is `SUCCESS`, `CompletedAt` is set
7. Verify reconciliation was triggered (check `AgentPackages` updated)
8. Try reporting a step for a different agent's run → 403
9. Try reporting a step without auth → 401
10. Verify `WorkloadRun.UpdatedAt` is updated on step report (for stale detection)