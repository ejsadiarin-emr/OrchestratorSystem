# 003 - Agent: InitStepExecutor Module

## Type

AFK

## Parent PRD

[docs/prd-init-steps.md](../prd-init-steps.md)

## Blocked by

- Blocked by #001 (Contracts & Database Schema Foundation) — for `PackageAssignment` and `AssignRunPayload` types only
- Can start in parallel with #002

## What to build

Create a new deep module `InitStepExecutor` (`apps/agent/backend/Steps/`) that encapsulates all shell command execution logic behind a simple interface. This module has no knowledge of pipeline ordering, diff outcomes, or workload semantics — it only knows "execute one command and report its result."

**Interface:**

```csharp
Task<InitStepResult> ExecuteAsync(
    string command, string defaultShell, string stepName,
    Dictionary<string, string> envVars, int timeoutSeconds,
    int packageIndex, Func<StepStatusPayload, Task> sendStatusAsync,
    CancellationToken ct)
```

**Internal responsibilities:**

1. Launch `Process.Start()` with `FileName = defaultShell`, `Arguments = "-Command <command>"`
2. Set `WorkingDirectory = Path.GetTempPath()`
3. Inject `DEPLOY_*` environment variables ephemerally via `Process.StartInfo.Environment` — variables are NOT persisted. See [env vars decision](../decisions/20260430-workload-init-steps/workload-init-steps-env-vars-20260430-001200.md) for the full variable table.
4. Apply per-step-type timeout; call `Process.Kill()` on timeout → treat as failure
5. Capture stdout/stderr; include in error `Message` field on failure
6. Send timeline status via `sendStatusAsync` with step name (e.g., `PreInit_0_0`, `PostInit_1_0`, `PostWorkload_0`)
7. Return exit code 0 as success, any non-zero as failure

**Timeouts (hardcoded per step type):**

| Step type | Timeout |
|-----------|---------|
| preInitSteps | 60 seconds |
| postInitSteps | 120 seconds |
| preWorkloadSteps | 60 seconds |
| postWorkloadSteps | 180 seconds |

**Exit code handling**: Only exit code 0 is success. No configurable `ExpectedExitCodes`.

**DEPLOY_* env vars** are injected from a `Dictionary<string, string>` passed in by the caller. The caller is responsible for constructing the correct variable set depending on scope (e.g., omitting `DEPLOY_ARTIFACT_PATH` for preInitSteps). `InitStepExecutor` does not know which scope it's running in.

**Encapsulation principle**: This module is testable in isolation with a mocked shell process or a test batch file. It does not depend on any agent infrastructure beyond `Process.Start()` and the `StepStatusPayload` contract.

## Acceptance criteria

- [ ] `ExecuteAsync` launches a process with the correct `FileName` (shell), `Arguments` (`-Command <command>`), and `WorkingDirectory` (`Path.GetTempPath()`)
- [ ] Environment variables passed in `envVars` are present in `Process.StartInfo.Environment` (ephemeral, not persisted)
- [ ] Timeout applies correctly: exceeded timeout triggers `Process.Kill()`, result is failure
- [ ] Exit code 0 returns success; any non-zero returns failure
- [ ] On failure, stdout and stderr are captured and included in the result/error message
- [ ] `sendStatusAsync` is called with correct `StepStatusPayload` (step name, status, package index)
- [ ] `CancellationToken` cancels the process when triggered
- [ ] Unit tests with mocked process: verify correct invocation args, env var injection, timeout enforcement, exit code interpretation
- [ ] Unit tests: timeout exceeded → `Process.Kill()` called → failure result
- [ ] Unit tests: successful command → `sendStatusAsync` called with Running → Success sequence

## Referenced decisions

- [Execution Model & Semantics](../decisions/20260430-workload-init-steps/workload-init-steps-20260430-000543.md)
- [Environment Variables](../decisions/20260430-workload-init-steps/workload-init-steps-env-vars-20260430-001200.md)
- [Working Directory](../decisions/20260430-workload-init-steps/workload-init-steps-working-dir-20260430-001400.md)
- [Failure Semantics](../decisions/20260430-workload-init-steps/workload-init-steps-failure-semantics-20260430-001500.md)
- [Exit Code Handling](../decisions/20260430-workload-init-steps/workload-init-steps-exit-codes-20260430-001600.md)
- [Timeout Configuration](../decisions/20260430-workload-init-steps/workload-init-steps-timeout-20260430-001700.md)
- [Timeline Reporting](../decisions/20260430-workload-init-steps/workload-init-steps-timeline-20260430-001000.md)
- [Output Capture](../decisions/20260430-workload-init-steps/workload-init-steps-output-capture-20260430-003100.md)
