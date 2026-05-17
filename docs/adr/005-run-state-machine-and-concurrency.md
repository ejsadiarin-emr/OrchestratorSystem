# ADR-005: Run State Machine & Concurrency Control

**Status:** Accepted  
**Date:** 2026-05-17  
**Context:** DeploymentPoC workload run lifecycle and concurrent-run prevention

## Problem

When deploying software to nodes, the orchestrator must:
1. Prevent two deployment runs from executing simultaneously on the same node for the same workload
2. Guarantee run state transitions follow valid paths
3. Handle idempotent creation so administrators can safely retry on network failures
4. Auto-resolve the effective mode (install vs. update) based on the node's current state

## Decision

### 1. State Machine

A workload run follows this state machine:

```
Queued → Running → Completed
                 └→ Failed
         └→ Cancelled (admin action)
```

Enforced via a CHECK constraint:
```sql
CK_WorkloadRuns_State: "State" IN ('Queued','Running','Completed','Failed','Cancelled')
```

Valid transitions:
- **Queued → Running** — agent claims the run via `PATCH /api/workload-runs/{runId}` with `{status: "Running"}`
- **Running → Completed** — agent reports success via PATCH
- **Running → Failed** — agent reports failure via PATCH (also sets `NodeWorkloadState.Status = "Drifted"`)
- **Any non-terminal → Cancelled** — admin cancels via `POST /api/workload-runs/{runId}/cancel`

Terminal states (`Completed`, `Failed`, `Cancelled`) cannot transition out.

The run mode is also constrained:
```sql
CK_WorkloadRuns_Mode: "Mode" IN ('install','update','uninstall','cancel')
```

### 2. Concurrent Run Prevention (Filtered Unique Index)

A filtered unique index prevents two active runs for the same node + workload:

```sql
IX_WorkloadRuns_NodeId_WorkloadId_Active UNIQUE (NodeId, WorkloadId)
    WHERE "State" IN ('Queued','Running')
```

This allows historic completed/failed/cancelled runs to coexist while blocking new runs if an active run exists. The orchestrator catches `DbUpdateException` and inspects `IsActiveRunConstraintViolation()` to return a `409 Conflict` with `ACTIVE_RUN_CONFLICT` code.

### 3. Idempotent Creation

Run creation uses a client-provided `IdempotencyKey`:
- Unique index on `IdempotencyKey` (128 char max)
- `IdempotencyRequestHash` (SHA-256-derived, 64 char max) captures the request payload hash
- On duplicate key: if the hash matches, return the existing run (`200 OK`); if the hash differs, return `409 Conflict`
- This allows safe retries on network failures without creating duplicate runs

### 4. Mode Auto-Resolution

When the admin requests `mode: "install"`, the orchestrator auto-resolves the effective mode per node based on `NodeWorkloadState`:

| Condition | Effective Mode |
|---|---|
| No state exists, no prior completed run | `install` |
| `CurrentRevisionId == request.RevisionId` | `install` with `ForceInstall` only if `Reinstall=true` |
| `CurrentRevisionId != null && != request.RevisionId` | `update` |

For `mode: "uninstall"`, no auto-resolution is needed — it always runs as `uninstall`.

### 5. Revision Snapshot on Run Creation

Each run captures `RevisionSnapshotJson` — an ordered list of `{PackageId, PackageIndex}` — at creation time. Since revisions are immutable (ADR-004), this snapshot is technically redundant with the live `RevisionId` foreign key but serves as an audit trail that survives any future schema changes.

### 6. NodeWorkloadState Lifecycle

`NodeWorkloadStateEntity` tracks per-node, per-workload status:

| Status | Meaning |
|---|---|
| `Current` | Node has this workload installed at the revision indicated by `CurrentRevisionId` |
| `Drifted` | Workload state is uncertain (e.g., run failed, or uninstall cleared revision) |
| `Unknown` | Initial state before first run is processed |

State transitions:
- **Created** on `AckClaim` (first time)
- **Updated** on `Complete`: `Status = "Current"`, `CurrentRevisionId = run.RevisionId`
- **Updated** on `Fail`: `Status = "Drifted"`
- **Cleared** on uninstall completion: `CurrentRevisionId = null`, `Status = "Drifted"`, `PackageStatesJson = "{}"`

CHECK constraint: `CK_NodeWorkloadState_Status: "Status" IN ('Current','Drifted','Unknown')`

## Consequences

### Positive
- **Database-level concurrency guarantee** — the filtered unique index is the single source of truth; no application-level locking needed
- **Idempotent creation** — network retries cannot create duplicate runs
- **Mode auto-resolution** — admins don't need to track which nodes have which revision; the orchestrator decides install vs. update
- **Audit trail** — every run is retained forever; `RevisionSnapshotJson` provides a cross-reference even if revision data is restructured

### Negative
- **No run deletion** — cancelled/failed runs persist; no cleanup path exists
- **Mode auto-resolution is one-shot** — decided at creation time; if the node state changes between creation and execution, the mode may be stale
- **IdempotencyKey is client-provided** — requires the frontend to generate and manage these keys; no server-side generation
- **Drifted state is a dead end** — `NodeWorkloadState` enters `Drifted` on failure or uninstall but has no automatic recovery path

## Trade-offs Accepted

- The filtered unique index means only one active run per (node, workload) at a time. This is intentional: concurrent installs of the same workload on the same node makes no sense.
- Mode auto-resolution at creation time (not execution time) means the resolution reflects node state at request time, not at execution time. This is acceptable because the gap between creation and execution is typically seconds (next poll cycle).
- `RevisionSnapshotJson` is an 8192-char text field rather than a normalized table. This is a deliberate denormalization for simplicity — the snapshot never needs to be queried independently.

## Related

- `InstallerDbContext.cs`: filtered unique index definition (line 223–226), CHECK constraints
- `WorkloadRunsController.cs`: run creation with idempotency and mode auto-resolution (line 160–314)
- `NodeWorkloadStateService.cs`: state transitions on AckClaim, Complete, Fail
- ADR-004: Workload revision immutability (snapshots are safe because revisions cannot change)
- ADR-006: Agent single-pipeline execution (agent-side complement to server-side concurrency control)