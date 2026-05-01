# Workload Run Polish: Uninstall Mode, Pre-Check & UI Revamp

**Status:** Resolved
**Date:** 2026-05-01
**Scope:** `apps/orchestrator`, `apps/agent`, `shared/contracts`, tests

---

## Context

The workload-run subsystem supports three modes (`install`, `update`, `rollback`) but `rollback` is a dead-end: it only skips init steps and otherwise runs the same pipeline. The admin UI has no uninstall capability, no way to verify what's actually installed on agent nodes, and the pre-check API (`POST /api/nodes/{id}/prechecks`) is a hardcoded stub — it returns `"passed"` for everything without ever contacting the agent.

The agent side, however, already has a working **PreCheckProbe** + **PackageDetector** that performs real filesystem checks (`file`, `version_manifest`, `registry` stubs). This is used during pipeline execution to skip already-satisfied packages and handle version mismatches. The disconnect is that the orchestrator has no access to this data before creating a run.

### Current Architecture (Key Files)

| Component | File | Purpose |
|---|---|---|
| Types (UI) | `apps/orchestrator/web/src/types.ts:103` | `WorkloadRunMode = 'install' \| 'update' \| 'rollback'` |
| Mode dropdown (UI) | `apps/orchestrator/web/src/pages/WorkloadRuns.tsx:24` | `const runModes = ['install', 'update', 'rollback']` |
| Mode validation (backend) | `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs:604-612` | `TryNormalizeMode()` accepts install/update/rollback |
| DB check constraint | `apps/orchestrator/backend/Data/InstallerDbContext.cs:225` | `CK_WorkloadRuns_Mode IN ('install','update','rollback','cancel')` |
| Pipeline executor (agent) | `apps/agent/backend/Pipeline/PipelineExecutor.cs` | `isRollback` flag gates init steps; uninstall phase handles diff |
| Pre-check probe (agent) | `apps/agent/backend/Steps/PreCheckProbe.cs` | Delegates to `PackageDetector.DetectAsync()` |
| Package detector (agent) | `apps/agent/backend/Steps/PackageDetector.cs` | `file` / `registry`(stub) / `version_manifest` detection |
| Pre-check stub (orch) | `apps/orchestrator/backend/Controllers/NodesController.cs:270-315` | `BuildPreCheckSummary()` returns hardcoded `"passed"` |
| Revision entity | `apps/orchestrator/backend/Data/Entities/WorkloadRevisionEntity.cs` | `PreWorkloadStepsJson`, `PostWorkloadStepsJson`, `DefaultShell` |
| Run payload contract | `shared/contracts/Runtime/RunPayloads/AssignRunPayload.cs` | `Mode`, `ForceInstall`, `PreWorkloadSteps`, `PostWorkloadSteps` |
| Workload import model | `apps/orchestrator/backend/Controllers/WorkloadsController.cs:1063-1080` | `WorkloadImportModel` with `PreWorkloadSteps`, `PostWorkloadSteps` |
| Node workload state | `apps/orchestrator/web/src/types.ts:363-368` | `NodeWorkloadAssignment` — what the DB thinks is installed |
| Pre-check result types | `apps/orchestrator/web/src/types.ts:370-388` | `PreCheckItem`, `NodePreCheckSummary`, `NodeDetailResponse` |
| Node run state type | `apps/orchestrator/web/src/types.ts:200-208` | `NodeRunState` includes `'rollback'` |

### Current Pipeline Phases (`PipelineExecutor.cs:22-329`)

```
Phase 0:    PreCheckProbe   — filesystem check per package (skipped if ForceInstall)
            DiffEngine      — compute Added/Removed/Changed/Unchanged (with pre-check overrides)
Phase 0.5:  PreWorkloadSteps — revision-level shell commands (SKIPPED on rollback)
Phase 1:    UninstallPackage — removed + UninstallFirst changed packages, reverse order
Phase 2:    PreInitSteps    — per-package shell commands (SKIPPED on rollback)
            AcquireArtifact — download + SHA256 verify from orchestrator
            InstallOrUpgrade — execute installer
            PostInstallVerify — detect post-install via PackageDetector
            PostInitSteps   — per-package shell commands (SKIPPED on rollback)
Phase 3:    PostWorkloadSteps — revision-level shell commands (SKIPPED on rollback)
```

Rollback currently only differs from install/update by skipping all init steps (line 86):
```csharp
var isRollback = string.Equals(context.Payload.Mode, "rollback", StringComparison.OrdinalIgnoreCase);
```

### Current `WorkloadImportModel` (WorkloadsController.cs:1063-1080)

```csharp
public sealed class WorkloadImportModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string Slug { get; set; } = string.Empty;
    public List<JsonElement>? RawPackages { get; set; }          // "packages"
    public List<JsonElement>? PreWorkloadSteps { get; set; }     // "preWorkloadSteps"
    public List<JsonElement>? PostWorkloadSteps { get; set; }    // "postWorkloadSteps"
    public string? DefaultShell { get; set; }
}
```

No `preUninstallSteps` or `postUninstallSteps` exist today.

---

## Decisions

### D1: Remove Rollback, Add Uninstall Mode

Delete `rollback` from the codebase entirely. Add `uninstall` as a net-new mode that removes all packages in the workload revision from the agent node and clears orchestrator state.

**Rationale:** The decision doc `workload-differential-update-rollback.md` and `workload-init-steps-run-modes-20260430-002400.md` both state rollback is "not a priority" and the intended fallback is "uninstall then reinstall." Rather than keeping a half-implemented rollback mode, we replace it with a proper uninstall mode that serves as the foundation for both teardown and future rollback (uninstall → reinstall).

**Behavioral contract:**
- Uninstall pipeline identifies all packages in the currently installed revision on the agent
- Runs `UninstallPackage.ExecuteAsync()` for each package (using each package's `UninstallArgs` from its `InstallAdapter`)
- Reports step status per package to the orchestrator
- On completion, orchestrator clears `NodeWorkloadState` (`CurrentRevisionId` → null, `PackageStatesJson` → `{}`)
- If any package uninstall fails, the run fails (best-effort, no rollback of uninstalls)

**Code changes:**

`WorkloadRunMode` type (`types.ts:103`):
```typescript
// BEFORE
export type WorkloadRunMode = 'install' | 'update' | 'rollback'
// AFTER
export type WorkloadRunMode = 'install' | 'update' | 'uninstall'
```

`NodeRunState` type (`types.ts:200-208`):
```typescript
// BEFORE
export type NodeRunState =
  | 'idle' | 'install' | 'update' | 'rollback'
  | 'cancel' | 'pending-approval' | 'failed' | 'success'
// AFTER
export type NodeRunState =
  | 'idle' | 'install' | 'update' | 'uninstall'
  | 'cancel' | 'pending-approval' | 'failed' | 'success'
```

`TryNormalizeMode` (`WorkloadRunsController.cs:604-612`):
```csharp
// BEFORE
return normalized switch
{
    "install" or "update" or "rollback" => true,
    _ => false
};
// AFTER
return normalized switch
{
    "install" or "update" or "uninstall" => true,
    _ => false
};
```

Error message updated from `"Mode must be one of: install, update, rollback"` to `"Mode must be one of: install, update, uninstall"`.

DB check constraint (`InstallerDbContext.cs:225`):
```csharp
// BEFORE
t.HasCheckConstraint("CK_WorkloadRuns_Mode", "\"Mode\" IN ('install','update','rollback','cancel')");
// AFTER
t.HasCheckConstraint("CK_WorkloadRuns_Mode", "\"Mode\" IN ('install','update','uninstall','cancel')");
```

Pipeline gating (`PipelineExecutor.cs:86`):
```csharp
// BEFORE
var isRollback = string.Equals(context.Payload.Mode, "rollback", StringComparison.OrdinalIgnoreCase);
// AFTER
var isUninstall = string.Equals(context.Payload.Mode, "uninstall", StringComparison.OrdinalIgnoreCase);
```

The `isUninstall` flag gates:
- **Phase 0.5: PreWorkloadSteps** — skipped on uninstall
- **Phase 2: PreInitSteps** — skipped on uninstall
- **Phase 2: PostInitSteps** — skipped on uninstall
- **Phase 3: PostWorkloadSteps** — skipped on uninstall

The `isUninstall` flag does NOT skip:
- **Phase 0.5: PreUninstallSteps** — EXECUTED on uninstall (runs BEFORE Phase 1)
- **Phase 3: PostUninstallSteps** — EXECUTED on uninstall (runs AFTER Phase 1)

Uninstall pipeline execution order (updated from "Current Pipeline Phases" above):
```
Phase 0:    PreCheckProbe          — detect what's actually on the node (not skipped)
Phase 0.5:  PreUninstallSteps      — revision-level shell commands before package removal
            [PreWorkloadSteps      — SKIPPED]
Phase 1:    UninstallPackage       — uninstall ALL detected packages (reverse PackageIndex order)
Phase 2:    [PreInitSteps          — SKIPPED]
            [AcquireArtifact       — SKIPPED]
            [InstallOrUpgrade      — SKIPPED]
            [PostInstallVerify     — SKIPPED]
            [PostInitSteps         — SKIPPED]
Phase 3:    PostUninstallSteps     — revision-level shell commands after package removal
            [PostWorkloadSteps     — SKIPPED]
```

Uninstall package source: The orchestrator sends the full package list from the node's `CurrentRevisionId` (via `WorkloadPackageEntity` rows) in `AssignRunPayload`. The agent runs `PreCheckProbe` to confirm what's actually present, then uninstalls only those detected packages. Packages from other workloads are untouched.

### D2: Pre-Check — Hybrid Source of Truth

The Run Creator UI shows orchestrator DB state by default (`NodeWorkloadState.CurrentRevisionId`, `PackageStatesJson`). A "Run pre-check" / "Refresh" button triggers a real-time agent probe via direct HTTP.

**Rationale:** The orchestrator DB may not reflect reality (e.g., packages installed outside orchestrator control, manual uninstalls). A real-time probe provides actual filesystem truth. But probing every agent on every modal open would add latency, so DB state is the default fast path with a manual refresh for accuracy.

**Agent truth always wins:** When a probe returns results that differ from DB state, the orchestrator updates DB silently. The UI re-renders from updated DB.

### D3: Pre-Check Scope — Package-Level, Workload-Scoped

The orchestrator sends detection configs only for packages defined across all published workload revisions. The agent returns per-package presence/version. No full filesystem scan — only packages that belong to known workload definitions.

**Rationale:** The agent has no concept of "workloads" — only packages. The orchestrator owns the package→workload mapping. By sending all packages with their `DetectionConfig`, the orchestrator can map per-package results back to workload revisions and determine: "All packages from AmazingWorkload v1 are present → that revision is installed on this node."

**Agent detect endpoint contract:**

`POST http://{node.IpAddress}:5001/api/detect`

Request:
```json
{
  "packages": [
    {
      "packageId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "dbeaver",
      "version": "24.3.0",
      "detection": {
        "type": "file",
        "path": "C:\\Program Files\\DBeaver\\dbeaver.exe",
        "expectedVersion": "24.3.0"
      }
    },
    {
      "packageId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "name": "python",
      "version": "3.13.3",
      "detection": {
        "type": "version_manifest",
        "path": "python",
        "expectedVersion": "3.13.3"
      }
    }
  ]
}
```

Response:
```json
{
  "results": [
    {
      "packageId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "dbeaver",
      "status": "AlreadySatisfied",
      "actualVersion": "24.3.0.0"
    },
    {
      "packageId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "name": "python",
      "status": "NotPresent"
    }
  ],
  "diskInfo": {
    "freeBytes": 123456789,
    "totalBytes": 500000000
  }
}
```

`PreCheckStatus` enum values: `AlreadySatisfied`, `WrongVersion`, `NotPresent`.

### D4: Pre-Check Trigger — Auto-Load + Manual Refresh

When the Run Creator modal opens with a workload selected:
- **Auto-load:** Fetch `NodeWorkloadState` from orchestrator DB for all nodes. Display inline as revision badges.
- **Manual refresh:** "Run pre-check" button sends `POST /api/nodes/{selectedNodeIds}/prechecks` to orchestrator, which calls `POST /api/detect` on each agent, reconciles results, and returns updated state to the UI.

**Rationale:** DB state is fast enough for auto-load on every modal open (one DB query). Real-time probe (HTTP call orchestrator→agent, potentially multiple agents, filesystem checks per package) takes seconds — manual trigger avoids blocking the UX.

### D5: Reconciliation — Agent Truth Wins, Don't Auto-Guess

When a pre-check finds drift, update `PackageStatesJson` to reflect actual detected state. **Do NOT auto-promote `CurrentRevisionId`.** Surface discrepancy as "drift detected" in the UI.

**Scenarios and behavior:**

| Scenario | DB says | Agent says | Action |
|---|---|---|---|
| A — Match | Node has AmazingWorkload v1 | Same version, all packages present | No-op |
| B — Missing (manual uninstall) | Node has v1 | No packages found | Clear `NodeWorkloadState`; show "not installed" |
| C — Pre-existing (outside orchestrator) | Node has nothing | All v1 packages present | Create `NodeWorkloadState` with `CurrentRevisionId = v1.Id`, populate `PackageStatesJson` with detected versions |
| D — Drift (version mismatch) | Node has v1 (dbeaver 24.3.0) | dbeaver 24.3.0 missing, dbeaver 26.0.3 present | Update `PackageStatesJson` to reflect actual state; keep `CurrentRevisionId = v1`; show "drift detected" badge in UI |
| E — Drift (partial) | Node has v1 (python+dbeaver) | python OK, dbeaver missing | Update `PackageStatesJson`: mark dbeaver as missing; show "drift: 1/2 packages present" |

**Scenario D and E block update mode** — if a package is missing from a claimed revision, the orchestrator refuses an update run. The admin must reinstall (check "Reinstall" in Install mode) to reconcile first.

### D6: UI Node State — Contextual Per-Node Columns

When a workload is selected in the Run Creator, the node list shows per-node revision state inline:

| Display | Meaning |
|---|---|
| `v1.0.0` (green badge) | Node has this revision, all packages verified present |
| `v1.0.0` (yellow badge) | Node has this revision, but pre-check detected drift |
| `not installed` (gray) | Node has no `NodeWorkloadState` for this workload |
| `checking...` (spinner) | Pre-check probe in progress |

The current node list in `WorkloadRuns.tsx` shows hostname, OS version, and online status. This change adds a new column/dimension that appears when a workload is selected.

### D7: Install Mode Revisions — Show All, Disable Installed

All published revisions are shown in the revision dropdown. If a selected node already has the selected revision, that revision is disabled (grayed out) in the dropdown **unless** the "Reinstall" checkbox is checked.

This prevents accidentally reinstalling the same version on nodes that already have it, while still allowing a force-reinstall when needed (e.g., repair scenario, drift recovery).

### D8: UI Modes — Two Modes: Install + Uninstall

The Run Creator UI shows two action buttons/tabs: **Install** and **Uninstall**.

- **Install** = ensure a revision is present on nodes. The agent's `PreCheckProbe` skips packages already at the correct version. When the admin picks a different revision than what's currently on the node, the orchestrator auto-converts to `mode: "update"` behind the scenes (see D9).
- **Uninstall** = tear down the workload from selected nodes. The revision dropdown is hidden (irrelevant — the system uninstalls whatever is currently installed per-node). Confirmation step required (see D13).

Update is NOT a separate UI mode. It is triggered implicitly when Install mode targets a different revision on a node that already has the workload.

### D9: Backend Keeps Update Mode (Internal Only)

The `runs` table's `Mode` column retains `install`, `update`, `uninstall`, `cancel`. The UI presents two modes (Install/Uninstall) but the backend auto-converts to `update` when:

```
mode === "install" AND node.NodeWorkloadState.CurrentRevisionId != null AND node.NodeWorkloadState.CurrentRevisionId != targetRevisionId
```

This preserves the diff optimization in `WorkloadRunsController.GetSteps()`:
```csharp
// When mode is "update", compute diff between current and target revisions
// When mode is "install", send all packages (PreCheckProbe handles skipping)
// When mode is "uninstall", use the current revision as the source for what to remove
```

The diff engine (`DiffEngine.ComputeDiff`) computes Added/Removed/Changed/Unchanged, and `UpgradeBehavior` (`InPlace` vs `UninstallFirst`) handles the uninstall-before-install dance for version changes.

### D10: Reinstall Checkbox — Install Mode Only

A "Reinstall" checkbox appears in Install mode when any selected node already has the selected revision. Checking it:
- Sends `forceInstall: true` in the `CreateWorkloadRunRequest`
- On the agent side, `ForceInstall: true` causes the pipeline to **skip Phase 0 PreCheckProbe** entirely
- All packages are installed regardless of what's already present
- Renamed from "Force reinstall" to "Reinstall"

Example form state:
```typescript
const [form, setForm] = useState({
  workloadId: '',
  revisionId: '',
  mode: 'install' as WorkloadRun['mode'],  // or 'uninstall'
  targetNodeIds: [] as string[],
  reinstall: false,                          // renamed from forceInstall
})
```

The checkbox UI is conditionally rendered:
```tsx
{form.mode === 'install' &&
 selectedNodes.some(node => nodeInstalledRevision(node) === form.revisionId) && (
  <label>
    <input type="checkbox" checked={form.reinstall} onChange={...} />
    Reinstall — force re-install even if already present
  </label>
)}
```

### D11: PreUninstallSteps / PostUninstallSteps — Add Now

New fields on `WorkloadRevisionEntity`, same shape as `PreWorkloadStepsJson`/`PostWorkloadStepsJson`:

```csharp
// WorkloadRevisionEntity.cs — AFTER
public sealed class WorkloadRevisionEntity
{
    public Guid RevisionId { get; set; } = Guid.NewGuid();
    public Guid WorkloadId { get; set; }
    public WorkloadDefinitionEntity Workload { get; set; } = null!;
    public string Version { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string PreWorkloadStepsJson { get; set; } = "[]";
    public string PostWorkloadStepsJson { get; set; } = "[]";
    public string PreUninstallStepsJson { get; set; } = "[]";   // NEW
    public string PostUninstallStepsJson { get; set; } = "[]";  // NEW
    public string DefaultShell { get; set; } = "powershell";
    public List<WorkloadPackageEntity> Packages { get; set; } = new();
}
```

`WorkloadImportModel` — new JSON properties:

```csharp
public sealed class WorkloadImportModel
{
    // ... existing fields ...
    [JsonPropertyName("preUninstallSteps")]
    public List<JsonElement>? PreUninstallSteps { get; set; }   // NEW
    [JsonPropertyName("postUninstallSteps")]
    public List<JsonElement>? PostUninstallSteps { get; set; }  // NEW
}
```

Sample JSON manifest entry with the new fields:
```json
{
  "name": "Dev Tools Stack",
  "slug": "dev-tools-stack",
  "version": "1.0.0",
  "preWorkloadSteps": ["Write-Host 'Preparing...'"],
  "postWorkloadSteps": ["Write-Host 'Done.'"],
  "preUninstallSteps": ["Write-Host 'Stopping services...'", "Stop-Service MyService"],
  "postUninstallSteps": ["Write-Host 'Cleanup complete.'"],
  "packages": ["nodejs-22.14.0", "python-3.13.3"]
}
```

`AssignRunPayload` — new fields:

```csharp
public sealed class AssignRunPayload
{
    // ... existing fields ...
    public List<string> PreWorkloadSteps { get; set; } = new();
    public List<string> PostWorkloadSteps { get; set; } = new();
    public List<string> PreUninstallSteps { get; set; } = new();   // NEW
    public List<string> PostUninstallSteps { get; set; } = new();  // NEW
    public string DefaultShell { get; set; } = "powershell";
    // ...
}
```

Pipeline execution order for uninstall mode:
```
Phase 0:    PreCheckProbe   — detect what's actually on the node
Phase 0.5:  PreUninstallSteps — revision-level shell commands BEFORE package removal (if present)
Phase 1:    UninstallPackage — uninstall ALL detected packages (reverse PackageIndex order)
Phase 2:    [SKIPPED — no install happens]
Phase 3:    PostUninstallSteps — revision-level shell commands AFTER package removal (if present)
```

### D12: UI Action Buttons — Replace Mode Dropdown

Replace the current `<select>` dropdown (`const runModes = ['install', 'update', 'rollback']`) with three icon-labeled action buttons:

```tsx
// BEFORE (WorkloadRuns.tsx:24)
const runModes: Array<WorkloadRun['mode']> = ['install', 'update', 'rollback']

// AFTER
<div className="flex gap-2 mb-4">
  <button
    className={`px-4 py-2 rounded ${form.mode === 'install' ? 'bg-blue-600 text-white' : 'bg-gray-100'}`}
    onClick={() => setForm(f => ({ ...f, mode: 'install' }))}
  >
    📥 Install
  </button>
  <button
    className={`px-4 py-2 rounded ${form.mode === 'uninstall' ? 'bg-red-600 text-white' : 'bg-gray-100'}`}
    onClick={() => setForm(f => ({ ...f, mode: 'uninstall' }))}
  >
    🗑️ Uninstall
  </button>
</div>
```

Each button adapts the form:
- **Install**: Full form (workload → revision → nodes → reinstall checkbox)
- **Uninstall**: Simplified form (workload → nodes — shows what's installed with confirmation)

### D13: Uninstall Confirmation — Required

A warning banner appears in the summary/confirm step when mode is "uninstall":

```tsx
{form.mode === 'uninstall' && showSummary && (
  <div className="bg-red-50 border border-red-300 rounded p-4 mb-4">
    <strong>Warning: This will permanently remove packages from nodes.</strong>
    <p>The following packages will be uninstalled from {selectedNodeCount} node(s):</p>
    <ul className="list-disc ml-6 mt-2">
      {workloadPackages.map(pkg => (
        <li key={pkg.packageId}>{pkg.packageName} {pkg.packageVersion}</li>
      ))}
    </ul>
    <label className="mt-4 block">
      <input type="checkbox" checked={confirmed} onChange={...} />
      I understand that this action cannot be undone.
    </label>
  </div>
)}
```

The submit button is disabled until the confirmation checkbox is checked.

### D14: Update Triggered Implicitly

No separate "Update" button in the runs table or node detail page. Update is triggered when:
1. Admin opens the Run Creator, selects Install mode
2. Selects a workload where nodes already have a different revision
3. Selects a different target revision
4. The orchestrator auto-converts `mode: "install"` → `mode: "update"` for those nodes

The UI can indicate this implicitly by showing the current→target mapping:
```
Node "win-prod-01": Amaz▸ v1.0.0 → v2.0.0 (update)
Node "win-prod-02": Amaz▸ not installed → v2.0.0 (fresh install)
```

No additional UI controls needed — the form handles both fresh installs and updates in one flow.

**Per-node classification badges** in the summary/confirm step make the distinction explicit:
- 🟢 `Fresh install` badge — node has no prior revision
- 🔵 `Update: v1.0.0 → v2.0.0` badge — node already has the workload, will be updated
This keeps the Install button clean while surfacing the actual action for review before submission.

### D15: Probe Communication — Direct HTTP

The orchestrator probes agents via direct HTTP. A new endpoint is added to the agent.

**Agent endpoint:** `POST /api/detect` (added to `apps/agent/backend/Program.cs`)

**Orchestrator HTTP call:** Uses `IHttpClientFactory` to call `http://{node.IpAddress}:5001/api/detect`

```csharp
// In a new service or NodesController
public async Task<NodeDetectResponse> ProbeNodeAsync(NodeEntity node, List<PackageDetectionRequest> packages)
{
    var client = _httpClientFactory.CreateClient();
    var url = $"http://{node.IpAddress}:5001/api/detect";
    var payload = new { packages };
    var response = await client.PostAsJsonAsync(url, payload);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<NodeDetectResponse>();
}
```

**Rationale:** Synchronous, simple. The agent is on a trusted network (PoC phase). SignalR is already disabled in favor of HTTP polling, so adding another HTTP endpoint is consistent with the current communication model. The agent already exposes `/health` on port 5001.

### D16: Reconciliation Detail — Surface Truth, Don't Auto-Guess

When pre-check finds drift, `PackageStatesJson` is updated with actual detected state but `CurrentRevisionId` is NOT changed. The UI shows "drift detected."

**Example flow:**

1. DB says Node X has AmazingWorkload v1 (`CurrentRevisionId = v1.Id`)
2. Agent probe returns: `python`=AlreadySatisfied(v3.13.3), `dbeaver`=NotPresent (was 24.3.0)
3. Orchestrator updates `PackageStatesJson`:
   ```json
   {
     "python": {"status": "present", "version": "3.13.3"},
     "dbeaver": {"status": "missing", "expectedVersion": "24.3.0"}
   }
   ```
4. `CurrentRevisionId` stays `v1.Id` (no auto-promotion)
5. UI shows: yellow "drift" badge next to v1.0.0

The admin can then:
- **Reinstall** (Install mode with Reinstall checked) → forces dbeaver 24.3.0 back
- **Update** (Install mode to v2 that has dbeaver 26.0.3) → but drift blocks this until reconciled

### D17: Mixed-Mode Runs — Per-Node Auto-Conversion at Dispatch

A single run can contain a mix of `install` and `update` nodes. The frontend sends `mode: "install"` but the dispatcher inspects each target node's `NodeWorkloadState` at creation time and sets `mode` per `WorkloadRunEntity` row:

| Node State | Dispatched Mode | Behavior |
|---|---|---|
| `CurrentRevisionId == null` | `"install"` | Fresh install — all packages sent |
| `CurrentRevisionId == targetRevisionId` | `"install"` (with Reinstall) | Force reinstall — PreCheckProbe skipped |
| `CurrentRevisionId != targetRevisionId` | `"update"` | Diff optimization — only changed packages sent |

**Rationale:** A single admin action should produce one `RunId` for traceability, but the pipeline execution must differ per node. Per-row mode preserves diff optimization (reducing network transfer and install time) without requiring the admin to manually split runs.

**Note:** Uninstall does not need auto-conversion — all selected nodes run `mode: "uninstall"` regardless of current state.

### D18: Uninstall Node Filtering — Hide Nodes Without Workload

In Uninstall mode, the node picker only shows nodes that have `NodeWorkloadState` for the selected workload. Nodes without the workload are hidden entirely.

If no nodes qualify, the Uninstall button is disabled with a tooltip: "No nodes have this workload installed."

**Rationale:** Uninstall is a targeted teardown action. Showing nodes where nothing can be removed adds noise and risk of confusion. Install mode continues to show all online nodes.

### D19: Uninstall Warning — Package List from Revision

The uninstall confirmation banner explicitly lists the packages that will be removed, sourced from the revision's `WorkloadPackageEntity` rows:

```
Warning: This will permanently remove packages from N node(s).
Packages to be uninstalled:
  - nodejs 22.14.0
  - python 3.13.3
```

If selected nodes have different currently-installed revisions, the banner shows the per-node revision and its packages grouped by node.

### D20: D1 Pipeline Clarification (Doc Fix)

PreUninstallSteps and PostUninstallSteps execute during uninstall mode (not skipped). Clarified in D1's behavioral contract above. This is a documentation correction, not a new decision.

---

## Implementation Scope

### Frontend (`apps/orchestrator/web/src/`)

| # | File | Change |
|---|---|---|
| 1 | `types.ts:103` | `WorkloadRunMode`: remove `'rollback'`, add `'uninstall'` |
| 2 | `types.ts:200-208` | `NodeRunState`: remove `'rollback'`, add `'uninstall'` |
| 3 | `types.ts:277-297` | `CreateWorkloadRunRequest`: rename `forceInstall` → `reinstall` (or keep both with backward compat) |
| 4 | `types.ts:355-361` | `WorkloadJsonEntry`: add `preUninstallSteps`, `postUninstallSteps` |
| 5 | `types.ts:370-383` | `PreCheckItem`: add `packageId` field for package-level probe results |
| 6 | `services/api.ts` | Update `createWorkloadRun` to send new mode values; add `runNodePreChecks` to accept package list |
| 7 | `pages/WorkloadRuns.tsx:24` | Replace `runModes` array with action buttons (Install/Uninstall) |
| 8 | `pages/WorkloadRuns.tsx:37-43` | Update `form` state: rename `forceInstall` → `reinstall` |
| 9 | `pages/WorkloadRuns.tsx` | Add per-node revision state display when workload selected |
| 10 | `pages/WorkloadRuns.tsx` | Add Reinstall checkbox (conditional visibility) |
| 11 | `pages/WorkloadRuns.tsx` | Add uninstall confirmation banner with checkbox |
| 12 | `pages/WorkloadRuns.tsx` | Add pre-check trigger button and result display |
| **13a** | `pages/WorkloadRuns.tsx` | Uninstall mode: filter node picker to only show nodes with the workload (D18) |
| 13b | `pages/WorkloadRuns.test.tsx:123` | Fix `'rollback'` assertion → `'uninstall'` |

### Backend (`apps/orchestrator/backend/`)

| # | File | Change |
|---|---|---|
| 14 | `Controllers/WorkloadRunsController.cs:53` | Error message: replace `"rollback"` with `"uninstall"` |
| 15 | `Controllers/WorkloadRunsController.cs:604-612` | `TryNormalizeMode`: replace `"rollback"` with `"uninstall"` |
| 16 | `Controllers/WorkloadRunsController.cs` | Add per-node auto-convert at creation time: `install` → `update` per-row when node has different revision (D17) |
| 17 | `Controllers/NodesController.cs:251-268` | `RunPreChecks()`: replace stub with real agent HTTP probe |
| 18 | `Controllers/NodesController.cs:270-315` | `BuildPreCheckSummary()`: replace with `ReconcileProbeResults()` using agent response |
| 19 | `Controllers/WorkloadsController.cs:1063-1080` | `WorkloadImportModel`: add `PreUninstallSteps`, `PostUninstallSteps` |
| 20 | `Controllers/WorkloadsController.cs` | Bulk import: parse, validate, persist new uninstall step fields |
| 21 | `Data/Entities/WorkloadRevisionEntity.cs` | Add `PreUninstallStepsJson`, `PostUninstallStepsJson` |
| 22 | `Data/InstallerDbContext.cs:225` | Update `CK_WorkloadRuns_Mode` check constraint |
| 23 | `Services/WorkloadRunDispatcher.cs` | Deserialize and include new uninstall step fields in `AssignRunPayload` |
| 24 | **DB migration** | New migration: add columns `PreUninstallStepsJson`, `PostUninstallStepsJson` to `WorkloadRevisions`; update check constraint |
| 25 | `appsettings.json` / config | Add agent probe timeout setting (e.g., `"AgentProbeTimeoutSeconds": 30`) |

### Agent (`apps/agent/backend/`)

| # | File | Change |
|---|---|---|
| 26 | `Pipeline/PipelineExecutor.cs:86` | Replace `isRollback` with `isUninstall` |
| 27 | `Pipeline/PipelineExecutor.cs` | Add uninstall-specific pipeline path (PreUninstallSteps → uninstall all → PostUninstallSteps) |
| 28 | `Program.cs` | Add `app.MapPost("/api/detect", ...)` endpoint |
| 29 | New file: `Steps/DetectEndpointHandler.cs` | (or inline) Parse detect request, call `PackageDetector.DetectAsync()` per package, return results |

### Contracts (`shared/contracts/`)

| # | File | Change |
|---|---|---|
| 30 | `Runtime/RunPayloads/AssignRunPayload.cs` | Add `PreUninstallSteps`, `PostUninstallSteps` |
| 31 | New file: `Runtime/Probes/DetectRequest.cs` | `PackageDetectionRequest` + `NodeDetectResponse` DTOs |
| 32 | New file: `Runtime/Probes/DetectResponse.cs` | `PackageDetectionResult` + `DiskInfo` DTOs |

### Tests

| # | File | Change |
|---|---|---|
| 33 | `tests/agent/integration/PipelineExecutorTests.cs:995,1007` | Replace `"rollback"` mode with `"uninstall"` |
| 34 | `tests/agent/unit/InitStepPipelineTests.cs:351` | Replace `"rollback"` mode with `"uninstall"` |
| 35 | `tests/orchestrator/unit/WorkloadRunsControllerCurrentPackagesTests.cs:453` | Replace `"rollback"` with `"uninstall"` |
| 36 | `tests/orchestrator/unit/` | Add pre-check probe reconciliation tests |
| 37 | `tests/agent/unit/` | Add detect endpoint tests |

### Documentation

| # | File | Change |
|---|---|---|
| 38 | `README.md:300,353,365,392` | Update mode references: `rollback` → `uninstall` |
| 39 | `docs/decisions/workload-differential-update-rollback.md` | Add note: deprecated, replaced by this doc |
| 40 | `docs/decisions/20260430-workload-init-steps/...` | Add note: rollback decisions superseded |

---

## Deferred / Out of Scope

- **Registry-based detection** in `PackageDetector` (currently stub at `PackageDetector.cs:85-90` — returns `NotPresent` for all registry detections). PoC Phase 1 limitation.
- **`WorkloadPreCheck` disk-space validation** wiring into `PipelineExecutor` — exists as code but not called from pipeline.
- **SignalR push** re-enablement — currently disabled, HTTP polling is primary.
- **Rollback-as-snapshot-restore** — deleted entirely. Future rollback would be: uninstall → reinstall (now possible with uninstall mode).
- **Workload editor** in frontend — no UI for editing workload definitions. Only JSON import exists.
