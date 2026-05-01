# SideBySide Verification Uses Explicit Detection Path

> **Status: Superseded.** This ADR is retained for historical context only. The `SideBySide` `UpgradeBehavior` value was removed from the system. Side-by-side installation scenarios are now handled by composing `UninstallFirst` with explicit `postInitSteps`, or by using workload-level orchestration scripts.

When `UpgradeBehavior` was `SideBySide`, the old version remained on disk after the new version installed. `PostInstallVerify` could fail if the detection command resolved ambiguously — for example, `python --version` returning the PATH-first old version even though the new version installed successfully.

We previously decided that `SideBySide` packages must use an explicit, unambiguous detection path for `PostInstallVerify` (Option 3). Rather than skipping verification entirely (Option 2) or hoping the default detection resolves correctly (Option 1), the package's `DetectionConfig` had to specify a full binary path (e.g., `C:\Python314\python.exe --version`) or another discriminator that targets only the new installation.

This preserved the pipeline's fail-fast guarantee without requiring a separate verification skip mechanism. If a `SideBySide` package's detection was ambiguous, the operator had to fix the detection config rather than the pipeline ignoring verification failures.

## Why SideBySide Was Removed

Operational experience showed that `SideBySide` created more problems than it solved:

1. **PATH ambiguity**: Even with explicit detection paths, leaving the old version on disk caused environment-level conflicts (PATH ordering, registry references, file associations).
2. **Verification complexity**: `PostInstallVerify` had to be taught to warn about bare commands for `SideBySide` packages, adding agent-side complexity.
3. **Overlap with existing primitives**: The same outcomes — staged migration, config transformation, warm cutover — can be achieved with `UninstallFirst` plus `postInitSteps` that pause between uninstall and install, or with workload-level pre/post steps.

For these reasons, `SideBySide` was removed from `UpgradeBehaviorValidator.AllowedValues` and all related documentation was updated.
