# Phase 2 Review: Core Pipeline vs MVP Specification

## GAPS — MVP Requirements Not Covered

### G1: `pollingIntervalSeconds` Not Stored on AgentNode (Critical)

**MVP Plan Section 3** states the enrollment response includes `pollingIntervalSeconds` and Section 12 says LOST detection uses `pollingIntervalSeconds × LostThresholdMultiplier` where `pollingIntervalSeconds` comes from the agent's own value. P2-004 acknowledges this gap in its tasks ("Store `pollingIntervalSeconds` on `AgentNode` (add to enrollment flow from P1-006)") but:

- **P1-002's `AgentNode` model has no `PollingIntervalSeconds` field.** The schema (Section 7) also omits it from the `AgentNodes` table definition.
- **P1-006's `EnrollResponse` returns `pollingIntervalSeconds`** but P1-006's `EnrollAsync` never persists it to the `AgentNode` record.
- **P2-004's LOST detection** uses `_options.DefaultPollingIntervalSeconds` (global default) instead of the per-agent value, which contradicts the MVP Plan's explicit statement that each agent uses its own `pollingIntervalSeconds`.

**Impact:** LOST detection threshold will be wrong for any agent enrolled with a different polling interval. Orphaned field — the data never makes it into the DB.

**Fix:** Add `PollingIntervalSeconds` column to `AgentNodes`, store it at enrollment, and use per-agent threshold in `LostAgentDetectionService`.

### G2: No Run Timeout / Stale Run Recovery

P2-003 transitions `PENDING → RUNNING` immediately on poll. If the Agent crashes after receiving the task, the run stays `RUNNING` forever. P2-004 marks runs as `FAILED` only when the agent is marked `LOST`, which is indirect and delayed.

**Impact:** An agent that crashes and restarts within the LOST threshold (e.g., crashes then re-enrolls quickly) would leave a zombie `RUNNING` run. The "one active run per agent" constraint would block all future runs.

**Fix:** Either (a) add an explicit run timeout mechanism (`startedAt + timeout`), or (b) document that zombie runs are recovered only via LOST detection and make the recovery explicit in P2-004's LOST service.

### G3: Artifact Download Endpoints Not Implemented

The MVP Plan (Section 8) defines:
- `GET /api/artifacts/{artifactId}/download` — binary download
- `GET /api/artifacts/{artifactId}/manifest` — manifest JSON download

P2-003 returns relative URLs like `/api/artifacts/{id}/manifest` but no Phase 2 ticket implements the actual download endpoints. These are needed for the Agent to fetch installers and manifests.

**Fix:** Add a ticket or extend P2-003 to implement artifact download endpoints with `[AgentAuth]`.

### G4: Step-Level Reporting Endpoint Not Implemented

The MVP Plan (Section 8) defines:
- `POST /api/runs/{runId}/steps` — step-level reporting
- `POST /api/runs/{runId}/complete` — run completion

P2-006's tasks mention creating these methods in the Agent's `AgentPollingService`, but no Orchestrator-side ticket implements the receiving endpoints. P2-005 only adds `POST /api/runs` and `GET /api/runs/{runId}`.

**Fix:** Add controller endpoints for receiving step reports and run completion. This is essential for P2-006 and P2-007 to function end-to-end.

### G5: No `assignedWorkloadVersion` Update After Install

P2-007 says "Update `AgentNode.AssignedWorkloadId` / `AssignedWorkloadVersion` after successful INSTALL run (not after pre-check)." But no ticket in Phase 2 actually implements this — it's in Phase 3 (P3 items). This is fine architecturally but should be called out so Phase 2 pre-check / delta flow doesn't assume it's done.

---

## BUGS — Technical Issues in Code Examples

### B1: N+1 Query in P2-003 `GetNextTaskAsync`

P2-003 iterates over packages and queries `Artifacts` one-by-one in a loop:

```csharp
foreach (var wp in packages)
{
    var artifact = await _db.Artifacts
        .FirstOrDefaultAsync(a => a.PackageId == wp.PackageId && a.Version == wp.PackageVersion);
```

**Fix:** Batch-load all artifacts upfront:

```csharp
var artifactIds = packages.Select(p => new { p.PackageId, p.PackageVersion }).ToList();
var artifacts = await _db.Artifacts.Where(a => artifactIds.Select(x => x.PackageId).Contains(a.PackageId)).ToListAsync();
```

### B2: WorkloadPackages Query Missing Version Filter (P2-003)

The query filters only by `WorkloadId`:

```csharp
var packages = await _db.WorkloadPackages
    .Where(wp => wp.WorkloadId == run.WorkloadId)
    .ToListAsync();
```

`WorkloadPackages` has a composite key of `(WorkloadId, PackageId)` but multiple versions of a workload can share the same `WorkloadId`. The query must also filter by version:

```csharp
.Where(wp => wp.WorkloadId == run.WorkloadId && wp.PackageVersion == ...)
```

Wait — looking at the schema, `WorkloadPackage` has `workloadId, packageId, packageVersion` as fields. But a workload can have multiple versions (e.g., `dbms-workload` v1 and v2). The `WorkloadPackage` uses `workloadId` but not `workloadVersion` as a FK field. This means if `dbms-workload` v1 and v2 both exist, `WorkloadPackage` entries for both versions would share the same `workloadId` and be indistinguishable.

**This is a schema gap.** `WorkloadPackage` needs a `workloadVersion` column, or the composite key must include it.

### B3: `Version.TryParse` May Fail on Non-Standard Versions (P2-008)

`CompareVersions` uses `Version.TryParse` which expects format `major.minor[.build[.revision]]` (1-4 numeric components). Manifest examples include:

- `"24.1.0"` — parses OK
- `"15.0.18390.0"` — parses OK  
- `"3.11.0"` — parses OK

But real-world software versions like `"5.0.1-rc1"`, `"2024.3"`, or `"9.0.26100.3194"` may fail or parse incorrectly. The fallback to `string.Compare` is reasonable but silently produces different ordering than numeric comparison.

**Fix:** Document that version strings must be .NET `System.Version`-compatible, or implement a more robust version parser.

### B4: `LostAgentDetectionService` Uses Global Default Instead of Per-Agent Threshold

P2-004's code applies one global threshold to all agents:

```csharp
var threshold = TimeSpan.FromSeconds(
    _options.DefaultPollingIntervalSeconds * _options.LostThresholdMultiplier);
```

This ignores the MVP Plan's per-agent `pollingIntervalSeconds`. An agent enrolled with 60-second polling would be incorrectly marked LOST at the 90-second default threshold instead of 180 seconds.

**Fix:** Query each agent's `PollingIntervalSeconds` and compute per-agent thresholds. (Requires fixing G1 first.)

### B5: Artifact Null Reference (P2-003)

```csharp
var artifact = await _db.Artifacts
    .FirstOrDefaultAsync(a => a.PackageId == wp.PackageId && a.Version == wp.PackageVersion);

taskPackages.Add(new TaskPackage
{
    ManifestUrl = $"/api/artifacts/{artifact!.Id}/manifest",
    BinaryUrl = $"/api/artifacts/{artifact.Id}/download",
```

If no artifact matches (e.g., manifest uploaded but binary missing), `artifact` is `null` and `artifact!.Id` throws `NullReferenceException`. No null check or error handling.

**Fix:** Handle missing artifact gracefully — either skip the package with a warning or return an error.

---

## INCONSISTENCIES — Contradictions Between Tickets or With MVP Plan

### I1: P2-001 Sends Heartbeat Before P2-004 Implements the Endpoint

P2-001's code (the service skeleton) sends heartbeat requests to `POST /api/agents/{agentId}/heartbeat`, but the heartbeat endpoint isn't implemented until P2-004. P2-001's acceptance criteria even say "Heartbeat sent on each polling cycle (placeholder for now)."

**Status:** Acknowledged in P2-001 ("placeholder for now") but the dependency graph has P2-001 → P2-002 → P2-004. If P2-001 is tested standalone, heartbeats will 404. Acceptable if P2-001 is committed first and P2-004 immediately after, but the P2-001 verification steps require heartbeat endpoint functionality.

**Fix:** P2-001 verification should test heartbeat separately or P2-001 should gate heartbeat behind a feature flag/config toggle.

### I2: LOST → Recovery State Machine Mismatch

MVP Plan Section 12 state machine:
```
LOST -> REGISTERED  (Agent comes back online and resumes heartbeat)
```

P2-004's code recovers to `WORKLOAD_ASSIGNED` if the agent has an assigned workload:

```csharp
agent.Status = agent.AssignedWorkloadId != null
    ? AgentNodeStatus.WORKLOAD_ASSIGNED
    : AgentNodeStatus.REGISTERED;
```

This is actually **correct** behavior — an agent that was working before going LOST should resume in `WORKLOAD_ASSIGNED` state, not drop back to bare `REGISTERED`. But it contradicts the MVP Plan's state machine which only shows `LOST → REGISTERED`.

**Fix:** Update the MVP Plan Section 12 state machine to show `LOST → REGISTERED | WORKLOAD_ASSIGNED` based on workload assignment.

### I3: `AgentNodeStatus` Enum Missing `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE`

P1-002 defines `AgentNodeStatus` as: `REGISTERED, UNREGISTERED, LOST`. But the MVP Plan Section 12 state machine includes `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE`:

```
REGISTERED -> WORKLOAD_ASSIGNED -> NEEDS_UPDATE
```

P2-004's code uses `AgentNodeStatus.WORKLOAD_ASSIGNED` which doesn't exist in the P1-002 enum.

**Fix:** Add `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE` to the `AgentNodeStatus` enum. (`NEEDS_UPDATE` is for Phase 3 but `WORKLOAD_ASSIGNED` is needed now.)

### I4: Relative URL Construction (P2-003)

P2-003 returns `ManifestUrl` and `BinaryUrl` as relative paths (`/api/artifacts/{id}/manifest`). The Agent needs full URLs. The Agent knows its `orchestratorUrl` from `agent.json` and can prepend it, but this should be explicitly documented or the Orchestrator should return full URLs.

**Status:** The Agent constructs full URLs client-side by prepending `orchestratorUrl`. This works but should be called out in the ticket or the API contract.

### I5: P2-005 CreatePreCheckRun Doesn't Filter WorkloadPackages by Version

Same schema issue as B2. The query:

```csharp
var packages = await _db.WorkloadPackages
    .Where(wp => wp.WorkloadId == workloadId).ToListAsync();
```

Should filter by workload version to avoid pulling packages from all versions of the same workload.

---

## MISSING DETAILS

### M1: `WorkloadPackage` Schema Missing `WorkloadVersion` Column

The `WorkloadPackage` table in Section 7 has fields: `workloadId, packageId, packageVersion, preInitSteps, postInitSteps`. But there's no `workloadVersion` field. When a workload has multiple versions (e.g., `dbms-workload` v1 and v2), both versions share the same `workloadId`. Without a version discriminator, you can't tell which packages belong to v1 vs v2.

**Required fix:** Add `workloadVersion` to `WorkloadPackage` schema and make the composite key `(workloadId, workloadVersion, packageId)`.

### M2: No Specification for Artifact Download Endpoint

P2-003 returns artifact URLs but no ticket implements the actual download endpoints. Need:
- `GET /api/artifacts/{artifactId}/download` (with `[AgentAuth]`)
- `GET /api/artifacts/{artifactId}/manifest` (with `[AgentAuth]`)

### M3: No Specification for Step Reporting / Run Completion Endpoints

P2-006's Agent-side code sends step reports and run completion, but no Orchestrator-side endpoint ticket exists to receive them.

### M4: P2-002 Auth Middleware — No DB Index on `AgentSecret`

The middleware does `.FirstOrDefaultAsync(a => a.AgentSecret == token)` on every request. Without an index on `AgentSecret`, this is a linear scan. The P1-002 schema setup creates indexes on `AgentNode.AgentId` and `AgentNode.Status` but not on `AgentSecret`.

**Fix:** Add index on `AgentSecret` in `OnModelCreating`.

### M5: P2-005 Acceptance Criteria Missing Version-Specific WorkloadPackage Query

The verification steps say "Create a workload with 3 packages" but don't test with multiple workload versions sharing the same `workloadId`.

### M6: P2-004 LOST Detection Timer Hardcoded

The service uses `Task.Delay(TimeSpan.FromSeconds(30))` hardcoded. The MVP Plan says LOST threshold configuration comes from `appsettings.json`, but the detection interval itself should also be configurable (or at minimum documented as intentional).

---

## READINESS ASSESSMENT

**Phase 2 is NOT ready for implementation.** There are several blocking issues:

| Severity | Issue | Blocker? |
|----------|-------|----------|
| **Critical** | M1: `WorkloadPackage` missing `workloadVersion` — all versioned workload queries are broken | Yes |
| **Critical** | G1 + B4: `pollingIntervalSeconds` not stored on `AgentNode` — LOST detection uses wrong threshold | Yes |
| **Critical** | G3: No artifact download endpoints — Agent can't fetch binaries | Yes |
| **Critical** | G4: No step reporting / run completion endpoints — Agent can't report results | Yes |
| **Medium** | I3: `WORKLOAD_ASSIGNED` not in `AgentNodeStatus` enum — code won't compile | Yes |
| **Medium** | B2 + I5: WorkloadPackage queries missing version filter — wrong packages returned | Yes |
| **Low** | B1: N+1 query in GetNextTaskAsync — performance issue, not functional | No |
| **Low** | B5: Null reference on missing artifact — should handle gracefully | No |
| **Low** | B3: Version.TryParse on non-standard formats — edge case | No |
| **Low** | I2: State machine doesn't show LOST → WORKLOAD_ASSIGNED | No |
| **Low** | I4: Relative URLs need client-side resolution | No |

**Required before P2 implementation can begin:**

1. Add `pollingIntervalSeconds` column to `AgentNodes` table (P1-002 schema fix)
2. Add `workloadVersion` column to `WorkloadPackage` table (P1-002 schema fix)
3. Add `WORKLOAD_ASSIGNED` (and optionally `NEEDS_UPDATE`) to `AgentNodeStatus` enum
4. Add `AgentSecret` index to the DB schema
5. Add P2 ticket(s) for: artifact download endpoints, step reporting endpoint, run completion endpoint
6. Update P2-003 and P2-005 queries to filter WorkloadPackages by workload version
7. Fix P2-004 LOST detection to use per-agent polling intervals