# Decision: Init Steps Working Directory

**Date:** 2026-04-30
**Status:** Resolved

## Q7: Working Directory for Init Step Commands

**Decision:** `Path.GetTempPath()` — consistent with where the agent already operates (artifact downloads go there), always exists on Windows, no configuration needed.

### Rationale

- `InstallOrUpgrade` already uses an artifact download directory under temp for its working directory
- `Path.GetTempPath()` always exists on Windows (service processes)
- No extra configuration field needed in the workload definition
- Consistent with the agent's existing operating environment

### Application

| Step Type | Working Directory |
|---|---|
| preInitSteps | `Path.GetTempPath()` |
| postInitSteps | `Path.GetTempPath()` |
| postWorkloadSteps | `Path.GetTempPath()` |

Note: `DEPLOY_ARTIFACT_PATH` (from Q5/Q6) is only available in postInitSteps — preInitSteps don't have an artifact path, but they still get a valid working directory.