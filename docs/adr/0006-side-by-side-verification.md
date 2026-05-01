# SideBySide Verification Uses Explicit Detection Path

When `UpgradeBehavior` is `SideBySide`, the old version remains on disk after the new version installs. `PostInstallVerify` can fail if the detection command resolves ambiguously — for example, `python --version` returning the PATH-first old version even though the new version installed successfully.

We decided that `SideBySide` packages must use an explicit, unambiguous detection path for `PostInstallVerify` (Option 3). Rather than skipping verification entirely (Option 2) or hoping the default detection resolves correctly (Option 1), the package's `DetectionConfig` must specify a full binary path (e.g., `C:\Python314\python.exe --version`) or another discriminator that targets only the new installation.

This preserves the pipeline's fail-fast guarantee without requiring a separate verification skip mechanism. If a `SideBySide` package's detection is ambiguous, the operator must fix the detection config rather than the pipeline ignoring verification failures.
