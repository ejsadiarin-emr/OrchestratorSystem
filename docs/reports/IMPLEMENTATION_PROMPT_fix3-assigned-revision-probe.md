# Fix 3: LoadDetectionConfigsByWorkloadAsync Uses Wrong Revision

## Problem

`LoadDetectionConfigsByWorkloadAsync` in `NodesController.cs:690-749` always loads detection configs from **published** revisions (`Where(r => r.IsPublished)`). When a node has an assigned workload on a non-published (or superseded) revision, the probe checks for the wrong package names/versions/paths, writes incorrect results to `PackageStatesJson`, and `BuildReadOnlyPreCheckSummary` shows stale/false failures.

## Root Cause

```
NodesController.cs:700-702:
var publishedRevisions = revisions.Where(r => r.IsPublished).ToList();
```

The node's `NodeWorkloadState.CurrentRevisionId` is **never consulted**. When v2.0.0 is published and the node is assigned v1.0.0, the probe sends v2 detection configs to the agent.

## What to Change

### 1. Controller Method: `LoadDetectionConfigsByWorkloadAsync`

**File**: `apps/orchestrator/backend/Controllers/NodesController.cs` (line ~690)

Add an optional `NodeEntity? node = null` parameter. When `node` is provided and `workloadId` is null, use the node's assigned `CurrentRevisionId`s instead of the published filter:

```csharp
private async Task<Dictionary<Guid, List<DetectionConfigDto>>> LoadDetectionConfigsByWorkloadAsync(
    Guid? workloadId, NodeEntity? node = null)
{
    // ... existing query setup (lines 691-699) stays the same ...

    // REPLACE line 700: var publishedRevisions = revisions.Where(r => r.IsPublished).ToList();
    List<WorkloadRevisionEntity> effectiveRevisions;

    if (node is not null && !workloadId.HasValue)
    {
        // No explicit workloadId — use the node's assigned revisions
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
        // Explicit workloadId (Run Creator) or no node context → published only
        effectiveRevisions = revisions.Where(r => r.IsPublished).ToList();
    }

    // Replace all remaining references to `publishedRevisions` with `effectiveRevisions`
    var packageIds = effectiveRevisions
        .SelectMany(r => r.Packages)
        .Select(p => p.PackageId)
        .Distinct()
        .ToList();

    // ... rest of the method uses effectiveRevisions instead of publishedRevisions ...
    foreach (var rev in effectiveRevisions)
    {
        // ... same loop body ...
    }
}
```

### 2. Caller Change: `RunPreChecks` (line ~261)

**File**: `apps/orchestrator/backend/Controllers/NodesController.cs`

Current:
```csharp
var workloadDetectionConfigs = request.WorkloadId.HasValue
    ? await LoadDetectionConfigsByWorkloadAsync(request.WorkloadId)
    : new Dictionary<Guid, List<DetectionConfigDto>>();
```

Change to pass the node entity when workloadId is null:
```csharp
var workloadDetectionConfigs = request.WorkloadId.HasValue
    ? await LoadDetectionConfigsByWorkloadAsync(request.WorkloadId)
    : await LoadDetectionConfigsByWorkloadAsync(null, node);
```

This is inside the `foreach (var node in nodes)` loop at line 285. The detection configs are gathered per-node when workloadId is null (each node may have different assigned workloads).

**IMPORTANT**: When `workloadId` IS set, the configs are the same for all nodes (one workload's published packages). When `workloadId` is null, move the `LoadDetectionConfigsByWorkloadAsync` call INSIDE the foreach loop so each node gets its own configs.

### 3. Caller Change: `RunSinglePreCheck` (line ~381)

**File**: `apps/orchestrator/backend/Controllers/NodesController.cs`

Current:
```csharp
var workloadDetectionConfigs = workloadId.HasValue
    ? await LoadDetectionConfigsByWorkloadAsync(workloadId)
    : new Dictionary<Guid, List<DetectionConfigDto>>();
```

Change to:
```csharp
var workloadDetectionConfigs = workloadId.HasValue
    ? await LoadDetectionConfigsByWorkloadAsync(workloadId)
    : await LoadDetectionConfigsByWorkloadAsync(null, node);
```

### 4. New Tests

**File**: `tests/orchestrator/unit/Controllers/NodesPreCheckReconciliationTests.cs`

Add these test methods following the existing naming/style conventions:

#### Test 1: `AssignedRevision_AllPackagesInstalled_ReportsPassed`

```csharp
[Test]
public async Task AssignedRevision_AllPackagesInstalled_ReportsPassed()
```

**Setup**: 
- Node has a `NodeWorkloadState` with `CurrentRevisionId` pointing to revision v1.0.0 (IsPublished = false)
- There is also a v2.0.0 revision (IsPublished = true) with DIFFERENT packages (diff packageId, diff version, diff detection path)
- Agent returns `AlreadySatisfied` for all v1 packages
- Call `RunSinglePreCheck(id, workloadId: null)` — not `RunPreChecks` with explicit workloadId

**Assert**:
- Response has per-package items for all v1 packages, each `status == "passed"`
- No drift warning needed if all passed
- `PackageStatesJson` updated in DB with `AlreadySatisfied`
- The v2 packages should NOT appear in the request to the agent (verify via handler inspection)

#### Test 2: `AssignedRevision_SomeMissing_ReportsDrift`

```csharp
[Test]
public async Task AssignedRevision_SomeMissing_ReportsDrift()
```

**Setup**:
- Node has NodeWorkloadState with v1 revision (IsPublished = false)
- v1 has 3 packages, agent returns 2 AlreadySatisfied + 1 NotPresent
- v2 is published with different packages
- Call with workloadId null

**Assert**:
- Drift shows `2/3 packages present`, status `warning`
- Per-package items: 2 passed, 1 failed

#### Test 3: `ExplicitWorkloadId_UsesPublishedRevision`

```csharp
[Test]
public async Task ExplicitWorkloadId_UsesPublishedRevision()
```

**Setup**:
- Node has NodeWorkloadState with v1 revision (IsPublished = false, packages pkgA v1)
- v2 is published (IsPublished = true, packages pkgB v2) 
- Call `RunSinglePreCheck(id, workloadId: explicitWorkloadId)` with the workload's ID

**Assert**:
- The probe sent to the agent contains v2 packages (pkgB, v2) — verify via handler inspection
- The v1 packages (pkgA) should NOT be sent

#### Test 4: `RunPreChecks_WithoutWorkloadId_ProbesAssignedRevisions`

```csharp
[Test]
public async Task RunPreChecks_WithoutWorkloadId_ProbesAssignedRevisions()
```

**Setup**:
- Two nodes, each with different assigned workloads (different revisions)
- Both workloads have same workloadId but different revisions
- One revision IsPublished=false (v1), other revision IsPublished=true (v2)
- Call `RunPreChecks` with nodeIds but `WorkloadId = null`

**Assert**:
- Each node gets probed with its own assigned revision's packages
- Node with v1 gets v1 configs, node with v2 gets v2 configs

## Verification

After implementation, run:

```powershell
$env:DOTNET_ROOT="C:\Program Files\dotnet"; $env:DOTNET_MULTILEVEL_LOOKUP="0"; dotnet test tests\orchestrator\unit --filter "NodesPreCheckReconciliationTests"
```

All 15 tests must pass (11 existing + 4 new).

Then run the full suite:
```powershell
dotnet test tests\orchestrator\unit
```

Confirm 0 new failures.

## Key Files

| File | What to change |
|------|---------------|
| `apps/orchestrator/backend/Controllers/NodesController.cs` | `LoadDetectionConfigsByWorkloadAsync` — add `NodeEntity?` param, use assigned revision when workloadId null |
| `apps/orchestrator/backend/Controllers/NodesController.cs` | `RunPreChecks` — pass node, move call inside loop for null workloadId case |
| `apps/orchestrator/backend/Controllers/NodesController.cs` | `RunSinglePreCheck` — pass node |
| `tests/orchestrator/unit/Controllers/NodesPreCheckReconciliationTests.cs` | Add 4 new test methods |

## Design Constraint

- **Do NOT change behavior when `workloadId` is explicitly provided** — that's the Run Creator flow where published revision preview is correct
- **Only change the `workloadId = null` path** — the node page "check assigned workloads" flow
- The `RunPreChecks` batch endpoint needs the detection config load moved inside the loop for the null-workloadId case, but should keep the existing path when workloadId IS set (configs are the same for all nodes)
