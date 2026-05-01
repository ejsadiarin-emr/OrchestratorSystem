# 008 - Agent: Uninstall Pipeline Path

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

- Blocked by #001 (Foundation: Rollback→Uninstall Mode Swap) — `isUninstall` flag must exist
- Blocked by #002 (Uninstall Steps: Contracts, Schema & Backend Import) — `AssignRunPayload` must carry `PreUninstallSteps`/`PostUninstallSteps`

## What to build

Implement the full uninstall pipeline execution path in the agent. When `mode: "uninstall"`, the agent detects what's installed, runs pre-uninstall shell commands, uninstalls all detected packages, then runs post-uninstall shell commands. All install/init phases are skipped.

**Agent (`apps/agent/backend/`):**

- `Pipeline/PipelineExecutor.cs:86`: The `isUninstall` flag (already renamed in #001) now gates pipeline behavior
- `Pipeline/PipelineExecutor.cs`: Implement the uninstall-specific execution order:

```
Phase 0:    PreCheckProbe      — detect what's actually on the node (NOT skipped)
Phase 0.5:  PreUninstallSteps  — revision-level shell commands BEFORE package removal (EXECUTED)
            PreWorkloadSteps   — SKIPPED (gated on isUninstall)
Phase 1:    UninstallPackage   — uninstall ALL detected packages, in reverse PackageIndex order
                                (uses each package's UninstallArgs from its InstallAdapter)
Phase 2:    PreInitSteps       — SKIPPED
            AcquireArtifact   — SKIPPED
            InstallOrUpgrade  — SKIPPED
            PostInstallVerify — SKIPPED
            PostInitSteps     — SKIPPED
Phase 3:    PostUninstallSteps — revision-level shell commands AFTER package removal (EXECUTED)
            PostWorkloadSteps  — SKIPPED (gated on isUninstall)
```

**Gating rules for `isUninstall`:**
- **SKIP**: `PreWorkloadSteps`, `PreInitSteps`, `PostInitSteps`, `PostWorkloadSteps`, `AcquireArtifact`, `InstallOrUpgrade`, `PostInstallVerify`
- **EXECUTE**: `PreCheckProbe`, `PreUninstallSteps`, `UninstallPackage`, `PostUninstallSteps`

**Uninstall package source:**
- The orchestrator sends the full package list from the node's `CurrentRevisionId` in `AssignRunPayload`
- The agent runs `PreCheckProbe` to confirm what's actually present
- Only detected packages are uninstalled (packages already missing are skipped)
- Packages belonging to other workloads are untouched

**Step reporting:**
- Each uninstall step reports status to the orchestrator (consistent with existing install/update step reporting)
- If any package uninstall fails, the run fails (best-effort — no rollback of already-uninstalled packages)
- On completion, orchestrator clears `NodeWorkloadState` (`CurrentRevisionId` → null, `PackageStatesJson` → `{}`)

**PreUninstallSteps / PostUninstallSteps execution:**
- Executed using the revision's `DefaultShell` (same mechanism as `PreWorkloadSteps`/`PostWorkloadSteps`)
- If the steps list is empty, the phase is a no-op (not an error)
- Step failures are reported to orchestrator and fail the run

## Acceptance criteria

- [ ] Uninstall pipeline skips all init/install phases (PreWorkloadSteps, PreInitSteps, AcquireArtifact, InstallOrUpgrade, PostInstallVerify, PostInitSteps, PostWorkloadSteps)
- [ ] PreCheckProbe runs before uninstall to detect what's actually present
- [ ] PreUninstallSteps execute before package removal in Phase 0.5
- [ ] UninstallPackage runs for all detected packages in reverse PackageIndex order
- [ ] PostUninstallSteps execute after package removal in Phase 3
- [ ] Only detected packages are uninstalled (missing packages skipped without error)
- [ ] Uninstall step failures fail the run (best-effort, no rollback)
- [ ] Step status reported to orchestrator consistently with install/update mode
- [ ] Empty PreUninstallSteps/PostUninstallSteps arrays are no-ops (not errors)
- [ ] `dotnet build` succeeds for agent project
- [ ] Existing install/update pipeline behavior is unchanged (regression check)

## Referenced decisions

- [D1: Remove Rollback, Add Uninstall Mode](../../decisions/workload-run-polish-uninstall-precheck.md#d1-remove-rollback-add-uninstall-mode)
- [D11: PreUninstallSteps / PostUninstallSteps — Add Now](../../decisions/workload-run-polish-uninstall-precheck.md#d11-preuninstallsteps--postuninstallsteps--add-now)
- [D20: D1 Pipeline Clarification](../../decisions/workload-run-polish-uninstall-precheck.md#d20-d1-pipeline-clarification-doc-fix)
