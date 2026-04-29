# Decision: PreUpgradeActions Renamed to PreWorkloadSteps

**Date:** 2026-04-30
**Status:** Resolved

## Q23: PreUpgradeActions → PreWorkloadSteps

**Decision: Rename `PreUpgradeActions` to `preWorkloadSteps`** — symmetric with `postWorkloadSteps`, same command semantics, different timing.

### Schema

Workload JSON:
```jsonc
{
  "name": "...",
  "defaultShell": "powershell",
  "packages": [...],
  "preWorkloadSteps": ["COMMAND: check disk space", "COMMAND: create restore point"],
  "postWorkloadSteps": ["COMMAND: final verification", "COMMAND: curl orchestrator"]
}
```

### Runtime contract (AssignRunPayload)
- `PreWorkloadSteps` (`List<string>`) replaces `PreUpgradeActions`
- `PostWorkloadSteps` (`List<string>`) — already decided
- `DefaultShell` (`string`, default `"powershell"`) — already decided

### Storage (WorkloadRevisionEntity)
- `PreWorkloadStepsJson` column (same pattern as `PostWorkloadStepsJson`)
- `PostWorkloadStepsJson` column
- `DefaultShell` column

### Shared semantics
Both `preWorkloadSteps` and `postWorkloadSteps` share the same execution model:
- Each command: separate `Process.Start()`, `FileName=DefaultShell`, `Arguments="-Command "+step`
- Working directory: `Path.GetTempPath()`
- First-failure-stop semantics
- DEPLOY_* env vars injected

### Difference: env var availability
| Env Var | preWorkloadSteps | postWorkloadSteps |
|---|---|---|
| DEPLOY_RUN_ID | Yes | Yes |
| DEPLOY_AGENT_ID | Yes | Yes |
| DEPLOY_WORKLOAD_NAME | Yes | Yes |
| DEPLOY_ORCHESTRATOR_URL | Yes | Yes |
| DEPLOY_PACKAGE_NAME | No | No |
| DEPLOY_PACKAGE_VERSION | No | No |
| DEPLOY_ARTIFACT_PATH | No | Yes (last package's artifact) |

### Pipeline execution order
1. `preWorkloadSteps` (all commands, workload-level)
2. For each package (by PackageIndex):
   - `preInitSteps` (per-package)
   - AcquireArtifact
   - InstallOrUpgrade
   - PostInstallVerify
   - `postInitSteps` (per-package)
3. `postWorkloadSteps` (all commands, workload-level)

## Q24: PreWorkloadSteps Failure Behavior

**Decision: First-failure-stop, abort entire workload run.** If any preWorkload step returns non-zero, stop remaining preWorkload steps and mark the entire run as failed. No package processing begins.

Rationale: `preWorkloadSteps` validate preconditions. If a precondition fails (e.g., "check disk space" returns non-zero), proceeding makes no sense.