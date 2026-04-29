# Decision: Init Steps Timeline Reporting & PostInstallVerify Scope

**Date:** 2026-04-30
**Status:** Resolved

## Q4: Timeline Reporting for Init Steps

**Decision:** Option C — Each init step command is reported as its own pipeline step, reusing the existing `SendStepStatusAsync` and `WorkloadRunTimelineEntity` mechanism.

### Step naming convention

| Step Type | Format | Example |
|---|---|---|
| Pre-init step | `PreInit_{packageIndex}_{stepIndex}` | `PreInit_0_0`, `PreInit_0_1` |
| Post-init step | `PostInit_{packageIndex}_{stepIndex}` | `PostInit_1_0` |
| Post-workload step | `PostWorkload_{stepIndex}` | `PostWorkload_0`, `PostWorkload_1` |

Existing steps remain unchanged: `AcquireArtifact`, `InstallOrUpgrade`, `PostInstallVerify`, `UninstallPackage`.

### Step ordering per package

For package at index `i`:
1. `PreInit_i_0`, `PreInit_i_1`, ... (all pre-init commands)
2. `AcquireArtifact` (existing)
3. `InstallOrUpgrade` (existing)
4. `PostInstallVerify` (existing)
5. `PostInit_i_0`, `PostInit_i_1`, ... (all post-init commands)

After all packages complete:
6. `PostWorkload_0`, `PostWorkload_1`, ... (all post-workload commands)

## PostInstallVerify vs postInitSteps — Not Redundant

**PostInstallVerify** is still needed alongside `postInitSteps`. They serve different purposes:

- **PostInstallVerify** — Built-in system step that uses `DetectionConfig` to confirm the package binary/version is present on the agent node after installation. This is deterministic and automatic.
- **postInitSteps** — User-defined arbitrary shell commands specified in the workload definition. These could do anything (configure a service, register with a DB, write a config file, etc.). They are optional.

The pipeline sequence is: `PostInstallVerify` (system verification) → `postInitSteps` (user customization). Both are required because:
1. `postInitSteps` are optional — the package needs system verification regardless
2. `postInitSteps` are arbitrary — they don't guarantee the package binary is installed correctly
3. `PostInstallVerify` confirms the artifact landed; `postInitSteps` configures post-install state

### Codebase reality

Current `InstallOrUpgrade.cs` does NOT inject any custom environment variables into the shell process. `Process.StartInfo` only sets `FileName`, `Arguments`, `RedirectStandardOutput/Error`, `CreateNoWindow`, and `WorkingDirectory`. The `DEPLOY_*` env var injection pattern is a new capability to be added alongside init steps.