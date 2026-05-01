# PRD: Workload Run Polish — Uninstall, Pre-Check & UI Revamp

**Date:** 2026-05-01
**Status:** Ready for Implementation
**Owner:** Architecture
**References:** `docs/decisions/workload-run-polish-uninstall-precheck.md` (20 decisions, D1–D20)

---

## Problem Statement

The workload-run subsystem has three critical gaps:

1. **No uninstall capability.** Operators can install and update workloads but cannot tear them down. The `rollback` mode exists but is half-implemented — it only skips init steps and otherwise runs the full install pipeline. The decision docs explicitly state rollback is "not a priority."

2. **No pre-run verification.** The pre-check API (`POST /api/nodes/{id}/prechecks`) returns hardcoded `"passed"` for everything without ever contacting the agent. The agent already has a working `PreCheckProbe` + `PackageDetector` (real filesystem checks), but the orchestrator can't access this data before creating a run. Operators have no way to know what's actually installed on a node before clicking "Run."

3. **The Run Creator UI needs a redesign.** A dropdown with `install | update | rollback` is confusing — the distinction between install and update is implementation detail, not operator intent. The form lacks per-node state visibility, uninstall confirmation, and pre-check feedback. The `forceInstall` naming is misleading.

---

## Solution

Replace `rollback` with a proper `uninstall` pipeline. Add real-time pre-check probing from orchestrator to agent via direct HTTP. Revamp the Run Creator UI around two action buttons (Install / Uninstall) with contextual per-node state badges, reinstall checkbox, and uninstall confirmation.

### End-to-End Flow (Install)

```
Operator opens Run Creator → selects workload
  → UI auto-loads NodeWorkloadState for all nodes (fast DB query)
  → Per-node revision badges appear inline (green/yellow/gray)
  → Operator selects target revision
    → Nodes with same revision: revision disabled unless "Reinstall" checked
    → Nodes with different revision: show current→target mapping
  → [Optional] Operator clicks "Refresh" → orchestrator probes agents via HTTP
    → Agent truth updates DB and UI
  → Operator clicks "Submit"
    → Backend creates per-node WorkloadRunEntity rows
    → Auto-converts mode: install if fresh, update if different revision, install(reinstall) if same
    → Agent polls, claims, executes pipeline (PreCheckProbe skips already-satisfied packages)
```

### End-to-End Flow (Uninstall)

```
Operator opens Run Creator → clicks "Uninstall" tab
  → UI shows only nodes with NodeWorkloadState for selected workload (D18)
  → Per-node "will remove" package list is visible
  → Confirmation step: warning banner + checkbox
    → "I understand that this action cannot be undone."
  → Operator clicks "Submit"
    → Backend creates per-node WorkloadRunEntity rows (mode: "uninstall")
    → Agent polls, claims, executes uninstall pipeline:
      Phase 0: PreCheckProbe (detect what's present)
      Phase 0.5: PreUninstallSteps (revision-level shell commands)
      Phase 1: UninstallPackage (all detected packages, reverse order)
      Phase 3: PostUninstallSteps (revision-level shell commands)
    → On completion: NodeWorkloadState cleared (CurrentRevisionId → null)
```

---

## User Stories

### Uninstall Mode

1. As an operator, I want to select nodes and click "Uninstall" so that I can tear down a workload from specific nodes without reinstalling.
2. As an operator, I want the uninstall pipeline to run revision-level `preUninstallSteps` before removing packages so that I can gracefully stop services first.
3. As an operator, I want the uninstall pipeline to run `postUninstallSteps` after removing packages so that I can clean up configuration files and logs.
4. As an operator, I want to see a confirmation banner listing the packages that will be removed before submitting an uninstall run so that I understand the blast radius.
5. As an operator, I want the uninstall submit button disabled until I check "I understand that this action cannot be undone" so that I can't accidentally trigger teardown.
6. As an operator, I want the node picker in uninstall mode to only show nodes that actually have the workload installed so that I don't waste time selecting nodes with nothing to remove.
7. As an operator, I want the `rollback` mode removed from the codebase entirely so that there is no confusion between rollback (dead code) and uninstall (real feature).

### Pre-Check & Verification

8. As an operator, I want per-node revision state badges to appear when I select a workload in the Run Creator so that I know what's installed before creating a run.
9. As an operator, I want a "Refresh" button that probes agents for actual installed state so that I can verify what's really on the node, not just what the DB says.
10. As an operator, I want packages detected as present (even if the DB didn't know about them) to create `NodeWorkloadState` records so that the orchestrator converges with reality.
11. As an operator, I want packages detected as missing (even if the DB says they're installed) to show a yellow "drift" badge so that I know the DB is stale.
12. As an operator, I want drift to block update runs until reconciled via reinstall so that I don't deploy a diff on top of a broken base.

### UI Revamp

13. As an operator, I want Install and Uninstall to be icon-labeled buttons instead of a mode dropdown so that the action intent is immediately clear.
14. As an operator, I want "Reinstall" checkbox (renamed from "Force reinstall") to appear only when relevant — when any selected node already has the target revision — so that the form stays clean.
15. As an operator, I want "Update" to happen automatically when I pick a different revision in Install mode so that I don't need to think about mode selection.
16. As an operator, I want per-node badges in the summary step showing "Fresh install" (green) or "Update: v1 → v2" (blue) so that I can review what will happen per node before submitting.
17. As an operator creating a single run targeting mixed nodes (some fresh, some updating), I want one `RunId` for traceability with per-node mode auto-conversion done by the backend.

### Uninstall Steps (Import & Pipeline)

18. As a workload author, I want to define `preUninstallSteps` and `postUninstallSteps` in my workload JSON so that I can handle service shutdown, data backup, and cleanup during teardown.
19. As a workload author, I want uninstall step commands to be rejected if empty or exceed 4096 characters so that import validation is consistent with existing init steps.
20. As an operator, I want the initial import endpoints (`WorkloadImportModel`, bulk import, revision create/update) to accept and persist uninstall step fields so that I can import them with the rest of the workload definition.
21. As a test engineer, I want uninstall steps included in `AssignRunPayload` sent to the agent so that the pipeline can execute them.
22. As a test engineer, I want the agent's detect endpoint (`POST /api/detect`) to be callable from orchestrator tests so that pre-check probing can be integration-tested.

### Agent Detect Endpoint

23. As an orchestrator, I want to send a list of package detection configs to the agent and get back per-package presence/version so that I can reconcile DB state with real filesystem state.
24. As an orchestrator, I want the agent detect endpoint to include disk free space so that pre-workload disk checks can be surfaced in the UI.
25. As an agent developer, I want the detect endpoint to reuse the existing `PackageDetector` so that detection logic is consistent between probe-time and pipeline-time.

---

## Implementation Decisions

### Modules Built or Modified

**Frontend (`apps/orchestrator/web/src/`)**

| File | Change |
|---|---|
| `types.ts` | Remove `'rollback'` from `WorkloadRunMode` and `NodeRunState`, add `'uninstall'`. Rename `forceInstall` → `reinstall` in `CreateWorkloadRunRequest`. Add `packageId` to `PreCheckItem`. Add `preUninstallSteps`/`postUninstallSteps` to `WorkloadJsonEntry`. |
| `services/api.ts` | Update `createWorkloadRun` contract. Add pre-check refresh API call. |
| `pages/WorkloadRuns.tsx` | Replace mode dropdown with Install/Uninstall action buttons. Add per-node revision badges. Add Reinstall checkbox (conditional). Add uninstall confirmation banner + checkbox. Add Refresh pre-check button. Add per-node classification badges in summary step. Filter node picker by workload presence in Uninstall mode. |
| `pages/WorkloadRuns.test.tsx` | Update mode assertions from `'rollback'` to `'uninstall'`. |

**Backend (`apps/orchestrator/backend/`)**

| File | Change |
|---|---|
| `Controllers/WorkloadRunsController.cs` | Remove `rollback` from `TryNormalizeMode` and error message. Add per-node auto-conversion (`install` → `update`) when node has different revision (D17). Accept `reinstall` field in request. Emit file revision 3 migration. |
| `Controllers/NodesController.cs` | Replace stub `RunPreChecks` with real agent HTTP probe. Replace `BuildPreCheckSummary` with reconciliation logic against agent response. |
| `Controllers/WorkloadsController.cs` | Add `PreUninstallSteps`/`PostUninstallSteps` to `WorkloadImportModel`. Parse, validate, persist in bulk import and revision create/update endpoints. |
| `Data/Entities/WorkloadRevisionEntity.cs` | Add `PreUninstallStepsJson`, `PostUninstallStepsJson` string columns (JSON `List<string>`, max 4096 chars each). |
| `Data/InstallerDbContext.cs` | Update `CK_WorkloadRuns_Mode` check constraint: `rollback` → `uninstall`. |
| `Services/WorkloadRunDispatcher.cs` | Deserialize and include `PreUninstallSteps`/`PostUninstallSteps` in `AssignRunPayload`. |
| **DB Migration** | Add `PreUninstallStepsJson`, `PostUninstallStepsJson` columns to `WorkloadRevisions` table. Update mode check constraint. |
| `appsettings.json` | Add `AgentProbeTimeoutSeconds: 30`. |

**Agent (`apps/agent/backend/`)**

| File | Change |
|---|---|
| `Pipeline/PipelineExecutor.cs` | Replace `isRollback` with `isUninstall`. Gate init steps on `isUninstall` (skip PreWorkloadSteps, PreInitSteps, PostInitSteps, PostWorkloadSteps). Stage 0.5: execute PreUninstallSteps. Stage 3: execute PostUninstallSteps. |
| `Program.cs` | Add `POST /api/detect` endpoint. |
| New file: `Steps/DetectEndpointHandler.cs` | Inline or separate handler: parse request, call `PackageDetector.DetectAsync()` per package, return `NodeDetectResponse`. |

**Contracts (`shared/contracts/`)**

| File | Change |
|---|---|
| `Runtime/RunPayloads/AssignRunPayload.cs` | Add `PreUninstallSteps`, `PostUninstallSteps` (`List<string>`). |
| New: `Runtime/Probes/DetectRequest.cs` | `PackageDetectionRequest` (packageId, name, version, detection config) and `DetectRequest` (list of packages). |
| New: `Runtime/Probes/DetectResponse.cs` | `PackageDetectionResult` (packageId, name, status, actualVersion) and `NodeDetectResponse` (results, diskInfo). |

**Tests**

| File | Change |
|---|---|
| `tests/agent/integration/PipelineExecutorTests.cs:995,1007` | Replace `"rollback"` mode with `"uninstall"`. |
| `tests/agent/unit/InitStepPipelineTests.cs:351` | Replace `"rollback"` mode with `"uninstall"`. |
| `tests/orchestrator/unit/WorkloadRunsControllerCurrentPackagesTests.cs:453` | Replace `"rollback"` with `"uninstall"`. |
| `tests/orchestrator/unit/` (new) | Add pre-check probe reconciliation tests. |
| `tests/agent/unit/` (new) | Add detect endpoint tests. |

**Documentation**

| File | Change |
|---|---|
| `README.md:300,353,365,392` | Update mode references: `rollback` → `uninstall`. |
| `docs/decisions/workload-differential-update-rollback.md` | Add deprecation note. |
| `docs/decisions/20260430-workload-init-steps/...` | Add note: rollback decisions superseded by `workload-run-polish-uninstall-precheck.md`. |

### Interfaces

**New agent endpoint: `POST /api/detect`**

```
POST http://{node.IpAddress}:5001/api/detect
Content-Type: application/json

{
  "packages": [
    {
      "packageId": "3fa85f64-...",
      "name": "dbeaver",
      "version": "24.3.0",
      "detection": {
        "type": "file",
        "path": "C:\\Program Files\\DBeaver\\dbeaver.exe",
        "expectedVersion": "24.3.0"
      }
    }
  ]
}

Response:
{
  "results": [
    {
      "packageId": "3fa85f64-...",
      "name": "dbeaver",
      "status": "AlreadySatisfied",
      "actualVersion": "24.3.0.0"
    }
  ],
  "diskInfo": {
    "freeBytes": 123456789,
    "totalBytes": 500000000,
    "drive": "C:\\"
  }
}
```

`status` values: `AlreadySatisfied`, `WrongVersion`, `NotPresent`.

**New orchestrator probe call pattern:**

Uses `IHttpClientFactory` to call agent directly. Timeout from config (`AgentProbeTimeoutSeconds`, default 30s).

```csharp
var client = _httpClientFactory.CreateClient();
var url = $"http://{node.IpAddress}:5001/api/detect";
var response = await client.PostAsJsonAsync(url, payload);
```

**Revised `CreateWorkloadRunRequest`** (no breaking changes, internal only):

```
- forceInstall (removed from frontend → backend contract; still accepted in AssignRunPayload for agents)
+ reinstall: boolean (frontend field; maps to forceInstall in AssignRunPayload)
```

**`AssignRunPayload` additions:**

```csharp
public List<string> PreUninstallSteps { get; set; } = new();
public List<string> PostUninstallSteps { get; set; } = new();
```

**Pipeline execution order for new modes:**

```
INSTALL:
  Phase 0: PreCheckProbe (skip if forceInstall)
           DiffEngine
  Phase 0.5: PreWorkloadSteps
  Phase 1: UninstallPackage (removed + UninstallFirst changed)
  Phase 2: PreInitSteps, AcquireArtifact, InstallOrUpgrade,
           PostInstallVerify, PostInitSteps
  Phase 3: PostWorkloadSteps

UPDATE:
  Same as INSTALL, but DiffEngine computes diff with fewer packages

UNINSTALL:
  Phase 0: PreCheckProbe (detect what's present)
  Phase 0.5: PreUninstallSteps (if present, execute; PreWorkloadSteps SKIPPED)
  Phase 1: UninstallPackage (all detected packages, reverse order)
  Phase 2: SKIPPED entirely
  Phase 3: PostUninstallSteps (if present, execute; PostWorkloadSteps SKIPPED)
```

---

## Data Migrations

The DB migration adds two nullable text columns to `WorkloadRevisions`:

```sql
ALTER TABLE "WorkloadRevisions" ADD COLUMN "PreUninstallStepsJson" TEXT NOT NULL DEFAULT '[]';
ALTER TABLE "WorkloadRevisions" ADD COLUMN "PostUninstallStepsJson" TEXT NOT NULL DEFAULT '[]';
```

And updates the mode check constraint on `WorkloadRuns`:

```sql
-- Drop old constraint, add new
-- BEFORE: "Mode" IN ('install','update','rollback','cancel')
-- AFTER:  "Mode" IN ('install','update','uninstall','cancel')
```

Existing rows in `WorkloadRuns` are unaffected (no `rollback` runs exist in PoC data).

---

## Deferred / Out of Scope

- **Registry-based detection** in `PackageDetector` — stub returns `NotPresent` for all registry types. PoC Phase 1 limitation.
- **`WorkloadPreCheck` disk-space validation** wiring into `PipelineExecutor` — exists as code but not called from pipeline.
- **SignalR push** re-enablement — currently disabled, HTTP polling is primary.
- **Rollback-as-snapshot-restore** — deleted entirely. Future rollback would be: uninstall → reinstall.
- **Workload editor** in frontend — no UI for editing workload definitions. Only JSON import exists.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Agent probe timeout or unreachable | Medium | Low | Timeout config (30s), graceful error display in UI |
| Stranded state after partial uninstall | Low | High | Best-effort: uninstall failures reported as run failure; operator re-runs |
| Drift detection false positives | Low | Medium | Same detection logic as pipeline runtime; already validated via PreCheckProbe |
| DB migration on existing data | Low | Low | No rollback runs exist in PoC; default `'[]'` for new columns is safe |
| Frontend state explosion | Medium | Medium | Conditional rendering; only show revision badges when workload selected |

---

## Verification

### Pre-merge

- All existing tests pass with `rollback` replaced by `uninstall`
- New detect endpoint tests pass (agent unit + integration)
- New pre-check reconciliation tests pass (orchestrator unit)
- `dotnet build` succeeds for all three projects
- `pnpm run typecheck` succeeds for web

### Manual smoke test

1. Import a workload JSON with `preUninstallSteps` / `postUninstallSteps`
2. Install the workload on a test node via Run Creator (Install button)
3. Verify revision badges appear in node list
4. Click Refresh to verify pre-check probe works
5. Uninstall the workload (Uninstall button, confirm checkbox)
6. Verify `NodeWorkloadState` cleared, packages removed
7. Reinstall and verify update path works (install to different revision)
8. Verify Reinstall checkbox forces reinstall on same-revision node
