# Decision: Init Steps Failure Semantics

**Date:** 2026-04-30
**Status:** Resolved

## Q8: PreInit Step Failure Behavior

**Decision:** First-failure-stop — if any command in `preInitSteps` returns a non-zero exit code, immediately stop executing remaining preInit commands for that package and skip the package install.

### Rationale

- Mirrors `&&` chaining behavior in shells — minimal surprise
- Avoids wasting time running commands after a prerequisite is known broken
- Workload authors can split checks into separate preInit commands if they want partial continuation

### Complete failure semantics matrix

| Step Type | Command Fails | Effect |
|---|---|---|
| preInitSteps | Non-zero exit code | Stop executing remaining preInit commands for this package. Skip the package install (warn, don't halt workload). |
| postInitSteps | Non-zero exit code | Mark package as failed (warn, don't halt workload). Continue to next package. |
| postWorkloadSteps | Non-zero exit code | First-failure-stop (same as preInit). Stop executing remaining postWorkload commands. Mark workload run as failed. |

### Working directory context (from Q7)

`WorkingDirectory` for all init step commands is `Path.GetTempPath()`. This does NOT restrict binary/command access — anything on system PATH, PowerShell cmdlets, and absolute paths all work. It only affects relative path resolution.

## Q10: Command Execution Model

**Decision:** Init step commands are executed via `powershell -Command <step>` (respecting `DefaultShell` from workload definition). Each command in the steps array is invoked as a **separate `Process.Start()`** invocation.

### Rationale

- `DefaultShell` defaults to `"powershell"` — commands are authored with PowerShell cmdlets in mind (`Invoke-WebRequest`, etc.)
- If someone wants CMD, they change `DefaultShell` to `"cmd"`
- Separate processes align with Q4 (per-command timeline entries) and Q8 (first-failure-stop per command)

### Execution pattern

```
For each command in preInitSteps:
    Process.Start("powershell", "-Command <step>")
    if exit code != 0: stop, skip package install
    SendStepStatusAsync(PreInit_{pkgIdx}_{stepIdx}, status)

For each command in postInitSteps:
    Process.Start("powershell", "-Command <step>")
    if exit code != 0: mark package failed, continue to next
    SendStepStatusAsync(PostInit_{pkgIdx}_{stepIdx}, status)

For each command in postWorkloadSteps:
    Process.Start("powershell", "-Command <step>")
    if exit code != 0: stop, mark workload run failed
    SendStepStatusAsync(PostWorkload_{stepIdx}, status)
```

### Process properties

| Property | Value |
|---|---|
| FileName | `DefaultShell` value (default: `"powershell"`) |
| Arguments | `"-Command " + step` |
| WorkingDirectory | `Path.GetTempPath()` |
| Environment | `DEPLOY_*` variables from Q5 |
| RedirectStandardOutput | true |
| RedirectStandardError | true |