# Per-Package UpgradeBehavior for Differential Updates

When a workload updates from one revision to another, packages that change version may need their old version uninstalled before the new installer runs. The naive approach — always running the new installer — fails for installers that don't remove the old version, such as Python (WiX Burn), where the old version persists and `PostInstallVerify` detects the wrong version.

We decided to add an `UpgradeBehavior` field to `InstallAdapterConfig` with two values: `InPlace` (default) and `UninstallFirst`. The agent pipeline executor branches on this value during differential updates: `UninstallFirst` packages are uninstalled in Phase 1 (using the *old* package's `InstallAdapter` config from `CurrentPackages`) before the new version is installed in Phase 2. `InPlace` packages skip Phase 1 uninstall.

This is a package-level setting rather than workload-level because upgrade behavior is an intrinsic property of the installer binary (Python always leaves the old version behind; Git always replaces in-place). Setting it per workload would force repetition and risk inconsistency.

**Why not pattern registry defaults?** We considered auto-populating `UpgradeBehavior` from a hardcoded pattern registry (`python-*` → `UninstallFirst`, etc.), but rejected it. Admins must explicitly declare the behavior in every package manifest. This prevents silent misconfiguration for packages we haven't catalogued and makes the intent visible at the package definition layer.

**Note:** A previous revision of this ADR included a third value, `SideBySide`. That value was removed (see ADR-0006, superseded) because it added complexity without clear operational benefit — side-by-side scenarios are better handled by composing `UninstallFirst` with explicit `postInitSteps` or by using workload-level orchestration.
