# 001 - Foundation: Rollback→Uninstall Mode Swap

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

None — can start immediately.

## What to build

Replace `rollback` with `uninstall` across all layers of the codebase. This is a mechanical rename — no behavioral changes to the pipeline yet (that comes in #008). Every reference to `rollback` as a run mode is updated to `uninstall`.

**Frontend (`apps/orchestrator/web/src/`):**

- `types.ts:103`: Change `WorkloadRunMode` from `'install' | 'update' | 'rollback'` to `'install' | 'update' | 'uninstall'`
- `types.ts:200-208`: Change `NodeRunState` — remove `'rollback'`, add `'uninstall'`

**Backend (`apps/orchestrator/backend/`):**

- `Controllers/WorkloadRunsController.cs:53`: Update error message from `"Mode must be one of: install, update, rollback"` to `"Mode must be one of: install, update, uninstall"`
- `Controllers/WorkloadRunsController.cs:604-612`: In `TryNormalizeMode()`, replace `"rollback"` with `"uninstall"` in the switch expression
- `Data/InstallerDbContext.cs:225`: Update `CK_WorkloadRuns_Mode` check constraint from `'rollback'` to `'uninstall'`

**Agent (`apps/agent/backend/`):**

- `Pipeline/PipelineExecutor.cs:86`: Replace `isRollback` with `isUninstall` flag (variable rename only — pipeline path changes come in #008)

**DB Migration:**

- Drop and recreate `CK_WorkloadRuns_Mode` check constraint with `'uninstall'` replacing `'rollback'`

## Acceptance criteria

- [ ] `WorkloadRunMode` type has `'uninstall'` and no `'rollback'`
- [ ] `NodeRunState` type has `'uninstall'` and no `'rollback'`
- [ ] `TryNormalizeMode()` accepts `"uninstall"` and rejects `"rollback"`
- [ ] Error message says `"install, update, uninstall"`
- [ ] DB check constraint allows `'uninstall'` and disallows `'rollback'`
- [ ] Agent pipeline flag renamed from `isRollback` to `isUninstall` (no logic change)
- [ ] `dotnet build` succeeds for all three projects (orchestrator, agent, contracts)
- [ ] `pnpm run typecheck` succeeds for web
- [ ] No existing `rollback` references remain in production code (tests may have some until #009)

## Referenced decisions

- [D1: Remove Rollback, Add Uninstall Mode](../../decisions/workload-run-polish-uninstall-precheck.md#d1-remove-rollback-add-uninstall-mode)
- [D8: UI Modes — Two Modes: Install + Uninstall](../../decisions/workload-run-polish-uninstall-precheck.md#d8-ui-modes--two-modes-install--uninstall)
- [D9: Backend Keeps Update Mode (Internal Only)](../../decisions/workload-run-polish-uninstall-precheck.md#d9-backend-keeps-update-mode-internal-only)
