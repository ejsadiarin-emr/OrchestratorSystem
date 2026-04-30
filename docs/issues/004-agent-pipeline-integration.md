# 004 - Agent: Pipeline Integration

## Type

AFK

## Parent PRD

[docs/prd-init-steps.md](../prd-init-steps.md)

## Blocked by

- Blocked by #002 (Orchestrator Import & Dispatch) — agent needs correct `AssignRunPayload` with init steps
- Blocked by #003 (Agent InitStepExecutor) — pipeline calls `InitStepExecutor.ExecuteAsync()`

## What to build

Integrate init step execution into `PipelineExecutor` (`apps/agent/backend/`) with the following execution order:

```
1. PreWorkload_0, PreWorkload_1, ...        (workload-level, first-failure-stop)
2. For each package (by PackageIndex):
    2a. PreInit_{i}_{0..n}                   (per-package, first-failure-stop → skip install)
    2b. AcquireArtifact                      (existing)
    2c. InstallOrUpgrade                     (existing)
    2d. PostInstallVerify                    (existing, system verification)
    2e. PostInit_{i}_{0..n}                  (per-package, failure → mark failed, continue)
3. PostWorkload_0, PostWorkload_1, ...       (workload-level, first-failure-stop)
```

### Phase 1: Pre-workload (new)

- Execute `AssignRunPayload.PreWorkloadSteps` via `InitStepExecutor`
- Env vars: `DEPLOY_RUN_ID`, `DEPLOY_AGENT_ID`, `DEPLOY_WORKLOAD_NAME`, `DEPLOY_ORCHESTRATOR_URL` only (no package-specific vars)
- Step naming: `PreWorkload_0`, `PreWorkload_1`, ...
- Timeout: 60 seconds per command
- **Failure behavior**: First failure aborts the entire run. No packages are processed. Run marked as Failed.

### Phase 2: Per-package execution (modified)

For each package the diff engine processes (`Added`, `Changed`):

**2a. Pre-init steps** (new, before AcquireArtifact):
- Env vars include: `DEPLOY_PACKAGE_NAME`, `DEPLOY_PACKAGE_VERSION` (no `DEPLOY_ARTIFACT_PATH`)
- Step naming: `PreInit_{packageIndex}_{stepIndex}`
- Timeout: 60 seconds per command
- **Failure behavior**: First failure stops pre-init for this package → skip AcquireArtifact/InstallOrUpgrade/PostInstallVerify for this package → mark package as failed → continue to next package

**2b-2d. Existing pipeline**: AcquireArtifact, InstallOrUpgrade, PostInstallVerify — unchanged.

**2e. Post-init steps** (new, after PostInstallVerify):
- Env vars include: `DEPLOY_PACKAGE_NAME`, `DEPLOY_PACKAGE_VERSION`, `DEPLOY_ARTIFACT_PATH`
- Step naming: `PostInit_{packageIndex}_{stepIndex}`
- Timeout: 120 seconds per command
- **Failure behavior**: Mark package as failed, continue to next package

### Phase 3: Post-workload (new)

- Execute `AssignRunPayload.PostWorkloadSteps` via `InitStepExecutor`
- Env vars: `DEPLOY_RUN_ID`, `DEPLOY_AGENT_ID`, `DEPLOY_WORKLOAD_NAME`, `DEPLOY_ORCHESTRATOR_URL`, `DEPLOY_ARTIFACT_PATH` (last package's artifact path)
- Step naming: `PostWorkload_0`, `PostWorkload_1`, ...
- Timeout: 180 seconds per command
- **Failure behavior**: First failure marks run as Failed

### Diff Engine Gating

- **Unchanged** packages skip ALL init steps (preInitSteps, postInitSteps)
- **ForceInstall** bypasses diff skipping — all init steps run for all packages regardless of diff outcome
- Init steps run on `install` and `update` modes; skipped during `rollback`

### DEPLOY_* Env Var Construction

The pipeline (not `InitStepExecutor`) is responsible for constructing the correct `Dictionary<string, string>` for each scope:

| Variable | preWorkload | preInit | postInit | postWorkload |
|----------|:-----------:|:-------:|:--------:|:------------:|
| `DEPLOY_RUN_ID` | ✓ | ✓ | ✓ | ✓ |
| `DEPLOY_AGENT_ID` | ✓ | ✓ | ✓ | ✓ |
| `DEPLOY_WORKLOAD_NAME` | ✓ | ✓ | ✓ | ✓ |
| `DEPLOY_ORCHESTRATOR_URL` | ✓ | ✓ | ✓ | ✓ |
| `DEPLOY_PACKAGE_NAME` | — | ✓ | ✓ | — |
| `DEPLOY_PACKAGE_VERSION` | — | ✓ | ✓ | — |
| `DEPLOY_ARTIFACT_PATH` | — | — | ✓ | ✓* |

*postWorkloadSteps gets the LAST package's artifact path.

## Acceptance criteria

- [ ] Pre-workload steps execute before any package processing
- [ ] Pre-workload failure (non-zero exit) aborts the entire run; no packages processed
- [ ] Per-package preInitSteps execute before AcquireArtifact for the package
- [ ] preInitSteps failure stops pre-init for that package, skips install, marks package failed, continues to next package
- [ ] Per-package postInitSteps execute after PostInstallVerify
- [ ] postInitSteps failure marks package failed, continues to next package
- [ ] Post-workload steps execute after all packages complete
- [ ] Post-workload failure marks run as Failed
- [ ] Unchanged packages (from diff engine) skip all init steps
- [ ] ForceInstall runs all init steps regardless of diff outcome
- [ ] Init steps run on `install` and `update` modes
- [ ] Init steps skipped during `rollback` mode
- [ ] `DEPLOY_ARTIFACT_PATH` NOT present in preInitSteps env vars
- [ ] `DEPLOY_ARTIFACT_PATH` IS present in postInitSteps env vars (after AcquireArtifact)
- [ ] Integration tests for each failure semantic (pre-workload abort, pre-init skip, post-init mark failed, post-workload mark failed)
- [ ] Integration tests for diff engine gating (unchanged skips, ForceInstall bypasses)
- [ ] Integration tests for run mode gating (install/update run, rollback skips)

## Referenced decisions

- [Execution Model & Semantics](../decisions/20260430-workload-init-steps/workload-init-steps-20260430-000543.md)
- [Timeline Reporting](../decisions/20260430-workload-init-steps/workload-init-steps-timeline-20260430-001000.md)
- [Environment Variables](../decisions/20260430-workload-init-steps/workload-init-steps-env-vars-20260430-001200.md)
- [Failure Semantics](../decisions/20260430-workload-init-steps/workload-init-steps-failure-semantics-20260430-001500.md)
- [Timeout Configuration](../decisions/20260430-workload-init-steps/workload-init-steps-timeout-20260430-001700.md)
- [Diff Engine Interaction](../decisions/20260430-workload-init-steps/workload-init-steps-diff-engine-20260430-003000.md)
- [Run Mode Behavior](../decisions/20260430-workload-init-steps/workload-init-steps-run-modes-20260430-002400.md)
- [ForceInstall Interaction](../decisions/20260430-workload-init-steps/workload-init-steps-force-install-20260430-002500.md)
- [PreWorkloadSteps Rename](../decisions/20260430-workload-init-steps/workload-init-steps-pre-workload-20260430-003200.md)
