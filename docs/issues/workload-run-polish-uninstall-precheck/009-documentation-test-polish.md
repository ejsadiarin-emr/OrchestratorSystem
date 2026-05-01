# 009 - Documentation & Final Test Polish

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

- Blocked by #001–#008 — all prior slices must be complete before final polish

## What to build

Update all documentation to reflect the rollback→uninstall change, add deprecation notes to superseded decision docs, write new tests for the pre-check probe and detect endpoint, and ensure all existing tests pass with the new mode values.

**Documentation:**

- `README.md`: Update mode references (`rollback` → `uninstall`) at the identified line locations (300, 353, 365, 392, and any others found)
- `docs/decisions/workload-differential-update-rollback.md`: Add deprecation notice at the top — "Deprecated. Replaced by [workload-run-polish-uninstall-precheck.md](workload-run-polish-uninstall-precheck.md). The `rollback` mode has been removed and replaced with a proper `uninstall` mode."
- `docs/decisions/20260430-workload-init-steps/` (relevant files): Add note that rollback-related decisions are superseded by `workload-run-polish-uninstall-precheck.md`

**New Tests:**

- `tests/orchestrator/unit/`: Add pre-check probe reconciliation tests covering:
  - All 5 reconciliation scenarios (A-E from D5/D16)
  - Agent unreachable → error returned, DB unchanged
  - Agent returns non-200 → error surfaced, DB unchanged
  - Empty package list → no-op
  - Partial probe results → partial update with warning

- `tests/agent/unit/`: Add detect endpoint tests covering:
  - Valid request with multiple packages → correct per-package results
  - Empty packages array → 200 with empty results
  - Package with `file` detection type → correct status and version
  - Package with `version_manifest` detection type → correct status and version
  - Package with `registry` detection type → `NotPresent` (stub)

**Existing Test Updates:**

- `tests/agent/integration/PipelineExecutorTests.cs:995,1007`: Replace `"rollback"` mode with `"uninstall"`
- `tests/agent/unit/InitStepPipelineTests.cs:351`: Replace `"rollback"` mode with `"uninstall"`
- `tests/orchestrator/unit/WorkloadRunsControllerCurrentPackagesTests.cs:453`: Replace `"rollback"` with `"uninstall"`
- Search and replace any remaining `"rollback"` references in test files (not in historical decision docs)

**Verification gate (run all):**

- `dotnet build` succeeds for orchestrator, agent, and contracts
- `dotnet test` all pass (no regressions)
- `pnpm run typecheck` succeeds for web
- `pnpm test` (frontend tests) pass

## Acceptance criteria

- [ ] README mode references updated from `rollback` to `uninstall`
- [ ] `workload-differential-update-rollback.md` has deprecation notice
- [ ] `20260430-workload-init-steps` docs have superseding note
- [ ] 5 reconciliation scenario tests added and passing
- [ ] Agent unreachable test added and passing
- [ ] Detect endpoint tests added and passing (valid request, empty request, each detection type)
- [ ] All existing test assertions updated from `"rollback"` to `"uninstall"`
- [ ] Zero remaining `"rollback"` references in production or test code (decision docs exempt)
- [ ] `dotnet build` all projects pass
- [ ] `dotnet test` all pass
- [ ] `pnpm run typecheck` passes
- [ ] `pnpm test` passes

## Referenced decisions

- [Implementation Scope — Tests section](../../decisions/workload-run-polish-uninstall-precheck.md#implementation-scope)
- [Implementation Scope — Documentation section](../../decisions/workload-run-polish-uninstall-precheck.md#implementation-scope-1)
- [D1: Remove Rollback, Add Uninstall Mode](../../decisions/workload-run-polish-uninstall-precheck.md#d1-remove-rollback-add-uninstall-mode)
- [PRD Verification section](../../prd-workload-run-polish.md#verification)
