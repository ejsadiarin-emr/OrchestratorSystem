# Workload Differential Update & Rollback — Grill Me Decisions Log

> **See also:** [update-mode-deep-dive.md](update-mode-deep-dive.md) — detailed explanations, walkthrough examples, configuration reference, and industry analogies for the upgrade behavior model.

## Context
The agent's workload-runs pipeline currently treats `install`, `update`, and `rollback` identically — it runs the full `AcquireArtifact → InstallOrUpgrade → PostInstallVerify` sequence for every package in the target revision, regardless of mode. This grilling session resolved how to make `update` and `rollback` actually work as differential operations for the PoC.

---

## Summary

| # | Topic | Decision |
|---|---|---|
| 1 | Update strategy | Differential update (only changed packages touched) |
| 2 | Before-state source | Orchestrator enriches `AssignRunPayload` with `CurrentPackages` |
| 3 | Revision state accuracy | `CurrentRevisionId` updates only on `Complete` |
| 4 | Diff computation | Agent computes diff from raw package lists |
| 5 | Diff identity | Diff by `Name` (not `PackageId`) |
| 6 | Changed packages | ~~Install target version directly (no uninstall-first)~~ Superseded by Decision 18 |
| 7 | Uninstall step | Add `UninstallPackage` step with `UninstallArgs` |
| 8 | Uninstall artifact acquisition | Conditional — only if `UninstallArgs` contains `{artifactPath}` |
| 9 | Post-uninstall verification | Skipped for PoC |
| 10 | Execution order | Two-phase: uninstall first, then install |
| 11 | Error handling | Fail-fast on uninstall errors |
| 12 | Step status indexing | Use previous revision's `PackageIndex` for uninstall |
| 13 | Diff ownership | `PipelineExecutor` computes the diff |
| 14 | No current state fallback | Treat as full install |
| 15 | `CurrentPackages` shape | Full `PackageAssignment` with all config |
| 16 | `UninstallArgs` source | Manifest (primary) + pattern registry (fallback) |
| 17 | Test data | `workloads-newer.json` removes `test-agent` |
| 18 | Upgrade behavior for changed packages | Per-package `UpgradeBehavior` enum on `InstallAdapterConfig`: `InPlace` (default), `UninstallFirst`, `SideBySide` |

---

## Decision 1: Differential Update, Not Full Replacement or Naive Forward Install
**Question:** When updating a workload, should the agent compare against what's installed and only touch changed packages, uninstall everything and reinstall fresh, or just install all target packages blindly?

**Decision:** Differential update. The agent computes a diff between the **current** revision's packages and the **target** revision's packages, then only executes actions for packages that actually changed.

Diff categories:
- `added` — package exists in target but not in current → full install pipeline (`AcquireArtifact → InstallOrUpgrade → PostInstallVerify`)
- `changed` — package exists in both but version differs → install target version (trust installer to handle in-place transition)
- `removed` — package exists in current but not in target → uninstall
- `unchanged` — package exists in both with same version → **skipped entirely**

For `install` mode (no previous revision), all target packages are treated as `added`.

**Rationale:** Most efficient, least disruptive, and makes rollback tractable (we know exactly what changed). The orchestrator already calculates delta steps for API consumers; this decision extends differential logic into the agent runtime.

---

## Decision 2: Orchestrator Enriches `AssignRunPayload` with `CurrentPackages`
**Question:** The agent is currently stateless (no local persistence). How does it know the "before" snapshot to compute a diff?

**Decision:** The orchestrator queries the node's last successfully installed revision and sends its packages as `CurrentPackages: List<PackageAssignment>` inside the `AssignRunPayload` SignalR message.

**Contract change:** Add one field to `AssignRunPayload`:
```csharp
public List<PackageAssignment> CurrentPackages { get; set; } = new();
```

For `install` mode, `CurrentPackages` is empty. For `update`/`rollback`, it contains the packages from the node's `CurrentRevisionId` at the time of run creation.

**Rationale:** Single-message SignalR flow, no new API surface, no local file I/O on the agent, and the orchestrator is the source of truth (avoids drift if agent is reinstalled or state is wiped).

---

## Decision 3: `CurrentRevisionId` Only Updates on `Complete`
**Question:** In `NodeWorkloadStateService.cs`, `CurrentRevisionId` is updated to the **new** revision on `AckClaim` — before any packages are actually installed. If the agent fails mid-pipeline, the orchestrator falsely believes the node is on the new revision. How do we fix this?

**Decision:** Move the `CurrentRevisionId = run.RevisionId` assignment from `HandleAckClaimAsync` to `HandleCompleteAsync` in `NodeWorkloadStateService`.

`AckClaim` still creates the state record if missing, but leaves `CurrentRevisionId` unchanged (reflecting the actually-installed revision, not the in-progress one).

**Rationale:** The "before" baseline for differential update must reflect what is **actually installed**, not what is **in progress**. If a run fails mid-pipeline, the orchestrator must still know what the node actually has installed. The next differential baseline drawn from `CurrentRevisionId` will be accurate.

---

## Decision 4: Raw Package Lists, Not Pre-Computed Delta Steps
**Question:** Should the orchestrator pre-compute delta actions (e.g., `[{action:"remove", package:"git"}]`) or send raw package lists and let the agent compute the diff?

**Decision:** Raw package lists. The orchestrator sends `CurrentPackages` and target `Packages`. The **agent** computes the diff locally.

**Diff algorithm (by `Name`):**
```
added     = targetPackages  - currentPackages (by Name)
removed   = currentPackages - targetPackages  (by Name)
changed   = intersection where Version differs
unchanged = intersection where Version equals
```

For `install` mode with no `CurrentPackages` → treat all target packages as `added` (full install).

**Rationale:** The agent needs both states to make intelligent decisions. `update` and `rollback` use the exact same diff algorithm — only the direction changes (which revision is "current" vs "target"). The orchestrator's existing delta endpoint (`GET /api/workload-runs/{runId}/steps`) remains API-facing and is not coupled to the runtime contract.

---

## Decision 5: Diff by `Name`, Not `PackageId`
**Question:** Bulk import creates `PackageEntity` records with a **version-specific** deterministic ID: `DeterministicGuid("{packageId}-{version}")`. This means `git-2.47.1` and `git-2.48.1` have different `PackageId`s. Diffing by `PackageId` would incorrectly classify every version change as "removed old + added new" instead of "changed." How do we fix this?

**Decision:** Add `Name` to `PackageAssignment` (populated from `PackageEntity.Name`, the logical name without version) and diff by `Name`.

**Contract change:**
```csharp
public string Name { get; set; } = string.Empty;
```

Diff algorithm:
```
added     = targetPackages  - currentPackages (by Name)
removed   = currentPackages - targetPackages  (by Name)
changed   = intersection where Version differs
unchanged = intersection where Version equals
```

`PackageId` is still sent for reference but is not used for diff logic.

**Rationale:** Minimal contract change, no database migration needed, and correctly identifies same-logical-package version changes across revisions.

---

## Decision 6: Changed Packages = Install Target Version (No Uninstall-First) — SUPERSEDED by Decision 18
**Question:** When a package like `git` changes from `2.47.1` → `2.48.1`, should the agent uninstall the old version first, then install the new one?

**Original Decision:** No. Run the **target version's installer** directly. It does **not** uninstall the old version first.

**Original Rationale:** Most well-behaved installers (MSI, Inno Setup, NSIS, WiX Burn) detect existing installations and perform in-place upgrades/downgrades automatically. Running the new installer is the standard deployment tool behavior (e.g., SCCM, Intune, Chocolatey). This avoids needing to locate and run the old version's uninstaller.

**Superseded by Decision 18:** The original assumption that "most installers handle in-place upgrades" was too broad. Python's installer (WiX-based) installs side-by-side rather than upgrading — the old version remains alongside the new one, causing `PostInstallVerify` to detect the wrong version. The correct approach is per-package `UpgradeBehavior` (Decision 18), which lets workload authors declare whether a package needs uninstall-first, in-place upgrade, or side-by-side handling.

---

## Decision 7: Add a Minimal Uninstall Step
**Question:** For removed packages, the agent currently has no uninstall capability — only `AcquireArtifact`, `InstallOrUpgrade`, and `PostInstallVerify`. How do we add uninstall without bloating scope?

**Decision:** Create an `UninstallPackage` step. It reuses the same process-spawning logic as `InstallOrUpgrade` but runs the uninstall command.

**Contract change:** Add one string field to `InstallAdapterConfig`:
```csharp
public string UninstallArgs { get; set; } = string.Empty;
```

**Execution logic:**
- Replace `{artifactPath}` placeholder in `UninstallArgs` (if present) with the actual artifact path
- Spawn process with `UseShellExecute = false`
- Validate exit code against `ExpectedExitCodes`
- Return structured error on failure (`exit_code_N`, `command_not_found`, `uninstall_timeout`)

**MSI example:** `msiexec.exe /x "{artifactPath}" /qn /norestart`
**EXE example:** `"{artifactPath}" /uninstall /S` (installer-specific)

**Rationale:** One contract field + one step class. Minimal scope. Reuses existing `Process`-spawning and exit-code validation infrastructure from `InstallOrUpgrade`.

---

## Decision 8: Conditional Artifact Acquisition for Uninstall
**Question:** Does `UninstallPackage` need to acquire (download) the old artifact before running the uninstall command?

**Decision:** Only if `UninstallArgs` contains the `{artifactPath}` placeholder.

- **MSI-style uninstall** (`/x "{artifactPath}"`) → may need the artifact file
- **EXE-style uninstall** (`/uninstall /S`) → may need the original EXE path
- **Registry/product-code uninstall** → does not need the artifact

**Rationale:** Avoids downloading a file just to uninstall it when the uninstall command is self-contained. Adds minimal complexity (a `string.Contains` check).

---

## Decision 9: Skip Post-Uninstall Verification for PoC
**Question:** Should the agent verify that a removed package is actually gone after running the uninstaller?

**Decision:** No. The `UninstallPackage` step does not include a verification step after uninstall. Success is determined solely by the process exit code.

**Rationale:** Trust the uninstaller. Post-uninstall verification (e.g., checking that files/registry entries are gone) adds complexity and is not required for the PoC. Can be added later if needed.

---

## Decision 10: Two-Phase Execution — Uninstall First, Then Install
**Question:** What execution order should the agent use for the computed diff actions?

**Decision:** The pipeline executes in two phases:
1. **Phase 1 — Uninstall:** All `removed` packages, in reverse `PackageIndex` order (highest index first, to avoid index-shifting issues if we ever track by index)
2. **Phase 2 — Install:** All `added` and `changed` packages, in normal `PackageIndex` order

Unchanged packages are skipped entirely.

**Rationale:** Prevents file locks and conflicts where a removed package and an added package might share resources. If an uninstall fails, the pipeline halts before any new packages are touched.

---

## Decision 11: Fail-Fast on Uninstall Errors
**Question:** If a removed package's uninstaller fails (non-zero exit code), should the pipeline continue with remaining packages or halt?

**Decision:** Halt immediately and send `Fail`. The pipeline does not continue.

**Rationale:** Deterministic behavior for the PoC. If an uninstall fails, something is wrong and the operator should investigate. We can revisit `continue_on_error` as a future enhancement.

---

## Decision 12: Step Status Indexing for Uninstall Steps
**Question:** `StepStatusPayload` has `PackageIndex`, but removed packages come from `CurrentPackages` (previous revision), which may have different `PackageIndex` values than the target revision. What index should be reported for uninstall steps?

**Decision:** Use the `PackageIndex` from `CurrentPackages` (the previous revision's index).

**Rationale:** The index is metadata for human readability in the timeline. Using the previous revision's index is honest — it tells you where that package was positioned in the previous revision. No contract changes needed.

---

## Decision 13: `PipelineExecutor` Owns Diff Computation
**Question:** Should the agent compute the diff before calling `PipelineExecutor`, or inside it?

**Decision:** Inside `PipelineExecutor`. The executor receives `CurrentPackages` and target `Packages` from the payload, computes the diff at the start of execution, and then iterates over the resulting actions.

**Rationale:** The `PipelineExecutor` is the right place to own orchestration logic. It already has the full payload. Centralizing the diff keeps the runtime service thin and the pipeline logic in one place.

**`PipelineContext` extension:** Add `CurrentPackages` to the context so the executor can access both states.

---

## Decision 14: Fallback to Install When No Current State Exists
**Question:** What happens when a run is created with `mode: "update"` or `mode: "rollback"`, but the node has **no previous successful installation** of that workload (`CurrentRevisionId` is null)?

**Decision:** The agent detects empty `CurrentPackages` and falls back to treating all target packages as `added` — it performs a full install.

**Rationale:** Self-healing behavior. If an operator mistakenly sends `update` to a fresh node, it just installs. The orchestrator doesn't need extra validation logic, and the agent is self-healing. The mode label becomes informational rather than a hard gate.

---

## Decision 15: Full `PackageAssignment` for `CurrentPackages`
**Question:** Does `CurrentPackages` need full `InstallAdapterConfig`/`DetectionConfig`, or just minimal metadata (`Name`, `Version`, `PackageId`) for diffing?

**Decision:** Full `PackageAssignment`. The orchestrator builds `CurrentPackages` by looking up the current revision's `WorkloadPackageEntity` records and their corresponding `PackageEntity` records, constructing complete `InstallAdapterConfig` (including `UninstallArgs`) and `DetectionConfig`.

**Rationale:** Reuses the exact same build logic as target `Packages` — just a different revision lookup. No new APIs. No hardcoded defaults. The agent receives everything it needs in one message.

**Note:** The current revision's `PackageEntity` might have been updated since the last run (e.g., `UninstallArgs` was added later). For the PoC, we use whatever is in `PackageEntity` at run creation time. This is acceptable because the orchestrator is the source of truth.

---

## Decision 16: `UninstallArgs` Source — Manifest + Pattern Registry
**Question:** `PackageEntity` currently has no `UninstallArgs` column. Where does `UninstallArgs` come from?

**Decision:** Two sources:
1. **Admin-provided manifest** — primary source when artifact is uploaded with a manifest containing `InstallAdapter.UninstallArgs`
2. **Pattern registry** — fallback for placeholder packages created during bulk import

**Pattern registry defaults (PoC):**
| Package Pattern | Installer Type | UninstallArgs |
|---|---|---|
| `git-*` | `exe` (Inno) | `/VERYSILENT /UNINSTALL` |
| `nodejs-*` | `msi` | `/x "{artifactPath}" /qn /norestart` |
| `7zip-*` | `exe` (NSIS) | `/S _?="{artifactPath}"` |
| `python-*` | `exe` (WiX) | `/quiet /uninstall` |
| `dotnet-runtime-*` | `exe` | `/quiet /uninstall /norestart` |
| `test-agent-*` | `exe` | `/S _?="{artifactPath}"` |

**Rationale:** Manifest is the production workflow. Pattern registry is a pragmatic fallback for placeholder packages created during bulk import. For MSI, the uninstall command is relatively standard. For EXE, it varies by installer family, so pattern matching is needed.

---

## Decision 17: Test Workload Modification for Removal Demo
**Question:** The current test workloads (`workloads-older.json` and `workloads-newer.json`) have identical package sets — only versions differ. How do we exercise the `removed` category in the demo?

**Decision:** Remove `test-agent` from `workloads-newer.json`'s "Runtime Environment" workload.

```json
// workloads-newer.json - Runtime Environment (v2.0.0)
"packages": [
  "dotnet-runtime-10.0.7"
]
```

**Demo scenarios:**
- **Update (older → newer):** `test-agent` = removed, `dotnet-runtime` = changed, others = changed
- **Rollback (newer → older):** `test-agent` = added, `dotnet-runtime` = changed, others = changed

The other packages (git, nodejs, python, 7zip) are changed in both directions.

**Rationale:** One pair of files covers all three scenarios (install, update with removal, rollback with addition). The demo narrative is cleaner.

---

## Decision 18: Per-Package `UpgradeBehavior` for Changed Packages
**Question:** Decision 6 assumed all installers handle in-place upgrades ("just run the new installer"). Python 3.13→3.14 proved this wrong: the WiX installer installs side-by-side, leaving the old version present, causing PostInstallVerify to detect the wrong version. How should the system handle packages whose installers don't perform in-place upgrades?

**Decision:** Add a per-package `UpgradeBehavior` enum to `InstallAdapterConfig` with three values:

**Category 1 — Self-upgrading installers (`InPlace`):**
The installer itself handles removing the old version safely while preserving data. Running an uninstall first would be destructive — you'd lose databases, configs, user data. The correct behavior is to just run the new installer and let it handle everything.

Examples: DBeaver (NSIS replaces existing install), SQL Server (setup.exe performs in-place upgrade preserving databases), Visual Studio (installer upgrades in-place preserving settings), Git (Inno Setup detects and replaces), 7-Zip (NSIS overwrites existing), Notepad++ (NSIS overwrites existing).

**Category 2 — Side-by-side installers (`UninstallFirst`):**
The installer doesn't remove the old version — it installs alongside it. If you just run the new installer, the old version remains on disk and in the registry/PATH, causing PostInstallVerify to detect the wrong version. The correct behavior is to explicitly uninstall the old version first, then install the new version.

Examples: Python (WiX Burn bundle installs side-by-side — 3.13 persists alongside 3.14), .NET Runtime (side-by-side installs, old version persists), Node.js major versions (side-by-side in different directories), any WiX bundle without UpgradeTable.

**Category 3 — Custom migration required (`SideBySide`):**
Neither simple approach works. The app has state that needs transformation — export config from old version, install new version alongside, migrate/transform data, then clean up old version. This is handled by `postInitSteps` running custom migration scripts while both versions coexist.

Examples: SQL Server named instance migration (both instances must run during cutover), web server role swaps (old serves traffic while new warms up), apps requiring registry/file transformation between versions.

| Value | Behavior | Use Case |
|---|---|---|
| `InPlace` (**default**) | Run new installer only; trust it to upgrade in-place | Self-upgrading installers: DBeaver, SQL Server, Visual Studio, Git, 7-Zip |
| `UninstallFirst` | Uninstall old version → install new version | Side-by-side installers: Python, .NET Runtime, Node.js LTS → new major |
| `SideBySide` | Install new alongside old; cleanup via `postInitSteps` | Custom migration: SQL Server named instance swaps, config transformation between versions |

**Contract changes:**

1. **`InstallAdapterConfig`** (shared contract, agent + orchestrator):
```csharp
public string UpgradeBehavior { get; set; } = "InPlace";  // "InPlace" | "UninstallFirst" | "SideBySide"
```

2. **`PackageEntity`** (orchestrator database):
```csharp
public string UpgradeBehavior { get; set; } = "InPlace";
```

3. **PipelineExecutor** (agent): When `DiffResult.Changed` contains a package with `UpgradeBehavior == "UninstallFirst"`, the executor adds an `UninstallPackage` step for the **old** package before running `AcquireArtifact → InstallOrUpgrade → PostInstallVerify` for the new one. The uninstall uses `CurrentPackages`'s `InstallAdapter` config for that package — the old version's command and `UninstallArgs`.

**Execution order for changed packages:**

| UpgradeBehavior | Phase 1 (Uninstall) | Phase 2 (Install) |
|---|---|---|
| `InPlace` | — | Acquire → InstallOrUpgrade → PostInstallVerify |
| `UninstallFirst` | UninstallPackage (old config) | Acquire → PostInstallVerify (skip detection of removal) → InstallOrUpgrade → PostInstallVerify |
| `SideBySide` | — | Acquire → InstallOrUpgrade → PostInitSteps (cleanup) → PostInstallVerify |

**Rationale:** This mirrors the Intune Supersedence model where administrators explicitly choose per-relationship whether an upgrade is "in-place update" or "uninstall-then-replace." The `UpgradeBehavior` is a **package-level** property — intrinsic to how the package's installer works, not workload-level. Python always needs `UninstallFirst` regardless of which workload references it. SQL Server always needs `InPlace` because uninstalling first would destroy databases. This avoids repeating the same setting across workload definitions.

The default `InPlace` preserves backward compatibility: existing packages continue to work as before (just run the new installer). Workload authors only need to explicitly set `UninstallFirst` or `SideBySide` for packages whose installers don't clean up old versions. Setting `InPlace` on a package like SQL Server is critical — running `UninstallFirst` on a stateful app would destroy data (databases, connection strings, configs) that the in-place upgrade is designed to preserve.

**Industry precedent:**
- **MSI Major Upgrades**: The new package's UpgradeTable declares how to find/remove old versions — the package itself defines upgrade behavior
- **Intune Supersedence**: Admin explicitly chooses "App Update" (in-place) vs "App Replacement" (uninstall-first) per supersedence relationship
- **Chocolatey**: Always uninstalls-then-installs using the OLD package's uninstall script — package-authored upgrade logic

**Failure mode for `UninstallFirst`:** If uninstall fails, the pipeline halts (consistent with Decision 11: fail-fast on uninstall errors). The node remains in a partially-migrated state: old version uninstalled but new version not yet installed. This is recoverable — the operator can retry the update, and since the old version is now gone, the next run will treat it as a fresh `added` package rather than a `changed` one.

- **Decision 19:** For `SideBySide` packages, should PostInstallVerify target the new version's detection path even when old version remains temporarily? Or should `SideBySide` packages skip PostInstallVerify and rely on postInitSteps for validation?
- **Decision 20:** Should `UpgradeBehavior` be versioned (per `PackageEntity` version) or truly intrinsic to the package name? Current assumption: intrinsic to package name — Python always side-by-side regardless of version.
