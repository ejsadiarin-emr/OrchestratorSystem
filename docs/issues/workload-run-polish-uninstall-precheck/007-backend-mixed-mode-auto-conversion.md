# 007 - Backend: Mixed-Mode Auto-Conversion at Dispatch

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

- Blocked by #001 (Foundation: Rollback→Uninstall Mode Swap) — mode values must be updated

## What to build

When the frontend sends `mode: "install"` for a run, the backend inspects each target node's `NodeWorkloadState` at creation time and auto-converts the mode per `WorkloadRunEntity` row. This allows a single run to contain mixed nodes (some fresh installs, some updates) while preserving diff optimization.

**Backend (`apps/orchestrator/backend/`):**

- `Controllers/WorkloadRunsController.cs`: In the run creation endpoint, after validating the request, iterate through each target node and determine the actual mode per row:

| Node State | Dispatched Mode | Behavior |
|---|---|---|
| `CurrentRevisionId == null` | `"install"` | Fresh install — all packages sent |
| `CurrentRevisionId == targetRevisionId` | `"install"` (with `ForceInstall` if `reinstall: true`) | Force reinstall — PreCheckProbe skipped |
| `CurrentRevisionId != targetRevisionId` AND `CurrentRevisionId != null` | `"update"` | Diff optimization — only changed packages sent |

- Uninstall mode does NOT auto-convert — all selected nodes get `mode: "uninstall"` regardless of current state
- The `RunId` (parent run grouping) remains the same for all rows created from one request — only the per-node `Mode` column differs

**Key invariants:**
- The frontend always sends `mode: "install"` or `mode: "uninstall"` (never `"update"`)
- Auto-conversion happens server-side, transparent to the UI
- Diff optimization is preserved: when mode becomes `"update"`, `WorkloadRunsController.GetSteps()` computes the diff between current and target revisions, sending only `Added`/`Removed`/`Changed` packages
- When mode is `"install"`, all packages are sent and `PreCheckProbe` handles skipping already-satisfied ones on the agent
- When mode is `"uninstall"`, the current revision's package list is used as the source for what to remove

**Adjacent change:**
- `WorkloadRunsController.cs`: Accept the `reinstall` field from the request body and map it to `ForceInstall` on the `WorkloadRunEntity` (or per-row assignment)

## Acceptance criteria

- [ ] Run creation endpoint iterates target nodes and sets per-row `Mode` based on `NodeWorkloadState`
- [ ] Node with no prior revision → `Mode = "install"`
- [ ] Node with same revision → `Mode = "install"` (with `ForceInstall` if `reinstall: true`)
- [ ] Node with different revision → `Mode = "update"` (diff optimization engaged)
- [ ] All rows share the same `RunId` (grouping)
- [ ] Uninstall mode passes through unchanged (no auto-conversion)
- [ ] `reinstall` field from request is accepted and mapped correctly
- [ ] Backward compatible: existing run creation continues to work
- [ ] `dotnet build` succeeds for orchestrator project
- [ ] Unit tests verify per-node mode assignment logic for all three node states

## Referenced decisions

- [D9: Backend Keeps Update Mode (Internal Only)](../../decisions/workload-run-polish-uninstall-precheck.md#d9-backend-keeps-update-mode-internal-only)
- [D17: Mixed-Mode Runs — Per-Node Auto-Conversion at Dispatch](../../decisions/workload-run-polish-uninstall-precheck.md#d17-mixed-mode-runs--per-node-auto-conversion-at-dispatch)
