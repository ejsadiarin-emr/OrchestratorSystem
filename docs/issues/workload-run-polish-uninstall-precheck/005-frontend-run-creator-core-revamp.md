# 005 - Frontend: Run Creator Core Revamp

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

- Blocked by #001 (Foundation: Rollback→Uninstall Mode Swap) — types and mode values must be updated first

## What to build

Revamp the Run Creator UI core: replace the mode dropdown with Install/Uninstall action buttons, add per-node revision state badges that auto-load when a workload is selected, add a conditional Reinstall checkbox, and update the API service layer. This is the foundation UI slice — uninstall-specific UX (confirmation, node filtering) comes in #006.

**Frontend types (`apps/orchestrator/web/src/types.ts`):**

- `types.ts:277-297`: In `CreateWorkloadRunRequest`, rename `forceInstall` → `reinstall` (the `AssignRunPayload` still uses `ForceInstall` internally — the mapping is done in the API service)
- `types.ts:355-361`: Add `preUninstallSteps` and `postUninstallSteps` to `WorkloadJsonEntry` (optional string arrays)
- `types.ts:370-383`: Add `packageId` field to `PreCheckItem` for package-level probe results

**API service (`apps/orchestrator/web/src/services/api.ts`):**

- Update `createWorkloadRun()` to send the new mode values (`'install'` | `'uninstall'`) and `reinstall` field
- Map `reinstall: true` → `forceInstall: true` in the request body to maintain backward compatibility with backend/agent contracts

**Run Creator page (`apps/orchestrator/web/src/pages/WorkloadRuns.tsx`):**

**Action buttons (D12):**
- Remove the `<select>` dropdown (`const runModes = ['install', 'update', 'rollback']`)
- Add two icon-labeled buttons: **Install** (blue when active) and **Uninstall** (red when active)
- Clicking a button sets `form.mode` accordingly
- Install mode shows: workload selector → revision dropdown → node picker → Reinstall checkbox → submit
- Uninstall mode shows: workload selector → node picker → submit (revision hidden; confirmation added in #006)

**Form state (D10):**
- Rename `forceInstall` → `reinstall` in local form state
- Default mode is `'install'`

**Per-node revision state badges (D6, D14):**
- When a workload is selected, auto-fetch `NodeWorkloadState` for all nodes (fast DB query — no agent probe)
- Display per-node inline badges:
  - `v1.0.0` (green) — node has this revision, DB says all packages present
  - `v1.0.0` (yellow) — node has this revision, but `PackageStatesJson` shows drift (pre-existing from prior probe)
  - `not installed` (gray) — no `NodeWorkloadState` for this workload
  - `checking...` (spinner) — pre-check probe in progress (wired in #007)
- Badges appear in the node list alongside hostname, OS, and online status

**Reinstall checkbox (D10):**
- Conditionally rendered only in Install mode
- Appears when any selected node already has the selected revision
- Label: "Reinstall — force re-install even if already present"
- Checked state maps to `reinstall: true` in form, which maps to `forceInstall: true` in the API request

**Per-node classification badges in summary step (D14):**
- In the confirm/summary step, show per-node badges:
  - Green "Fresh install" — node has no prior revision
  - Blue "Update: v1.0.0 → v2.0.0" — node already has workload, will be updated
- These badges make the implicit mode distinction explicit before submission

**Tests (`apps/orchestrator/web/src/pages/WorkloadRuns.test.tsx`):**
- Update all mode assertions from `'rollback'` to `'uninstall'`
- Update `forceInstall` references to `reinstall`

## Acceptance criteria

- [ ] Mode dropdown replaced with Install/Uninstall action buttons
- [ ] Install button activates full form (workload → revision → nodes → reinstall checkbox)
- [ ] Uninstall button shows simplified form (workload → nodes; no revision)
- [ ] Selecting a workload auto-loads `NodeWorkloadState` and displays per-node revision badges
- [ ] Green badge for nodes with matching installed revision
- [ ] Yellow badge for nodes with drift in `PackageStatesJson`
- [ ] Gray "not installed" badge for nodes without `NodeWorkloadState`
- [ ] Reinstall checkbox appears only when a selected node already has the target revision
- [ ] Reinstall checkbox maps to `forceInstall: true` in API requests
- [ ] Summary step shows per-node classification badges (Fresh install / Update: vX → vY)
- [ ] `pnpm run typecheck` succeeds
- [ ] Existing test assertions updated from `'rollback'` to `'uninstall'`
- [ ] Tests pass

## Referenced decisions

- [D6: UI Node State — Contextual Per-Node Columns](../../decisions/workload-run-polish-uninstall-precheck.md#d6-ui-node-state--contextual-per-node-columns)
- [D7: Install Mode Revisions — Show All, Disable Installed](../../decisions/workload-run-polish-uninstall-precheck.md#d7-install-mode-revisions--show-all-disable-installed)
- [D10: Reinstall Checkbox — Install Mode Only](../../decisions/workload-run-polish-uninstall-precheck.md#d10-reinstall-checkbox--install-mode-only)
- [D12: UI Action Buttons — Replace Mode Dropdown](../../decisions/workload-run-polish-uninstall-precheck.md#d12-ui-action-buttons--replace-mode-dropdown)
- [D14: Update Triggered Implicitly](../../decisions/workload-run-polish-uninstall-precheck.md#d14-update-triggered-implicitly)
