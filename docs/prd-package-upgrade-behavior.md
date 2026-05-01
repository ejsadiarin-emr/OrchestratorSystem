# Package Upgrade Behavior for Differential Updates

## Problem Statement

When a workload updates from one revision to another, packages that change version are currently handled naively: the agent runs the new installer without considering whether the old version needs removal first. This fails for installers that leave the old version behind (e.g., Python WiX Burn bundles), where the old version remains on disk and in PATH. Post-install verification then detects the wrong version, causing the pipeline to fail.

The immediate symptom: updating Python 3.13.3 to 3.14.4 results in both versions coexisting, `python --version` returns 3.13.3, and `PostInstallVerify` reports a version mismatch. The same issue affects any package whose installer does not perform in-place upgrades.

The root cause is that the pipeline has no concept of per-package upgrade semantics. All changed packages are treated identically: acquire the new artifact, run the installer, verify. There is no branch for "uninstall old first" or "install alongside old."

## Solution

Introduce a per-package `UpgradeBehavior` setting with two values:

- **`InPlace`** (default): Run the new installer only. The installer handles replacing the old version. Used for self-upgrading installers like DBeaver, Git, 7-Zip, and MSI packages with UpgradeTable.
- **`UninstallFirst`**: Uninstall the old version before installing the new one. Used for installers that don't remove the old version, such as Python and .NET Runtime, where running the new installer leaves the previous version on disk.

The pipeline executor branches on this value during differential updates. For `UninstallFirst` changed packages, the executor looks up the old package's configuration from `CurrentPackages` and runs `UninstallPackage` in Phase 1 before the new version is installed in Phase 2. For `InPlace` changed packages, Phase 1 is skipped.

Pre-check overrides are extended to handle interrupted updates: if a changed package's pre-check shows the old version is already gone (`NotPresent`), the diff is overridden to `added` (fresh install). If the new version is already present (`AlreadySatisfied`), the diff is overridden to `unchanged` (skip).

## User Stories

1. As a workload administrator, I want to declare each package's upgrade behavior in the manifest, so that the agent handles version transitions correctly without guessing.
2. As a workload administrator, I want the system to reject packages with missing upgrade behavior configuration, so that silent misconfiguration is impossible.
3. As an operator updating a workload containing Python, I want the old Python version uninstalled before the new one is installed, so that `python --version` returns the correct version after the update.
4. As an operator updating a workload containing DBeaver, I want the new installer to run directly without uninstalling first, so that my database connections and workspace settings are preserved.
5. As an operator, I want interrupted updates to be resumable without manual cleanup, so that if an uninstall succeeded but the install failed, retrying the update treats the package as a fresh install.
6. As an operator, I want the pipeline to skip installing a package that is already at the target version, so that retries and redundant runs are fast and idempotent.
7. As a developer adding a new package type, I want explicit upgrade behavior documentation and validation, so that I don't accidentally deploy a package with the wrong transition semantics.
8. As an auditor, I want the upgrade behavior of every package to be visible in the manifest and database, so that I can verify correctness without reading the installer binary's internals.
9. ~~As an operator managing SQL Server workloads, I want to mark packages as `SideBySide` and provide custom `postInitSteps` for migration, so that databases remain online during the cutover window.~~ *(Removed — `SideBySide` was removed from the system. Use `InPlace` for stateful apps or compose `UninstallFirst` with workload-level `postInitSteps` for staged migrations.)*
10. As a test engineer, I want the test workload manifests to include `upgradeBehavior` and `uninstallArgs` fields, so that end-to-end tests exercise both code paths (`InPlace` and `UninstallFirst`).
11. As a build engineer, I want the artifact download script to generate complete manifests including upgrade behavior, so that test artifacts are representative of production manifests.
12. As an agent developer, I want the diff engine to override `changed` packages based on pre-check ground truth, so that the pipeline executor sees a diff that reflects actual machine state.
13. As an orchestrator developer, I want the package creation API to validate `UpgradeBehavior` is present, so that the database never contains packages with undefined transition semantics.
14. As a frontend user viewing a workload run timeline, I want to see "UninstallPackage" steps for `UninstallFirst` packages in the correct order, so that I can trace what happened during an update.
15. As an operator debugging a failed update, I want clear log messages indicating why a package was categorized as `added` vs `changed` vs `unchanged`, so that I can understand pre-check override decisions.
16. As a database administrator, I want existing packages in the database to default to `InPlace` after migration, so that backward compatibility is preserved while new packages must explicitly declare behavior.
17. As a security reviewer, I want the system to reject pattern registry defaults, so that no hidden inference logic can silently misclassify a package's upgrade behavior.
18. ~~As a package author, I want `SideBySide` packages to warn me if detection uses a bare command (e.g., `python --version`) instead of a fully qualified path, so that verification ambiguity is caught at execution time.~~ *(Removed — `SideBySide` was removed from the system.)*
19. As an operator running a rollback, I want the same upgrade behavior logic to apply in reverse, so that rolling back a `UninstallFirst` package reinstalls the old version cleanly.
20. As a test engineer, I want unit tests for the diff engine that cover all pre-check override combinations, so that regressions in diff categorization are caught before integration.

## Implementation Decisions

### Modules Built or Modified

**Shared Contract Layer**
- `InstallAdapterConfig` (shared contract between orchestrator and agent): Add `UpgradeBehavior` string property with default `"InPlace"`. This is the canonical representation transmitted in both API and SignalR payloads.

**Orchestrator Backend**
- `PackageEntity` (database entity): Add `UpgradeBehavior` string column. Existing rows are backfilled to `"InPlace"` via migration.
- Package creation/update API: Validate that `UpgradeBehavior` is present and is one of the three allowed values. Reject with 400 if missing or invalid. No pattern registry fallback.
- `BuildPendingPackageDto` and `BuildPackageAssignments` (payload construction): Populate `UpgradeBehavior` from `PackageEntity` for both target and current packages.

**Agent Backend**
- `DiffEngine` (agent pipeline): Add two pre-check override rules:
  - `changed` + `NotPresent` → `added`
  - `changed` + `AlreadySatisfied` → `unchanged`
  These are applied after the existing override rules and before the pipeline executor consumes the diff.
- `PipelineExecutor` (agent pipeline): In Phase 1 (uninstall), iterate over `removed` packages AND `changed` packages whose `UpgradeBehavior == "UninstallFirst"`. For each `UninstallFirst` changed package, resolve the old configuration from `CurrentPackages` by matching `Name`, then call `UninstallPackage.ExecuteAsync(oldConfig, ...)`. Descending `PackageIndex` order is preserved for all uninstall operations.


**Test Artifacts and Scripts**
- Test workload manifests (`workloads-older.json`, `workloads-newer.json`): Add `upgradeBehavior` and `uninstallArgs` fields to package definitions.
- Artifact download script: Generate `upgradeBehavior` and `uninstallArgs` in manifest output.

### Interfaces

**`InstallAdapterConfig`** gains one property:
- `UpgradeBehavior: string` — `"InPlace"` | `"UninstallFirst"`

**`PackageEntity`** gains one property:
- `UpgradeBehavior: string` — same domain

**`DiffEngine.ComputeDiff`** signature unchanged; behavior extended internally via pre-check overrides.

**`PipelineExecutor.ExecuteAsync`** signature unchanged; Phase 1 logic branches on `UpgradeBehavior` from the target package's `InstallAdapter` while resolving old config from `CurrentPackages`.

### Schema Changes

- Add `UpgradeBehavior` column to `PackageEntity` table (string, non-nullable, default `"InPlace"`).
- Migration backfills existing rows to `"InPlace"`.

### API Contracts

- Orchestrator package creation/update endpoints reject requests with missing or invalid `UpgradeBehavior`.
- `PendingWorkloadRunResponse` and `AssignRunPayload` both carry `UpgradeBehavior` in `InstallAdapterConfig` for every package.
- `CurrentPackages` continues to carry full `InstallAdapterConfig` (including `UninstallArgs` and `UpgradeBehavior`) so the agent can resolve old configuration for uninstall.

### Specific Interactions

**Uninstall of `UninstallFirst` changed packages:**
1. Diff engine categorizes package as `changed` (name present in both current and target, version differs).
2. Pre-check runs; if `NotPresent`, override to `added`. If `AlreadySatisfied`, override to `unchanged`.
3. If still `changed`, Phase 1 checks `package.InstallAdapter.UpgradeBehavior`.
4. If `"UninstallFirst"`, resolve `oldPackage = CurrentPackages.First(p => p.Name == changedPackage.Name)`.
5. Call `UninstallPackage.ExecuteAsync(oldPackage.InstallAdapter, ...)` using the old package's `Command` and `UninstallArgs`.
6. If uninstall fails, pipeline halts (fail-fast).
7. Phase 2 installs the new package normally (acquire → install → verify).

## Testing Decisions

### What Makes a Good Test

Tests should verify **external behavior** (diff categorization, pipeline step sequencing, validation errors) rather than implementation details (internal list manipulation, specific loop ordering). Each test should be independent, fast, and deterministic.

### Modules to Test

**High Priority — Unit Tests**
- `DiffEngine` with pre-check overrides: Test all combinations of base diff + pre-check status → expected final categories. Cover the two new override rules (`changed`→`added`, `changed`→`unchanged`) plus all existing rules.
- `PipelineExecutor` Phase 1 branching: Mock `UninstallPackage.ExecuteAsync` and verify it is called with the correct old config for `UninstallFirst` changed packages, and NOT called for `InPlace` changed packages.
- Orchestrator package creation validation: Test that missing/invalid `UpgradeBehavior` returns 400, and valid values succeed.

**Medium Priority — Integration Tests**
- End-to-end update flow: Create a workload with a `UninstallFirst` package, run an update, verify the timeline contains `UninstallPackage` followed by `InstallOrUpgrade`.
- Pre-check resilience: Simulate an interrupted update (old version already gone) and verify the retry treats the package as `added` and skips uninstall.

**Low Priority — Contract Tests**
- Verify `UpgradeBehavior` is present in both API (`PendingWorkloadRunResponse`) and SignalR (`AssignRunPayload`) contracts.

### Prior Art

- `DiffEngineTests.cs` (agent unit tests) — table-driven tests for diff categorization. Extend with pre-check override test cases.
- `PipelineExecutorTests.cs` (agent integration tests) — mock-based tests for pipeline step sequencing. Add test cases for `UninstallFirst` branching.
- `WorkloadRunsApiContractTests.cs` / `WorkloadsApiContractTests.cs` (orchestrator integration tests) — API contract validation. Add test for package creation with `UpgradeBehavior`.

## Out of Scope

- **Pattern registry / auto-inference**: No automatic detection of upgrade behavior from filename, installer type, or heuristics. Admins must explicitly declare.
- **Frontend UI changes**: The orchestrator web frontend is not modified. `UpgradeBehavior` is visible in API responses but no new UI fields are added.

- **Post-uninstall verification**: After Phase 1 uninstall, no verification step checks that the old version is actually gone. Exit code is trusted.
- **Rollback-specific logic**: Rollback reuses the same diff and upgrade behavior logic as update, just with reversed current/target. No separate rollback branching.
- **Workload-level upgrade behavior**: The setting is per-package only. Workloads cannot override a package's intrinsic behavior.
- **Version-specific upgrade behavior**: A package's upgrade behavior is assumed constant across all versions. No per-version override.

## Further Notes

### Backward Compatibility

Existing packages in the database are backfilled to `"InPlace"` via migration. This preserves the pre-existing behavior (run new installer only) for all existing workloads. New packages created after the migration must explicitly declare `UpgradeBehavior` or the creation API rejects them.

### Installer Family Reference

While the system does not auto-infer upgrade behavior, the following table documents known behaviors for common installer families to guide admin configuration:

| Installer Family | Typical Behavior | Example Packages |
|---|---|---|
| MSI with UpgradeTable | `InPlace` | Most well-authored MSIs |
| WiX Burn Bundle | `UninstallFirst` | Python, .NET Runtime |
| Inno Setup | `InPlace` | Git |
| NSIS | `InPlace` | 7-Zip, DBeaver, Notepad++ |
| Custom EXE | Must be tested | — |

Admins should test by installing version 1, then installing version 2 without removing version 1. If version 1 remains on disk, the package needs `UninstallFirst`.

### Failure Mode: Wrong `UpgradeBehavior`

If an admin sets `InPlace` on a package that should be `UninstallFirst`, the symptom is a `PostInstallVerify` version mismatch after update. The fix is to change the package's `UpgradeBehavior` to `UninstallFirst` and retry. The old version will then be uninstalled in Phase 1 before the new version is installed.

If an admin sets `UninstallFirst` on a stateful package that should be `InPlace` (e.g., SQL Server), the uninstaller may destroy data. This is why the system requires explicit declaration and does not guess.

### Relationship to Existing Decisions

This PRD implements Decisions 6 (superseded), 7, 10, 11, 15, and 18 from `workload-differential-update-rollback.md`, and resolves open questions 19 and 20 from the same document. It is cross-referenced by ADRs 0005 through 0008.
