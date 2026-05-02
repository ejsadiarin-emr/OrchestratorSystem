# Pre-Check Logic Audit Report

**Date**: 2026-05-03  
**Context**: Follows commit 455f294 (removal of `ExpectedVersion` from codebase). Audit of orchestrator-to-agent pre-check behavior.
**Branch**: `main`

## Expected Behavior

| Step | Action | Expected |
|------|--------|----------|
| 1 | Fresh DB + node has dbeaver-v1 (no workload assigned). Click "Run Pre-check" on `/nodes`. | Probe disk, OS, agent only. No package probing (no workload context). |
| 2 | Open Run Creator, select "Amazing Workload v1" (packages: dbeaver-v1, sqlserver-v1). Auto pre-check fires. | Probe agent for **both** packages. Show drift: "1/2 packages present (missing sqlserver-v1)". |
| 3 | User creates install run. | Pipeline runs, skips dbeaver (already installed), installs sqlserver. DB reconciles — assigns workload to node, records package states. |

## Step-by-Step Findings

### Step 1: `/nodes` "Run Pre-check" (no workload) — **BEHAVES CORRECTLY** ✓

**Path**: `NodeDetailsModal.tsx:43` → `NodesController.RunSinglePreCheck:382`

- `workloadId` = null → `workloadDetectionConfigs` = empty → no packages sent to agent → agent returns empty results list.
- `ReconcileProbeResults:500` returns only OS/Agent/Disk items.
- Matches expected behavior.

### Step 2: Run Creator auto pre-check with selected workload — **GAP FOUND** ✗

**Path**: `WorkloadRuns.tsx:111-132` → `NodesController.RunPreChecks:261` → `ReconcileProbeResults:500`

The auto pre-check DOES fire correctly:
- Frontend calls `runNodesPreChecks(onlineIds, form.workloadId)` — workloadId IS provided. ✓
- Backend loads detection configs for the workload's packages. ✓
- Backend probes agent — agent correctly reports dbeaver as `AlreadySatisfied`, sqlserver as `NotPresent`. ✓

**But `ReconcileProbeResults` drops the results on the floor** at `NodesController.cs:548-551`:

```csharp
if (existingState is null)
{
    // Workload not assigned to this node — do not report or create phantom state
}
```

When the workload is not yet assigned to the node (no `NodeWorkloadState` record), the method **iterates past the workload entry without doing anything**. No per-package items are added to the summary. The UI receives only OS/Agent/Disk items, even though the agent was properly probed and returned actionable results.

**Result**: User sees no drift warning. The "1/2 packages present (missing sqlserver-v1)" message never appears.

### Step 3: Run creation/execution — **BEHAVES CORRECTLY** ✓

**Paths**:
- `WorkloadRunsController.Create:80-308` — Drift check at line 164-185 only applies to nodes WITH existing state. For unassigned nodes, this is fine — there's no prior state to be out of sync with.
- `WorkloadRunDispatcher.DispatchAsync:56-91` — `CurrentPackages` = empty list (no prior state), `Packages` = [dbeaver, sqlserver], `ForceInstall` = false.
- `PipelineExecutor.ExecuteAsync:22-63` — Phase 0 PreCheckProbe: dbeaver → `AlreadySatisfied` (skipped), sqlserver → `NotPresent` (installed). ✓
- `DiffEngine.ApplyPreCheckOverrides:54-109` — dbeaver moved from Added → Unchanged. ✓

## Gap Summary

**One code defect** in `NodesController.ReconcileProbeResults` at line 548-551:

When a workloadId is explicitly provided to the pre-check endpoint (meaning: "I want to check this specific workload's packages against the node"), the results are silently discarded if no `NodeWorkloadState` exists. This defeats the purpose of pre-checking before assigning a workload.

### What needs to change

**File**: `apps/orchestrator/backend/Controllers/NodesController.cs`, method `ReconcileProbeResults` (lines 548-551)

Replace the "do nothing" branch for unassigned workloads with logic that:
1. Still compares agent probe results against workload detection configs
2. Builds per-package `PreCheckItem` entries with proper status (passed/warning/failed)
3. Adds a summary item showing present/total ratio (e.g., "drift: 1/2 packages present")
4. Does **NOT** create or update `NodeWorkloadState` (reconciliation to DB happens on run execution)

### Why the frontend doesn't need changes

`WorkloadRuns.tsx:770-792` already renders pre-check results from the `precheckResults` map — it shows colored badges with per-package tooltip info. Once the backend returns per-package items, they'll appear automatically.

## Acceptance Criteria

1. **Given** a node with dbeaver-v1 installed but no workload assigned, **when** user opens Run Creator and selects "Amazing Workload v1" (dbeaver-v1 + sqlserver-v1), **then** auto pre-check shows per-package results: dbeaver = passed, sqlserver = failed/not-installed, with a drift warning badge.

2. **Given** a node with ALL packages of a workload already installed (but workload not yet assigned), **when** auto pre-check runs, **then** all per-package items show "passed" and overall status is "passed".

3. **Given** a node with NONE of a workload's packages installed, **when** auto pre-check runs, **then** all packages show as "not installed" with drift warning.

4. **Given** a node with an **assigned** workload, existing reconciliation behavior (Scenario A/B/D/E) remains unchanged — no regression on existing pre-check flows.

5. **Given** the unassigned workload case, **when** pre-check runs, **then** no `NodeWorkloadState` is created in the DB (state creation is the run pipeline's job).

## Verification Steps

After implementation:

1. Run existing reconciliation tests — all must pass unchanged:
   ```
   dotnet test tests/orchestrator/unit --filter "NodesPreCheckReconciliationTests"
   ```

2. Write 3 new tests for the unassigned workload scenario:
   - `UnassignedWorkload_PartialMatch_ReportsDrift` — node has 1/2 packages
   - `UnassignedWorkload_AllMatch_ReportsPassed` — node has all packages
   - `UnassignedWorkload_NoneMatch_ReportsMissing` — node has none

3. End-to-end smoke check:
   - Start orchestrator + agent
   - Install only dbeaver on agent machine
   - Open Run Creator, select workload with dbeaver + another package
   - Confirm pre-check shows drift warning in the node list
   - Confirm per-package detail appears in tooltip

4. Full test suite pass:
   ```
   dotnet test tests/orchestrator/unit
   ```
   Confirm 0 new failures.

## Implementation Outline

The fix is contained to one method. The new branch at `ReconcileProbeResults:548` should:

```
if (existingState is null)
{
    // Unassigned workload — report probe results without creating DB state
    var presentCount = detectionConfigs.Count(d =>
        agentResultMap.TryGetValue(d.PackageId, out var r) &&
        r.Status != PreCheckStatus.NotPresent);
    var totalCount = detectionConfigs.Count;

    items.AddRange(BuildPerPackageItems(detectionConfigs, agentResultMap, version));
    items.Add(new PreCheckItem
    {
        Category = "package",
        Name = $"drift: {presentCount}/{totalCount} packages present",
        Status = presentCount == totalCount ? "passed" : "warning",
        Detail = presentCount == totalCount ? "all packages present" : "drift detected"
    });
}
```

This reuses the existing `BuildPerPackageItems` helper (line 638) and mirrors the drift warning pattern already used in line 600-606. No DB writes, no new abstractions, no frontend changes.
