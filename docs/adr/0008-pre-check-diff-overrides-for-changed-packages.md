# PreCheck Diff Overrides for Changed Packages

During differential updates, the `DiffEngine` categorizes packages as `added`, `removed`, `changed`, or `unchanged` by comparing `CurrentPackages` (from the orchestrator) against `Packages` (target). Before execution, `PreCheck` probes each package's actual on-disk state using `DetectionConfig`. The diff categories can be overridden based on PreCheck results.

The existing override rules handled `added` and `unchanged` packages only:
- `added` + `AlreadySatisfied` → `unchanged`
- `added` + `WrongVersion` → `changed`
- `unchanged` + `WrongVersion`/`NotPresent` → `changed`

We added two override rules for `changed` packages:
- `changed` + `NotPresent` → `added`: The old version was already removed (e.g., interrupted previous run), so no uninstall is needed. Treat as fresh install.
- `changed` + `AlreadySatisfied` → `unchanged`: The target version is already installed (e.g., manual admin intervention or completed previous run), so no work is needed.

Without these overrides, `changed` packages with `UpgradeBehavior = "UninstallFirst"` would attempt to uninstall an already-removed package (wasting time and potentially failing), or re-install an already-present package (noisy but usually harmless). The overrides make the diff reflect ground truth after probing.

These overrides are applied before Phase 1 (uninstall) and Phase 2 (install) execute, so the pipeline executor sees the corrected categories.

---
## Consequences

- **Positive**: Resilient to interrupted updates and manual admin fixes. Diff reflects actual machine state.
- **Trade-off**: `PreCheck` must run before every update, adding a small latency cost (acceptable for correctness).
- **Risk**: None. Overrides only move packages to categories with *less* work, never more.
