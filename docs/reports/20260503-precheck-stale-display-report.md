# Pre-Check Stale Display — Comprehensive Investigation Report

**Date**: 2026-05-03  
**Branch**: `main`  
**Scope**: Pre-check reconciliation flow — `NodesController.cs`, probe config loading, DB read-back path  
**Issue**: Pre-checks show "sqlserver: failed -- not installed" and "drift: 2/3 packages present" even though all assigned workload packages (v1) are installed on the agent machine.

---

## 1. Executive Summary

Two bugs have already been **fixed** in `NodesController.ReconcileProbeResults` (see section 4). However a third, deeper defect in `LoadDetectionConfigsByWorkloadAsync` remains: it **always probes against the published revision's packages**, regardless of which revision the node is actually assigned. When the published revision differs from the assigned revision (different package names, versions, or detection paths), the probe returns misleading results, and the node detail page (`BuildReadOnlyPreCheckSummary`) shows incorrect status.

The user's machine has **Amazing Workload v1** assigned and fully installed. But **v2.0.0 is the published revision**, and v2 expects a different sqlserver path (`MSSQL17.SQLEXPRESS`) than v1 (`MSSQL15.SQLEXPRESS`). The probe correctly reports the v2 path doesn't exist, but the UI misleadingly says "sqlserver: failed" for the assigned v1 workload.

---

## 2. User Scenario (Reproduction)

| What | State |
|------|-------|
| Node has Amazing Workload v1 **assigned** | `NodeWorkloadState.CurrentRevisionId` → v1 |
| Packages: dbeaver 24.3.0, python 3.13.3, sqlserver 2019 | All installed on the agent |
| v2.0.0 is **published** | dbeaver 26.0.3, python 3.14.4, sqlserver 2025 |
| Click "Run Pre-check" on `/nodes` or open node detail (`/api/nodes/{id}/details`) | Shows `sqlserver: failed -- not installed`, `drift: 2/3 packages present` |

The machine HAS sqlserver (MSSQL15.SQLEXPRESS\sqlservr.exe) but at the v1 path. The v2 probe looks for MSSQL17.SQLEXPRESS\sqlservr.exe — correctly absent.

---

## 3. Root Cause Chain (End-to-End)

### 3.1 The Loading Defect — `LoadDetectionConfigsByWorkloadAsync` (line 690–749)

```csharp
// NodesController.cs:700-702
var publishedRevisions = revisions.Where(r => r.IsPublished).ToList();
```

This method **only** loads detection configs from published revisions. It never consults the node's `NodeWorkloadState.CurrentRevisionId`. When `v2.0.0` is published and `v1.0.0` is not, all pre-check probes use `v2` packages.

**File**: `apps/orchestrator/backend/Controllers/NodesController.cs:700-702`

### 3.2 Who Calls It

| Endpoint | Code location | workloadId param |
|----------|--------------|-----------------|
| `POST /api/nodes/prechecks` | `RunPreChecks:275-277` | user-supplied (Run Creator) or null |
| `POST /api/nodes/{id}/prechecks` | `RunSinglePreCheck:394-396` | user-supplied or null |

Both methods pass `workloadId` to `LoadDetectionConfigsByWorkloadAsync`. When workloadId IS provided, the intent is "preview what this workload would require" — probing against the published revision is **correct**. When workloadId IS NULL, the intent is "check the node's assigned workloads" — the current code sends **no** detection configs (empty dictionary), which means packages are never probed in this path. Either way, the node's `CurrentRevision` is never used.

### 3.3 How Probes Write to DB — `ReconcileProbeResults` (line 500–633)

When the probe returns results for v2 packages (because the published revision was used):

| NodeWorkloadState exists? | Branch | What happens |
|---------------------------|--------|-------------|
| YES (workload assigned) | `hasAnyDetected` true, `allMatch` false → **drift** (line 592–620) | Writes v2's NotPresent/WrongVersion to `PackageStatesJson`. This is **correct** for drift detection — the packages DO differ. |
| YES (workload assigned) | `allMatch` true → **Scenario A** (line 582–589) | Updates `PackageStatesJson` with current good state. `Fix 2` added this. |
| NO (unassigned workload) | (line 548–564) | Reports per-package items without DB write. `Fix 1` added this. |

### 3.4 How the UI Reads Back — `BuildReadOnlyPreCheckSummary` (line 752–840)

```csharp
// NodesController.cs:789 — reads PackageStatesJson written by last probe
if (prop.Value.TryGetProperty("status", out var statusEl))
{
    var s = statusEl.GetString();
    if (s == "NotPresent") anyFailed = true;
    ...
}
```

This reads `NodeWorkloadState.PackageStatesJson` written by the last `ReconcileProbeResults` call. If that call used v2 detection configs and wrote `NotPresent` for sqlserver, the UI shows `failed`. The user's **actual** v1 sqlserver installation is irrelevant — the probe never checked for it.

### 3.5 The Full Chain

```
User clicks "Pre-check"
  → LoadDetectionConfigsByWorkloadAsync(workloadId)
    → WHERE IsPublished → loads v2 packages (dbeaver 26.0.3, python 3.14.4, sqlserver 2025)
  → Agent probed for: dbeaver@26.0.3, python@3.14.4, sqlserver@2025 (MSSQL17.SQLEXPRESS)
  → Agent response: dbeaver=AlreadySatisfied, python=??? , sqlserver=NotPresent (MSSQL17 path doesn't exist)
  → ReconcileProbeResults: enters DRIFT branch, writes {"status":"NotPresent",...} to PackageStatesJson
  → BuildReadOnlyPreCheckSummary reads PackageStatesJson → "failed"
  → User sees: "sqlserver: failed -- not installed" (for v1 workload!)
```

**The probe never asked about v1 packages.** The agent was never told to look for MSSQL15.SQLEXPRESS.

---

## 4. Completed Fixes (Already Applied)

### Fix 1: Unassigned Workload Null Branch (line 548–564)

**Commit**: on `main`  
**What**: The empty `if (existingState is null)` block that silently discarded agent probe results was replaced with per-package reporting (drift summary, present/total ratio, no DB write).

**Tests added**: `UnassignedWorkload_AllMatch_ReportsPassed`, `UnassignedWorkload_PartialMatch_ReportsDrift`, `UnassignedWorkload_NoneMatch_ReportsMissing`

### Fix 2: PackageStatesJson Updated on Scenario A (All-Match) (line 582–589)

**Commit**: on `main`  
**What**: When all packages matched (`allMatch == true`), the code called `BuildPerPackageItems` for the response but never updated `PackageStatesJson` in the DB. This meant `BuildReadOnlyPreCheckSummary` (the GET endpoint) read stale `NotPresent` data from a prior drift probe.

**Fix**: Added `existingState.PackageStatesJson = BuildPackageStatesJson(...)` and `existingState.UpdatedAtUtc = DateTime.UtcNow` in the all-match branch.

### Why These Fixes Are Insufficient

Both fixes are **correct and necessary** but address problems downstream of the detection config loading. They don't fix the root cause: the probe loads the **wrong package specifications** (published revision != assigned revision).

---

## 5. Remaining Defect — Wrong Revision Packages Loaded for Assigned Workloads

### 5.1 Defect

`LoadDetectionConfigsByWorkloadAsync` loads **published** revisions only. When the node's assigned workload uses a **different revision** (typically an older one that isn't the latest published), the probe:
- Checks for package names/versions/paths from the published revision
- Reports misleading results that don't reflect the node's actual assignment
- Writes inaccurate `PackageStatesJson` to the DB
- Causes `BuildReadOnlyPreCheckSummary` to show incorrect status

### 5.2 Why This Is Distinct From Fix 1 and Fix 2

| Fix | What it addressed | What it didn't |
|-----|------------------|----------------|
| Fix 1 | Unassigned workload results were silently dropped | The probe configs sent to the agent |
| Fix 2 | Stale `PackageStatesJson` on all-match scenario | Which packages the probe was checking |
| **Fix 3 needed** | — | **Probe sends v2 packages to agent, writes v2 results to DB, even though the node is assigned v1** |

### 5.3 Design Decision: When Should Each Revision Be Used?

| Trigger | What to probe | Rationale |
|---------|--------------|-----------|
| Run Creator auto pre-check (`workloadId` explicit) | **Published revision** | User is previewing what deploying the latest version would do |
| Node page "Run Pre-check" (`workloadId` null) | **Node's assigned CurrentRevision** | User wants to verify the assigned workload's state |
| `GET /api/nodes/{id}/details` | Read `PackageStatesJson` from DB | Should reflect the node's assigned state (currently reflects published state) |

### 5.4 Proposed Fix

**File**: `apps/orchestrator/backend/Controllers/NodesController.cs`

**Approach**: Add a new method `LoadDetectionConfigsForNodeWorkloads` that takes `NodeEntity` and loads packages from the node's `NodeWorkloadStates.CurrentRevision`. Use this when `workloadId` is **null** (the "check assigned workloads" flow). Keep `LoadDetectionConfigsByWorkloadAsync` for the explicit `workloadId` case (Run Creator).

**Implementation outline**:

```csharp
// New method — loads detection configs from the node's ASSIGNED revisions
private async Task<Dictionary<Guid, List<DetectionConfigDto>>> LoadDetectionConfigsForNodeWorkloads(
    NodeEntity node)
{
    var stateRevisionIds = node.NodeWorkloadStates
        .Where(s => s.CurrentRevisionId.HasValue)
        .Select(s => s.CurrentRevisionId!.Value)
        .ToList();

    if (stateRevisionIds.Count == 0)
        return new Dictionary<Guid, List<DetectionConfigDto>>();

    var revisions = await _db.WorkloadRevisions
        .Where(r => stateRevisionIds.Contains(r.RevisionId))
        .Include(r => r.Packages)
        .ToListAsync();

    var packageIds = revisions
        .SelectMany(r => r.Packages)
        .Select(p => p.PackageId)
        .Distinct()
        .ToList();

    var packages = await _db.Packages
        .Where(p => packageIds.Contains(p.PackageId))
        .ToListAsync();

    var packageMap = packages.ToDictionary(p => p.PackageId);
    var result = new Dictionary<Guid, List<DetectionConfigDto>>();

    foreach (var rev in revisions)
    {
        var configs = new List<DetectionConfigDto>();
        foreach (var wp in rev.Packages)
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
        if (configs.Count > 0)
            result[rev.WorkloadId] = configs;
    }
    return result;
}
```

**Changes to callers**:

In `RunPreChecks` (line 275–277):
```csharp
var workloadDetectionConfigs = request.WorkloadId.HasValue
    ? await LoadDetectionConfigsByWorkloadAsync(request.WorkloadId)
    : await LoadDetectionConfigsForNodeWorkloads(node);  // ← use assigned revision
```

Wait — `RunPreChecks` iterates over multiple nodes. When `workloadId` is null, each node may have different assigned workloads/revisions. The probe is sent per-node, but the detection configs are gathered once. This requires a different approach for the batch endpoint.

**Alternative (simpler)**: Modify `LoadDetectionConfigsByWorkloadAsync` to accept an optional `NodeEntity` parameter. When provided and the workloadId is null, use the node's assigned revisions. When workloadId is provided, keep using published revisions.

**Alternative (simplest — recommended)**: Accept `NodeEntity?` parameter. When the node has an assigned workload whose `CurrentRevisionId` matches one of the revisions, prefer the assigned revision over whether it's published.

```csharp
// Modified signature:
private async Task<Dictionary<Guid, List<DetectionConfigDto>>> LoadDetectionConfigsByWorkloadAsync(
    Guid? workloadId, NodeEntity? node = null)
{
    // ... existing query ...

    List<WorkloadRevisionEntity> effectiveRevisions;

    if (node is not null && workloadId.HasValue)
    {
        // Explicit workloadId (Run Creator) → use published revision
        effectiveRevisions = revisions.Where(r => r.IsPublished).ToList();
    }
    else if (node is not null)
    {
        // No workloadId (check assigned workloads) → use node's CurrentRevision
        var assignedRevisionIds = node.NodeWorkloadStates
            .Where(s => s.CurrentRevisionId.HasValue)
            .Select(s => s.CurrentRevisionId!.Value)
            .ToHashSet();
        effectiveRevisions = revisions
            .Where(r => assignedRevisionIds.Contains(r.RevisionId))
            .ToList();
    }
    else
    {
        effectiveRevisions = revisions.Where(r => r.IsPublished).ToList();
    }

    // ... rest uses effectiveRevisions instead of publishedRevisions ...
}
```

### 5.5 Tradeoffs

| Approach | Pro | Con |
|----------|-----|-----|
| New method + switch in caller | Clean separation; published vs assigned logic is explicit | Some code duplication with the existing method |
| Add `NodeEntity?` param to existing method | Single code path, fewer lines | Mixes two concerns; published-only callers (Run Creator) pass null node |
| **Second approach (recommended)** | Minimal diff, reuses existing query infrastructure | Slightly more complex method signature |

The second approach is recommended because it keeps the diff small and reuses the existing package lookup infrastructure.

---

## 6. Acceptance Criteria

### AC-1: Node with assigned v1 workload, v2 published — node page pre-check shows passed

**Given** a node with Amazing Workload v1 assigned (dbeaver 24.3.0, python 3.13.3, sqlserver 2019 at MSSQL15)  
**And** all v1 packages are installed on the agent  
**And** v2.0.0 is published (dbeaver 26.0.3, python 3.14.4, sqlserver 2025 at MSSQL17)  
**When** user clicks "Run Pre-check" on the nodes page (no workloadId)  
**Then** the response shows `dbeaver: passed`, `python: passed`, `sqlserver: passed`  
**And** drift shows `3/3 packages present`  
**And** overall status is `passed`

### AC-2: Node with assigned v1 workload, v2 published — detail page shows passed

**Given** same setup as AC-1  
**When** user opens `GET /api/nodes/{id}/details`  
**Then** `LatestPreCheck.Items` includes the workload item with status `passed`  
**And** `PackageStatesJson` in the DB contains `AlreadySatisfied` for all three packages

### AC-3: Run Creator auto pre-check still uses published revision

**Given** same setup as AC-1  
**When** user opens Run Creator and selects Amazing Workload (auto pre-check fires with `workloadId`)  
**Then** the probe uses the **published v2** detection configs  
**And** shows drift/warning for sqlserver (since MSSQL17 doesn't exist)  
**And** this is correct — Run Creator should preview what deploying the latest revision would do

### AC-4: Unassigned workload case still works (no regression on Fix 1)

**Given** a node with no workloads assigned  
**When** user opens Run Creator and selects a workload (auto pre-check with `workloadId`)  
**Then** per-package items appear in the response (Fix 1 behavior preserved)  
**And** no `NodeWorkloadState` is created in the DB

### AC-5: All-match case still updates DB (no regression on Fix 2)

**Given** a node with v1 assigned and all v1 packages installed  
**And** v1 is the published revision  
**When** pre-check runs  
**Then** `PackageStatesJson` is updated with `AlreadySatisfied` for all packages (Fix 2 behavior)  
**And** `BuildReadOnlyPreCheckSummary` returns `passed`

### AC-6: Node with NO assigned workloads — basic probe only

**Given** a node with no workloads assigned  
**When** user clicks "Run Pre-check" on nodes page (no workloadId)  
**Then** only OS, agent, disk items are returned (no package items)  
**And** no DB writes occur

---

## 7. Verification Steps

### 7.1 Unit Tests

Run existing tests — must all pass:
```powershell
$env:DOTNET_ROOT="C:\Program Files\dotnet"; $env:DOTNET_MULTILEVEL_LOOKUP="0"; dotnet test tests\orchestrator\unit --filter "NodesPreCheckReconciliationTests"
```

Add the following new tests to `tests/orchestrator/unit/Controllers/NodesPreCheckReconciliationTests.cs`:

1. **`AssignedRevision_AllPackagesInstalled_ReportsPassed`** — Node has v1 assigned, all v1 packages AlreadySatisfied. Verify per-package items all show `passed`.
2. **`AssignedRevision_SomeMissing_ReportsDrift`** — Node has v1 assigned, 1/3 packages NotPresent. Verify drift warning with `1/3` ratio.
3. **`PublishedRevision_UsedForExplicitWorkloadId`** — workloadId is explicitly provided, verify published revision's packages (not assigned revision's) are used for detection configs.
4. **`GetDetails_ReflectsAssignedRevisionState`** — After probing with assigned revision, verify `BuildReadOnlyPreCheckSummary` returns correct status.

### 7.2 End-to-End Smoke Check

1. Start orchestrator + agent on VM
2. Install v1 packages (dbeaver 24.3.0, python 3.13.3, sqlserver 2019 MSSQL15)
3. Assign Amazing Workload v1 to the node
4. Publish v2.0.0 (different packages)
5. On nodes page, click "Run Pre-check"
6. **Verify**: All 3 packages show `passed`, drift shows `3/3`, overall `passed`
7. Open node detail page
8. **Verify**: Same — workload shows `passed`
9. Open Run Creator with Amazing Workload selected
10. **Verify**: Auto pre-check shows drift (sqlserver NotPresent for v2), which is correct for the Run Creator flow

### 7.3 Full Test Suite

```powershell
dotnet test tests\orchestrator\unit
```

Confirm 0 new failures.

---

## 8. Files Involved

| File | Role |
|------|------|
| `apps/orchestrator/backend/Controllers/NodesController.cs:690-749` | `LoadDetectionConfigsByWorkloadAsync` — root cause (published-only filter) |
| `apps/orchestrator/backend/Controllers/NodesController.cs:275-277` | `RunPreChecks` — caller, needs to use assigned revision when workloadId is null |
| `apps/orchestrator/backend/Controllers/NodesController.cs:394-396` | `RunSinglePreCheck` — same |
| `apps/orchestrator/backend/Controllers/NodesController.cs:500-633` | `ReconcileProbeResults` — writes probe results to DB |
| `apps/orchestrator/backend/Controllers/NodesController.cs:752-840` | `BuildReadOnlyPreCheckSummary` — reads PackageStatesJson for GET /details |
| `apps/orchestrator/backend/Data/Entities/NodeWorkloadStateEntity.cs` | `NodeWorkloadStateEntity` — holds `CurrentRevisionId` and `PackageStatesJson` |
| `apps/orchestrator/backend/Data/Entities/WorkloadRevisionEntity.cs` | `WorkloadRevisionEntity` — has `IsPublished` flag |
| `tests/orchestrator/unit/Controllers/NodesPreCheckReconciliationTests.cs` | Existing tests for reconciliation scenarios |

---

## 9. Related Documents

- `docs/reports/20260503-precheck-logic-audit.md` — Original audit identifying Fix 1 gap (unassigned workload)
- `docs/reports/session-handoff-agent-vm-debugging.md` — Handoff notes from bugfix session identifying the revision mismatch
