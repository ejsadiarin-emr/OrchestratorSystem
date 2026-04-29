# PRD: Workload Init Steps

**Date:** 2026-04-30
**Status:** Draft
**Owner:** Architecture
**References:** `docs/decisions/20260430-workload-init-steps/` (20 ADR files)

---

## Problem Statement

Workload authors need to run custom shell commands before and after package installation to validate prerequisites (e.g., check disk space, verify service accounts), configure services post-install (e.g., register with a database, apply config templates), and verify the final workload state (e.g., curl health endpoints). Currently the pipeline only performs acquire/install/verify — there is no hook for user-defined customization, forcing operators to manually run these commands outside the deployment system.

---

## Solution

Add **init steps** — user-authored shell commands that the agent executes at three scopes within the existing per-package pipeline:

| Scope | Timing | Failure Behavior |
|-------|--------|-----------------|
| `preWorkloadSteps` | Before any package processing | First-failure-stop; abort entire run |
| `preInitSteps` (per package) | Before `AcquireArtifact` for that package | First-failure-stop; skip that package, continue workload |
| `postInitSteps` (per package) | After `PostInstallVerify` for that package | Mark package failed, continue to next package |
| `postWorkloadSteps` | After all packages complete | First-failure-stop; mark run as failed |

Commands are opaque shell strings. Each is invoked as a separate `Process.Start()` using a workload-level `DefaultShell` (defaults to PowerShell). The system provides `DEPLOY_*` environment variables to each command for context (RunId, AgentId, PackageName, etc.). This is a PoC feature with display-only frontend support and JSON import as the primary authoring path.

---

## User Stories

### Core Execution

1. As a workload author, I want to define `preWorkloadSteps` in my workload JSON so that prerequisite checks (disk space, service availability) run before any package installation begins.
2. As a workload author, I want to define `preInitSteps` on a specific package so that per-package prerequisites (version check, license validation) run before that package's artifact is downloaded.
3. As a workload author, I want to define `postInitSteps` on a specific package so that I can configure the installed software (register with a database, write config files, restart a service).
4. As a workload author, I want to define `postWorkloadSteps` so that I can verify the entire deployed workload is healthy (curl health endpoints, run integration smoke tests).
5. As a workload author, I want init step commands to receive `DEPLOY_*` environment variables so that I can reference the run context in my scripts without hardcoding values.
6. As a workload author, I want `DEPLOY_ARTIFACT_PATH` available in `postInitSteps` so that I can reference the downloaded artifact in my post-install configuration commands.
7. As a workload author, I want to specify which shell executes my commands via a `defaultShell` field so that I can write commands in PowerShell (the default) or CMD.
8. As an operator, I want init steps to appear on the workload run timeline with their own step identifiers so that I can trace which commands ran, in what order, and with what status.

### Failure Handling

9. As an operator, I want a failing `preInitSteps` command to skip the package install but continue the workload so that one broken prerequisite doesn't cascade into a full deployment failure.
10. As an operator, I want a failing `postInitSteps` command to mark the package as failed but continue to the next package so that the remaining packages still get installed.
11. As an operator, I want a failing `preWorkloadSteps` command to abort the entire workload run immediately so that no time is wasted on a deployment with broken preconditions.
12. As an operator, I want a failing `postWorkloadSteps` command to mark the workload run as failed so that I know my final verification didn't pass.
13. As an operator, I want init step output (stdout/stderr) captured in the timeline message on failure so that I can diagnose why a shell command failed.

### Timeouts

14. As an operator, I want pre-init commands to have a 60-second timeout so that fast prerequisite checks don't hang indefinitely.
15. As an operator, I want post-init commands to have a 120-second timeout so that service configuration has enough time to complete.
16. As an operator, I want post-workload commands to have a 180-second timeout so that end-to-end verification has generous time.

### Diff Engine and Run Modes

17. As an operator, I want init steps to only execute for packages the diff engine actually processes (Added, Changed) so that unchanged packages don't incur unnecessary side effects.
18. As an operator, I want `ForceInstall` to always run init steps regardless of diff outcome so that I can re-configure already-installed packages.
19. As an operator, I want init steps to run on both `install` and `update` modes so that pre/post configuration is applied consistently.
20. As an operator, I want init steps skipped during `rollback` mode to keep the rollback path simple.

### Import and Authoring

21. As a workload author, I want to import a workload JSON file with `preInitSteps`, `postInitSteps`, `preWorkloadSteps`, `postWorkloadSteps`, and `defaultShell` fields so that I can define init steps without a UI.
22. As a workload author, I want to mix string shorthand and object form for packages in the import JSON so that I only need to write object syntax for packages that have init steps.
23. As a workload author, I want empty strings in init step arrays rejected at import time so that I don't accidentally submit broken commands.
24. As a workload author, I want command strings exceeding 4096 characters rejected at import time so that I'm forced to put complex logic in scripts rather than inline.
25. As a workload author, I expect init steps to be safe to re-run (idempotent) because the system provides no retry-detection logic.

### Frontend Display

26. As an operator viewing a workload revision, I want to see each package's pre and post init steps in a collapsible section so that I can audit what custom commands are configured without UI clutter.
27. As an operator viewing a workload revision, I want packages without init steps to show no expansion indicator so that I can quickly scan for packages with custom configuration.
28. As an operator viewing a workload revision, I want post-workload steps displayed in a separate section at the bottom of the revision view so that I can distinguish per-package steps from workload-level steps.
29. As an operator, I want to see a note near unchanged packages on the revision detail page explaining that init steps won't run unless Force Install is used.
30. As an operator viewing a workload run timeline, I want init step entries displayed alongside existing pipeline steps (AcquireArtifact, InstallOrUpgrade, PostInstallVerify) so that I have a complete view of the run.
31. As an operator, I want the `defaultShell` value displayed on the workload revision detail page so that I know which shell is configured.

### API Contract

32. As an API consumer, I want to send `preInitSteps` and `postInitSteps` as typed string arrays in the workload revision creation request so that the API contract is unambiguous.
33. As an API consumer, I want to send `postWorkloadSteps` and `defaultShell` as typed fields in the workload revision creation request.
34. As an API consumer, I want the runtime payload (AssignRunPayload) to include `PreWorkloadSteps`, `PostWorkloadSteps`, and `DefaultShell` so that the agent receives all init step data.
35. As an API consumer, I want `PackageAssignment` to include `PreInitSteps` and `PostInitSteps` so that per-package commands are available to the agent.

### Backward Compatibility

36. As a system operator, I want existing workloads with no init steps (empty arrays) to continue running unchanged so that the feature is additive.
37. As a system operator, I want existing `PreUpgradeActions` in AssignRunPayload to become `PreWorkloadSteps` so that the rename is a clean, intentional contract change.
38. As a system operator, I want string shorthand packages in import JSON (`"nodejs-24.13.0"`) to continue importing without modification.

---

## Implementation Decisions

### Modules to Build or Modify

#### 1. Shared Contracts (`shared/contracts/`)

**Rename and extend `AssignRunPayload`:**
- Rename `PreUpgradeActions` to `PreWorkloadSteps`
- Add `PostWorkloadSteps` (`List<string>`)
- Add `DefaultShell` (`string`, default `"powershell"`)

**Extend `PackageAssignment`:**
- Add `PreInitSteps` (`List<string>`)
- Add `PostInitSteps` (`List<string>`)

#### 2. Orchestrator Database (`apps/orchestrator/backend/Data/Entities`)

**Extend `WorkloadPackageEntity`:**
- Add `PreInitStepsJson` (string, nullable, default `"[]"`)
- Add `PostInitStepsJson` (string, nullable, default `"[]"`)

**Extend `WorkloadRevisionEntity`:**
- Add `PreWorkloadStepsJson` (string, nullable, default `"[]"`)
- Add `PostWorkloadStepsJson` (string, nullable, default `"[]"`)
- Add `DefaultShell` (string, default `"powershell"`)

**Database migration:** Add-only ALTER TABLE statements. No columns renamed or dropped. Existing rows get default values. No data migration needed.

#### 3. Orchestrator API (`apps/orchestrator/backend/`)

**Request models:**
- Add `PreInitSteps`, `PostInitSteps` to `WorkloadPackageInput`
- Add `PostWorkloadSteps`, `DefaultShell` to `CreateWorkloadRevisionRequest`

**`WorkloadImportService` changes:**
- Custom JSON converter that handles both `JsonTokenType.String` and `JsonTokenType.StartObject` for package entries in the `packages` array
- String packages deserialized as `{ name: value, preInitSteps: [], postInitSteps: [] }`
- Validation: reject empty strings, reject any command over 4096 characters
- Parse `preWorkloadSteps`, `postWorkloadSteps`, `defaultShell` from workload-level JSON

**`WorkloadRunDispatcher` changes:**
- Deserialize init step JSON columns when building `AssignRunPayload`
- Populate `PackageAssignment.PreInitSteps`/`.PostInitSteps` from `WorkloadPackageEntity`
- Populate `AssignRunPayload.PreWorkloadSteps`/`.PostWorkloadSteps`/`.DefaultShell` from `WorkloadRevisionEntity`

#### 4. Agent InitStepExecutor (`apps/agent/backend/Steps/`) — NEW deep module

A dedicated module that encapsulates all shell command execution logic behind a simple interface:

**Interface:**
```
Task<InitStepResult> ExecuteAsync(
    string command, string defaultShell, string stepName,
    Dictionary<string, string> envVars, int timeoutSeconds,
    int packageIndex, Func<StepStatusPayload, Task> sendStatusAsync,
    CancellationToken ct)
```

**Internal responsibilities:**
- Launch `Process.Start()` with `FileName=defaultShell`, `Arguments="-Command <step>"`
- Set `WorkingDirectory = Path.GetTempPath()`
- Inject `DEPLOY_*` environment variables ephemerally via `Process.StartInfo.Environment`
- Apply per-step-type timeout; call `Process.Kill()` on timeout, treat as failure
- Capture stdout/stderr; include in error message on failure
- Send timeline status via `sendStatusAsync` with step names like `PreInit_0_0`, `PostInit_1_0`, `PostWorkload_0`
- Return exit code as success/failure

**Encapsulation principle:** `InitStepExecutor` does not know about pipeline ordering, diff outcomes, or workload semantics. It only knows "execute one command and report its result." This makes it a deep module — testable in isolation with a mocked process or test shell.

#### 5. Agent PipelineExecutor (`apps/agent/backend/`)

**Integrate init steps into the execution sequence:**

1. **Pre-workload phase (new):** Execute `preWorkloadSteps` via `InitStepExecutor`. If any fail, abort and mark run as Failed (no packages processed).
2. **Per-package phase (modified):** For each Added/Changed package in order:
   a. `preInitSteps` — execute each via `InitStepExecutor`. If any fail, stop that package's pre-init, skip install, mark package as failed, continue to next package.
   b. Existing pipeline (AcquireArtifact, InstallOrUpgrade, PostInstallVerify) — unchanged.
   c. `postInitSteps` — execute each via `InitStepExecutor`. If any fail, mark package as failed, continue to next package.
3. **Post-workload phase (new):** After all packages, execute `postWorkloadSteps`. If any fail, mark run as Failed.
4. Unchanged packages (from diff engine) skip all init steps.
5. ForceInstall bypasses diff skipping — all init steps run.

#### 6. DEPLOY_* Environment Variable Injection — cross-cutting

Inject ephemeral environment variables into `Process.StartInfo.Environment` before each init step command execution. Variables are not persisted.

| Variable | Availability | Source |
|----------|-------------|--------|
| `DEPLOY_RUN_ID` | All init steps | `PipelineContext.Payload.RunId` |
| `DEPLOY_AGENT_ID` | All init steps | `PipelineContext.AgentId` |
| `DEPLOY_WORKLOAD_NAME` | All init steps | `Payload.WorkloadName` |
| `DEPLOY_ORCHESTRATOR_URL` | All init steps | `PipelineContext.OrchestratorBaseUrl` |
| `DEPLOY_PACKAGE_NAME` | preInitSteps, postInitSteps | `PackageAssignment.Name` |
| `DEPLOY_PACKAGE_VERSION` | preInitSteps, postInitSteps | `PackageAssignment.Version` |
| `DEPLOY_ARTIFACT_PATH` | postInitSteps only | Downloaded artifact local path |

`DEPLOY_ARTIFACT_PATH` is deliberately omitted from `preInitSteps` since the artifact hasn't been downloaded yet. It is available in `postInitSteps` (after AcquireArtifact) and `postWorkloadSteps` (last package's artifact path).

#### 7. Frontend (`apps/orchestrator/web/`)

**Workload revision detail page:**
- Append init steps field to package entries: collapsible section (collapsed by default), showing pre-init and post-init commands as formatted code blocks
- Packages without init steps show no expansion indicator
- Post-workload steps shown in a separate section at the bottom of the revision view
- Display `defaultShell` value
- Add note near unchanged packages about Force Install

**Workload run timeline:**
- Timeline entries for init steps use existing step display patterns
- Step names follow naming convention: `PreInit_0_0`, `PostInit_1_0`, `PostWorkload_0`, etc.

No create/edit UI for init steps in this phase. JSON import is the authoring path.

### Step Naming Convention (Timeline)

| Step Type | Format | Example |
|-----------|--------|---------|
| Pre-init step | `PreInit_{packageIndex}_{stepIndex}` | `PreInit_0_0`, `PreInit_0_1` |
| Post-init step | `PostInit_{packageIndex}_{stepIndex}` | `PostInit_1_0` |
| Pre-workload step | `PreWorkload_{stepIndex}` | `PreWorkload_0` |
| Post-workload step | `PostWorkload_{stepIndex}` | `PostWorkload_0`, `PostWorkload_1` |

Existing step names (`AcquireArtifact`, `InstallOrUpgrade`, `PostInstallVerify`, `UninstallPackage`) remain unchanged.

### Execution Order Summary

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

### Three-Layer Data Flow

| Layer | Format | Description |
|-------|--------|-------------|
| Import JSON file | Hybrid (string or object) | Ergonomic for humans authoring workload definitions |
| REST API (Frontend → Orchestrator) | Always object | Strict, typed, validated contract |
| Runtime payload (Orchestrator → Agent) | Always object | Determined by AssignRunPayload / PackageAssignment contracts |

### PostInstallVerify vs postInitSteps — Not Redundant

These serve distinct purposes and both are required:

- **PostInstallVerify:** Built-in system step using `DetectionConfig` to confirm the package binary/version is present. Deterministic and automatic. Runs for every package regardless of whether `postInitSteps` are defined.
- **postInitSteps:** User-defined arbitrary shell commands. Optional. Could do anything (configure a service, register with a DB, write a config file). Does not replace system verification.

The pipeline sequence `PostInstallVerify → postInitSteps` ensures system verification completes before user customization begins.

---

## Testing Decisions

### What Makes a Good Test

Tests should verify external behavior, not internal implementation details:
- **Agent-side:** Verify that given a list of commands, the correct number of processes are launched with the right shell, working directory, environment variables, and arguments. Verify timeout behavior, exit code handling, and output capture on failure.
- **Orchestrator-side:** Verify that init step data round-trips correctly from import JSON → storage → API response → dispatch payload.
- **Pipeline integration:** Verify that preInit failure skips the package install, postInit failure marks the package failed but continues, preWorkload failure aborts the entire run, and postWorkload failure marks the run as failed.

### Modules to Test

- **InitStepExecutor** — Unit tested in isolation. Mock the shell process to verify correct invocation, env var injection, timeout enforcement, exit code interpretation, and step status reporting. This is the highest-value test surface.
- **PipelineExecutor init step integration** — Integration tests verifying failure semantics at each scope (pre, post, workload-level).
- **WorkloadImportService** — Unit tests for hybrid JSON parsing, validation (empty strings, 4096-char limit), and correct deserialization of all init step fields.
- **WorkloadRunDispatcher payload construction** — Verify init steps are correctly deserialized from JSON columns and populated into the runtime payload.
- **Orchestrator API** — Integration tests for create revision endpoint with init step fields; verify round-trip persistence.

### Prior Art

- **Agent-side tests:** Look at existing tests for `InstallOrUpgrade` (process execution, exit code handling, timeout behavior). `InitStepExecutor` follows the same Process.Start() pattern with additional env var injection and per-command timeline reporting.
- **Orchestrator-side tests:** Look at existing tests for `WorkloadImportService` (JSON parsing) and `WorkloadRunDispatcher` (payload construction).
- **Pipeline tests:** Look at existing `PipelineExecutor` integration tests that verify the sequential execution of acquire, install, verify steps.

---

## Out of Scope

- **Full CRUD UI for init steps**: Frontend is display-only in this phase. JSON import is the authoring path. Editing init steps in the UI is deferred.
- **Per-step shell override**: Only `defaultShell` at workload level. No per-command shell prefix syntax.
- **Configurable timeouts per step**: Hardcoded per-step-type defaults only (60s pre, 120s post, 180s post-workload). No workload-level timeout override.
- **Configurable exit codes**: Only exit code 0 is treated as success. No `ExpectedExitCodes` field for init steps.
- **Rollback init step support**: Init steps are skipped entirely during rollback mode. Rollback itself is non-priority.
- **Revision diff for init steps**: No side-by-side diff comparison of init step changes between revisions.
- **Max command count limits**: No enforced limit on the number of init step commands. Self-limitation by human authorship.
- **Full stdout/stderr streaming**: Current pattern (status-only on success, error message on failure) is retained. Log aggregation infrastructure is deferred.
- **`DEPLOY_RETRY_COUNT` environment variable**: Not implemented in this phase. Authors are responsible for command idempotency.
- **Per-step output artifact storage**: No separate log files for init step output.
- **Secret/environment variable injection from orchestrator**: `DEPLOY_*` vars are agent-generated only. No orchestrator-defined env vars.
- **Parallel init step execution**: Commands run sequentially, one process at a time.

---

## Further Notes

- The `defaultShell` supports any value that can be invoked as a process (e.g., `powershell`, `cmd`, `pwsh`, `bash` if available). The agent does not validate the shell value at assignment time — it will fail at execution time if the shell isn't present on the node.
- The working directory for all init steps is `Path.GetTempPath()`. This does not restrict access to system PATH, PowerShell cmdlets, or absolute paths. It only affects relative path resolution within commands.
- Workload authors are responsible for making init step commands idempotent. The system provides no retry-detection. If a run is retried, init steps execute again.
- Init steps exist exclusively in the workload definition — not on the package manifest. Package manifests remain general-purpose, reusable across workloads.
- The Diff Engine controls whether init steps run: unchanged packages skip all init steps. Use `ForceInstall` to re-configure unchanged packages.
- Output from stdout/stderr is captured for inclusion in the error `Message` field of the timeline entry on failure, but not stored in a separate log file in this phase.
