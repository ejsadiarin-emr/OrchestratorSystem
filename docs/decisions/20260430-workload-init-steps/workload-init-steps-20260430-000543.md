# Decision: Workload Init Steps — Execution Model and Semantics

**Date:** 2026-04-30
**Status:** Resolved

## Context

Adding initialization steps (pre/post) to the workload/package pipeline. Need to determine execution model and failure semantics.

## Decisions

### D1: Init steps extend the per-package pipeline

preInitSteps and postInitSteps are additional pipeline steps that run within each package's install cycle:

- `preInitSteps` → runs **before** `AcquireArtifact` for each package
- `postInitSteps` → runs **after** `PostInstallVerify` for each package

This preserves the current sequential model (process packages by PackageIndex, one at a time).

### D2: Init steps are opaque shell commands

Each step is a raw shell command string executed directly by the agent. No declarative interpretation.

### D3: Default shell is workload-level, PowerShell for now

A `defaultShell` field at the workload definition level determines how commands are invoked. Default: `powershell`.

### D4: Failure semantics

| Step type | Failure behavior |
|---|---|
| preInitSteps | Skip this package's install (warn, don't halt pipeline) |
| postInitSteps | Mark package as failed (warn, don't halt pipeline) |

Both log warnings and continue. The pipeline does NOT abort on init step failure.

## Open

- Where do init steps live in the schema? (package manifest vs workload definition level — see next decision)