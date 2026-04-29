# Decision: Init Steps Environment Variables

**Date:** 2026-04-30
**Status:** Resolved

## Q5: Environment Variables for Init Steps

**Decision:** Inject `DEPLOY_*` environment variables at runtime into the shell process for init step commands. These are **ephemeral only** — set on `Process.StartInfo.Environment` before process start, not persisted anywhere. Visible only to that command execution.

### Environment variable set

| Variable | Source | Example |
|---|---|---|
| `DEPLOY_RUN_ID` | `PipelineContext.Payload.RunId` | `"abc-123"` |
| `DEPLOY_AGENT_ID` | `PipelineContext.AgentId` | `"node-05"` |
| `DEPLOY_WORKLOAD_NAME` | `Payload.WorkloadName` | `"CustomApp1 Workload"` |
| `DEPLOY_PACKAGE_NAME` | `PackageAssignment.Name` | `"7zip-26.00"` |
| `DEPLOY_PACKAGE_VERSION` | `PackageAssignment.Version` | `"26.00"` |
| `DEPLOY_ORCHESTRATOR_URL` | `PipelineContext.OrchestratorBaseUrl` | `"http://host:5000"` |
| `DEPLOY_ARTIFACT_PATH` | Downloaded artifact local path | `"C:\temp\7z2600-x64.exe"` |

### Codebase reality (verified from bugfix worktree)

Current `InstallOrUpgrade.cs` in `apps/agent/backend/Steps/`:
- **WorkingDirectory** is set to `Path.GetDirectoryName(Path.GetFullPath(artifactPath))` with fallback to `Directory.GetCurrentDirectory()`
- **No custom environment variables are currently injected** — `Process.StartInfo` only sets `FileName`, `Arguments`, `RedirectStandardOutput`, `RedirectStandardError`, `CreateNoWindow`, `WorkingDirectory`
- For UAC-elevated execution (`UseShellExecute = true`, `Verb = "runas"`), output redirection is disabled

This means the `DEPLOY_*` env var injection is a **new capability** — it doesn't exist in the current codebase and needs to be added as part of the init steps implementation.

## Q6: DEPLOY_ARTIFACT_PATH in preInitSteps

**Decision:** Option A — **Omit `DEPLOY_ARTIFACT_PATH` entirely from preInitSteps**. It is only available in `postInitSteps` because the artifact hasn't been downloaded yet when preInitSteps execute.

Rationale: Cleaner contract. No empty/null values that could confuse command authors. Commands that need the artifact path should be in postInitSteps.