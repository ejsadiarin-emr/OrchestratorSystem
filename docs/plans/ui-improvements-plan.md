# UI Improvements Implementation Plan

## Overview

This plan covers four UI improvement work-streams for the Orchestrator Web application:

1. **Home page (`/`)** — Color-code status values in table rows (health, risk level, run state, etc.).
2. **Upload UX (`/workloads` & `/artifacts`)** — Replace pretentious progress bars/steppers with a simple loading spinner.
3. **Workloads page (`/workloads`)** — Make "viewing existing workloads" the first-class UI element instead of the bulk-import dropzone.
4. **Node Details (`/nodes`)** — Add clickable rows that open a "Node Details" modal showing workloads, pre-check results (installed packages & versions, disk space, OS), and a manual "Run Pre-check" button. Pre-checks should also auto-run when the modal opens.

---

## Backend Gap Analysis

| Endpoint | Status | Notes |
|----------|--------|-------|
| `GET /api/nodes` | **Exists** | Lists all nodes; used by `/nodes` and Dashboard. |
| `GET /api/nodes/{id}` | **Exists** | Returns `Node` model (`Hostname`, `IpAddress`, `Status`, `OsVersion`, `AgentVersion`, `LastSeenAt`, etc.). **Currently unused by frontend.** |
| `GET /api/nodes/workload-states` | **Exists** | Returns workload states per node. |
| `GET /api/nodes/{id}/prechecks` | **Missing** | No endpoint exists to fetch or trigger pre-check results for a node. |
| `POST /api/nodes/{id}/prechecks` | **Missing** | No endpoint exists to manually run pre-checks on demand. |

### Pre-checks Context

- The **agent** backend has internal pre-check logic (`WorkloadPreCheck`, `PreCheckProbe`), but the **orchestrator** backend does not expose this data.
- The orchestrator `NodeEntity` already stores `OsVersion`, `AgentVersion`, `Status`, and related `NodeWorkloadStates`, but it does **not** store disk-space or installed-package snapshots.
- Therefore, **Phase 1 must add new backend endpoints and data models** to support the Node Details modal.

---

## Phase 1: Backend — Node Details & Pre-checks API

### 1.1 Data Model Additions

**`apps/orchestrator/backend/Models/Node.cs`**

Add new request/response models:

```csharp
public class NodeDetailResponse : Node
{
    public List<NodeWorkloadAssignment> Workloads { get; set; } = new();
    public NodePreCheckSummary? LatestPreCheck { get; set; }
}

public class NodeWorkloadAssignment
{
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // running, pending, etc.
}

public class NodePreCheckSummary
{
    public DateTime RunAt { get; set; }
    public string OverallStatus { get; set; } = string.Empty; // passed, failed, warning
    public List<PreCheckItem> Items { get; set; } = new();
}

public class PreCheckItem
{
    public string Category { get; set; } = string.Empty; // disk, os, package, agent
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // passed, failed, warning
    public string? Detail { get; set; }
    public string? ExpectedVersion { get; set; }
    public string? ActualVersion { get; set; }
}

public class RunPreCheckRequest
{
    public Guid NodeId { get; set; }
}
```

### 1.2 Database Migration (Optional but Recommended)

If we want to persist pre-check results (so the modal can show history without re-running), add a new table:

**`apps/orchestrator/backend/Data/Entities/NodePreCheckResultEntity.cs`**

```csharp
public class NodePreCheckResultEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NodeId { get; set; }
    public DateTime RunAtUtc { get; set; } = DateTime.UtcNow;
    public string OverallStatus { get; set; } = string.Empty;
    public string ResultsJson { get; set; } = "[]"; // serialized List<PreCheckItem>
}
```

Add `DbSet<NodePreCheckResultEntity> NodePreCheckResults { get; set; }` to `InstallerDbContext` and create an EF migration.

> **Decision needed:** Should pre-checks be persisted, or computed on-demand via SignalR/command to the agent? For the first iteration, **persisting mocked/synthetic results** is simplest and avoids building a full command-and-response bus to the agent.

### 1.3 Controller Endpoints

**`apps/orchestrator/backend/Controllers/NodesController.cs`**

Add:

```csharp
// GET /api/nodes/{id:guid}/details
[HttpGet("{id:guid}/details")]
public async Task<ActionResult<NodeDetailResponse>> GetDetails(Guid id)
{
    // 1. Fetch node base info
    // 2. Fetch assigned workloads via NodeWorkloadStates + Workload names
    // 3. Fetch latest PreCheckResult for this node (or synthesize from node data)
    // Return enriched NodeDetailResponse
}

// POST /api/nodes/{id:guid}/prechecks
[HttpPost("{id:guid}/prechecks")]
public async Task<ActionResult<NodePreCheckSummary>> RunPreChecks(Guid id)
{
    // 1. Verify node exists
    // 2. Synthesize pre-check results from node data (OS, agent version, disk-space placeholder)
    //    OR trigger an async job if we have agent command capability.
    // 3. Save results to NodePreCheckResultEntity
    // 4. Return the summary
}
```

### 1.4 Frontend API Layer

**`apps/orchestrator/web/src/services/api.ts`**

Replace the mock `runAgentPrecheck()` with real calls:

```typescript
export async function getNodeDetails(nodeId: string): Promise<NodeDetailResponse> {
  const res = await fetch(`${API_BASE}/api/nodes/${nodeId}/details`)
  if (!res.ok) throw new Error(`Failed to fetch node details: ${res.status}`)
  return res.json()
}

export async function runNodePreChecks(nodeId: string): Promise<NodePreCheckSummary> {
  const res = await fetch(`${API_BASE}/api/nodes/${nodeId}/prechecks`, { method: 'POST' })
  if (!res.ok) throw new Error(`Failed to run pre-checks: ${res.status}`)
  return res.json()
}
```

Add corresponding types to **`apps/orchestrator/web/src/types.ts`**:

```typescript
export interface NodeWorkloadAssignment {
  workloadId: string
  workloadName: string
  revision: string
  status: string
}

export interface PreCheckItem {
  category: string
  name: string
  status: 'passed' | 'failed' | 'warning'
  detail?: string
  expectedVersion?: string
  actualVersion?: string
}

export interface NodePreCheckSummary {
  runAt: string
  overallStatus: 'passed' | 'failed' | 'warning'
  items: PreCheckItem[]
}

export interface NodeDetailResponse extends Node {
  workloads: NodeWorkloadAssignment[]
  latestPreCheck?: NodePreCheckSummary
}
```

---

## Phase 2: Home Page (`/`) — Color Coding

**Target file:** `apps/orchestrator/web/src/pages/Dashboard.tsx`

### 2.1 Nodes Live Table

Current columns rendered as plain text or unstyled spans:
- `node.health` — values: `online` | `offline` | `warning`
- `node.riskLevel` — values: `low` | `med` | `high` (currently hardcoded to `'low'` in `api.ts`)
- `node.runState` — not shown in this table but available in drawer
- `node.reasonCode` — plain text

**Implementation:**

Create a small reusable `StatusBadge` component (or use the existing `Badge` from `components/ui/badge.tsx`):

```tsx
// Health badge
const healthColor: Record<NodeHealth, string> = {
  online:  'bg-emerald-100 text-emerald-800',
  warning: 'bg-amber-100 text-amber-800',
  offline: 'bg-red-100 text-red-800',
}

// Risk badge
const riskColor: Record<RiskLevel, string> = {
  low:  'bg-blue-100 text-blue-800',
  med:  'bg-amber-100 text-amber-800',
  high: 'bg-red-100 text-red-800',
}
```

Update table cells (~lines 277-299):
- **Health**: wrap in `<span className={healthColor[node.health]}>` (pill/badge style).
- **Risk Level**: wrap in `<span className={riskColor[node.riskLevel]}>`.
- **Workload Updates**: `IndicatorBadge` already uses color; verify contrast is sufficient.
- **Reason Code**: if `reasonCode` implies an error, add a subtle red tint.

### 2.2 Workloads Overview Table

- **Revision Updates / Package Update Signals**: already have some color via `IndicatorBadge`; ensure consistency with the new palette.

### 2.3 KPI Cards

- Ensure KPI values (e.g., `nodesOffline`, `failedRuns24h`) use the same semantic colors when highlighted.

---

## Phase 3: Upload UX — Loading Spinners

### 3.1 Artifacts (`/artifacts`)

**Target file:** `apps/orchestrator/web/src/pages/ArtifactStore.tsx`

Current behavior (lines ~560-566):
```tsx
{(isUploading || uploadProgress > 0 || uploadStatus) && (
  <div className="space-y-3 rounded-lg border ... p-4">
    <Progress value={uploadProgress} />
    <Stepper steps={ingestSteps} activeStep={uploadStep} />
    <p className="text-sm text-[var(--text-soft)]">{uploadStatus}</p>
  </div>
)}
```

**Change to:**
```tsx
{isUploading && (
  <div className="flex items-center justify-center gap-3 rounded-lg border ... p-4">
    <Loader2 className="h-5 w-5 animate-spin text-[var(--accent)]" />
    <span className="text-sm text-[var(--text-soft)]">Uploading...</span>
  </div>
)}
```

- Remove `uploadProgress`, `uploadStep`, `uploadStatus` UI state from the render.
- Keep the underlying upload logic (`uploadArtifactWithProgress`, chunked upload); just **do not surface progress % or step labels** to the user.
- The `isUploading` boolean is already managed correctly.

### 3.2 Workloads (`/workloads`)

**Target file:** `apps/orchestrator/web/src/pages/Workloads.tsx`

Current behavior (lines ~77, ~269-288):
- `isBulkImporting` boolean disables the Import button and changes text to `"Importing..."`.

**Change to:**
- While `isBulkImporting` is true, show a centered spinner overlay or inline spinner inside the dropzone/results area instead of just disabled-button text.
- Use the same `Loader2` spinner pattern for visual consistency.

---

## Phase 4: Workloads Page (`/workloads`) — First-Class Workload Viewing

**Target file:** `apps/orchestrator/web/src/pages/Workloads.tsx`

Current layout order:
1. Bulk Import Dropzone (hero section, very large)
2. Bulk Import Results
3. Create Revision Modal (triggered elsewhere)
4. Definitions / Latest Revision Card Grid

**Desired layout order:**
1. **Existing Workloads Card Grid** — move to the top. Add a clear header: "Workload Definitions".
2. **Create / Import Actions** — move below the grid as a secondary toolbar or compact section.
   - Keep the dropzone but make it smaller / collapsible, or place it in an "Import" subsection.
3. **Bulk Import Results** — appear below the dropzone only when active.

**Implementation steps:**

1. Re-order JSX sections so the card grid (`md:grid-cols-2 lg:grid-cols-3`) renders first.
2. Add a section header with count: `<h2>Workload Definitions ({workloads.length})</h2>`.
3. Convert the bulk-import dropzone into a compact card or an expandable panel rather than the full-width hero.
4. Ensure the "Import" button and drag-and-drop zone are still accessible but visually subordinate.

---

## Phase 5: Node Details Modal (`/nodes`)

**Target file:** `apps/orchestrator/web/src/pages/Nodes.tsx`

### 5.1 Make Rows Clickable

Current `Nodes.tsx` table rows are not clickable (no `onClick` handler and no cursor style).

- Add `cursor-pointer` and `hover:bg-[var(--surface-subtle)]` to each `<tr>`.
- Add `onClick={() => openNodeDetails(node.id)}`.

### 5.2 Create `NodeDetailsModal` Component

**New file:** `apps/orchestrator/web/src/components/NodeDetailsModal.tsx`

Props:
```typescript
interface NodeDetailsModalProps {
  nodeId: string | null
  open: boolean
  onClose: () => void
}
```

**Content sections:**

1. **Node Header** — Display name, hostname, IP, status badge, OS version, agent version.
2. **Workloads Tab** — List of `NodeWorkloadAssignment` fetched from `getNodeDetails`.
   - Columns: Workload Name, Revision, Status.
3. **Pre-checks Tab** — Show `latestPreCheck` items.
   - Display each `PreCheckItem` as a row with an icon:
     - `passed` → green check
     - `warning` → amber alert
     - `failed` → red X
   - Show `expectedVersion` vs `actualVersion` for package checks.
   - Show `detail` text (e.g., disk-space numbers).
4. **Run Pre-check Button** — Floating button or footer action:
   - On click: call `runNodePreChecks(nodeId)`.
   - While running: show spinner inside the button.
   - On completion: refresh `getNodeDetails` to display new results.

### 5.3 Auto-run Pre-checks on Open

Inside `NodeDetailsModal`, in a `useEffect` keyed by `nodeId`:

```typescript
useEffect(() => {
  if (!open || !nodeId) return
  // If no pre-checks exist, or if we always want fresh data:
  runNodePreChecks(nodeId)
    .then(summary => setPreCheckSummary(summary))
    .catch(() => { /* silently fail or show toast */ })
}, [open, nodeId])
```

> **UX note:** If auto-run takes time, show a skeleton or spinner in the Pre-checks tab so the modal does not feel empty.

### 5.4 Integrate into `Nodes.tsx`

- Add state: `const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)`.
- Render `<NodeDetailsModal nodeId={selectedNodeId} open={!!selectedNodeId} onClose={() => setSelectedNodeId(null)} />` at the bottom of the page.

---

## File Change Summary

| File | Change Type | Description |
|------|-------------|-------------|
| `apps/orchestrator/backend/Models/Node.cs` | Add | `NodeDetailResponse`, `NodeWorkloadAssignment`, `NodePreCheckSummary`, `PreCheckItem`, `RunPreCheckRequest` |
| `apps/orchestrator/backend/Data/Entities/NodePreCheckResultEntity.cs` | **Create** | New entity for persisted pre-check results (optional) |
| `apps/orchestrator/backend/Data/InstallerDbContext.cs` | Modify | Add `DbSet<NodePreCheckResultEntity>` (optional) |
| `apps/orchestrator/backend/Controllers/NodesController.cs` | Modify | Add `GetDetails` and `RunPreChecks` endpoints |
| `apps/orchestrator/web/src/types.ts` | Modify | Add `NodeDetailResponse`, `NodePreCheckSummary`, `PreCheckItem`, `NodeWorkloadAssignment` |
| `apps/orchestrator/web/src/services/api.ts` | Modify | Add `getNodeDetails()` and `runNodePreChecks()`; remove mock `runAgentPrecheck()` |
| `apps/orchestrator/web/src/pages/Dashboard.tsx` | Modify | Color-code health, risk level, and status cells |
| `apps/orchestrator/web/src/pages/ArtifactStore.tsx` | Modify | Replace progress bar + stepper with loading spinner |
| `apps/orchestrator/web/src/pages/Workloads.tsx` | Modify | Reorder layout; add spinner to bulk import; make existing workloads primary |
| `apps/orchestrator/web/src/pages/Nodes.tsx` | Modify | Add clickable rows; integrate `NodeDetailsModal` |
| `apps/orchestrator/web/src/components/NodeDetailsModal.tsx` | **Create** | Modal component for node details, workloads, and pre-checks |

---

## Decisions & Open Questions

1. **Pre-check persistence:**
   - *Option A (Recommended for MVP):* Do **not** create a new DB table. Synthesize pre-check results on-the-fly in the controller from existing `NodeEntity` fields (`OsVersion`, `AgentVersion`, `NodeWorkloadStates`) and return them. The `POST` endpoint simply re-runs the synthesis and returns fresh data.
   - *Option B:* Add `NodePreCheckResultEntity` and persist results so history is available.
   - *Decision:* Start with **Option A** to avoid a migration. If users need historical pre-check data later, migrate to Option B.

2. **Disk-space pre-checks:**
   - The orchestrator backend does not currently receive disk-space telemetry from agents.
   - *Option A:* Omit disk-space from the first iteration.
   - *Option B:* Add a placeholder check that always returns "unknown" or mock data.
   - *Decision:* Include a **placeholder** row in the UI (`Disk Space` → `Status: unknown` → `Detail: Agent telemetry not yet implemented`) so the UI structure is complete.

3. **Agent command to run real pre-checks:**
   - True remote pre-check execution would require sending a command to the agent via SignalR and waiting for a response. This is a larger feature.
   - *Decision:* The `POST /api/nodes/{id}/prechecks` endpoint will return **synthetic / cached** results for now. The UI will be ready for real async execution later.

---

## Suggested Order of Implementation

1. **Backend models + controller endpoints** (Phase 1)
2. **Frontend types + API functions** (Phase 1 continued)
3. **Node Details Modal** (Phase 5) — this is the largest new component and validates the new API.
4. **Home page color coding** (Phase 2) — pure UI, low risk.
5. **Workloads page reorder + spinner** (Phase 4 + Phase 3 for Workloads)
6. **Artifacts upload spinner** (Phase 3 for Artifacts)

This order ensures the new API is ready before the modal is built, and leaves the simpler polish tasks for last.
