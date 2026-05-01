# Implementation Plan: SideBySide Removal, Script Refactor, and Detection Simplification

## 1. SideBySide Removal from System

### 1.1 Code Changes (Completed)
- **`apps/orchestrator/backend/Validation/UpgradeBehaviorValidator.cs`**
  - Removed `SideBySide` from `AllowedValues`.
  - Updated error messages to list only `InPlace` and `UninstallFirst`.
- **`apps/orchestrator/backend/Validation/UpgradeBehaviorValidatorTests.cs`**
  - Removed all `SideBySide` test cases.
  - Adjusted assertion counts from 3 to 2 valid values.
- **`apps/agent/backend/Pipeline/PipelineExecutor.cs`**
  - Removed the `SideBySide` bare-command warning block (lines 270–281).

### 1.2 Documentation Changes (Completed)
- **`docs/adr/0005-package-upgrade-behavior.md`**
  - Rewrote to document only `InPlace` and `UninstallFirst`.
  - Added a note that `SideBySide` was previously included but removed.
- **`docs/adr/0006-side-by-side-verification.md`**
  - Marked as **Superseded** with historical context.
  - Explains why `SideBySide` was removed and what to use instead.
- **`docs/prd-package-upgrade-behavior.md`**
  - Removed `SideBySide` from the solution description, user stories, interfaces, testing decisions, and out-of-scope sections.
  - Strikethrough user stories 9 and 18 explicitly document the removal.
- **`docs/decisions/update-mode-deep-dive.md`**
  - Removed the `SideBySide` row from the `UpgradeBehavior` table.
  - Removed Example 4 (SQL Server side-by-side migration).
  - Removed the `SideBySide — PostInstallVerify Must Use Explicit Detection Path` subsection.
  - Updated the summary decision tree to remove the `SideBySide` branch.
- **`docs/decisions/workload-differential-update-rollback.md`**
  - Removed `SideBySide` from the `UpgradeBehavior` enum list.
  - Removed Category 3 (`SideBySide`) entirely, including its examples and table row.
  - Removed Decisions 19 and 20, which were specific to `SideBySide` post-install verification and versioning.
- **`docs/adr/0007-manifest-driven-upgrade-configuration.md`**
  - Updated reference from "side-by-side bug" to "old-version-persistence bug."

### 1.3 Test Workload Changes (Completed)
- **`scripts/download-amazing-workload-artifacts.ps1`**
  - Changed Python `upgradeBehavior` from `SideBySide` to `UninstallFirst` (Python installer leaves old versions on PATH, causing PostInstallVerify failures).
  - Added SQL Server Express 2019 and 2025 as a third package pair to test `UninstallFirst` behavior with a high-risk, reboot-required installer.
  - SQL Server manifests use `upgradeBehavior = "UninstallFirst"`, `expectedExitCodes = @(0, 3010)`, `timeoutSeconds = 600`, and `riskLevel = "high"`.

### 1.4 Verification
- [x] Grep confirms no `SideBySide` references remain in `apps/`, `scripts/`, or `test-artifacts/`.
- [ ] Run `UpgradeBehaviorValidator` unit tests. **BLOCKED** — .NET 10 runtime is not installed on this machine (only .NET 8.0.25 available). Tests build successfully but the test host cannot launch.
- [ ] Run agent pipeline tests to ensure `InPlace` / `UninstallFirst` paths still pass. **BLOCKED** — same .NET 10 runtime issue.

---

## 2. Refactor `download-amazing-workload-artifacts.ps1`

### 2.1 Completed Changes
- **Direct output**: Manifests are now emitted directly to `test-artifacts/` instead of a temporary `manifests/` folder.
- **Installer caching**: Downloads are cached in `.artifact-cache/`. Re-runs skip re-downloading if the file already exists.
- **Diff-aware zipping**: The final zip archives (`amazing-workload-artifacts-v1.zip` and `v2.zip`) are rebuilt only when:
  1. The zip does not exist, OR
  2. Any manifest JSON is newer than the zip, OR
  3. Any cached installer is newer than the zip.
- **Content-aware manifest writing**: Added `Set-ContentIfChanged` helper so manifest files are only rewritten when their JSON content actually changes. This prevents timestamp churn from falsely triggering zip rebuilds on every script run.

### 2.2 Verification
- [x] Run the script on a clean workspace and confirm `test-artifacts/` is populated. **PASS** — 6 manifests (DBeaver, Python, SQL Server × 2 versions) and 2 zips created.
- [x] Re-run the script and confirm downloads are skipped and the zip is not rebuilt. **PASS** — Downloads skipped, zips correctly skipped with "Skipped ... (up-to-date)" message.
- [x] Modify a manifest (via script variable change), re-run, and confirm the zip is rebuilt. **PASS** — Only the affected zip (v1) was rebuilt; v2 remained up-to-date.

---

## 3. Detection Field Recommendation

### 3.1 Research Findings
- `detection` is a first-class object in the manifest schema (`ResolvedManifest` / `ArtifactManifest`) with fields: `type`, `path`, `expectedVersion`.
- The orchestrator backend applies defaults in `ArtifactIngestService.ResolveDetection()` and persists them into `PackageEntity.DetectionConfigJson`.
- The agent backend uses `PackageDetector` (supports `file`, `registry` [stub], `version_manifest`) during `PreCheckProbe` and `PostInstallVerify`.
- Known issues:
  - Binary name mismatches (`nodejs` → `node`, `python` → `python3`) because manifests use `packageId` instead of the real binary name.
  - Version prefix stripping bugs (`==22.14.0` not normalized).
  - `registry` detection is a non-functional stub.
  - Detection config is historically lost during `BulkImport`.

### 3.2 Recommended Path Forward
**Keep `detection` in manifests, but simplify it aggressively.**

1. **Remove `expectedVersion`** from the manifest schema. Derive it from `manifest.version` (prefix with `==`).
2. **Default `type`** to `version_manifest` and omit it from most manifests.
3. **Allow only an optional `detection.path` override** when the binary name genuinely differs from `packageId` (e.g., `nodejs` → `node`).
4. **Move well-known binary aliases and common install-directory search logic** into the orchestrator/agent backend, so manifests do not encode platform-specific path knowledge.
5. **Remove `registry` as an allowed manifest type** until the agent implements it.

### 3.3 Trade-offs
| Approach | Pros | Cons |
|---|---|---|
| **Simplify manifests (Recommended)** | Fewer translation bugs, less repetition, manifest-as-source-of-truth for install layout only | Backend must own canonical alias map |
| **Remove detection entirely** | Zero manifest complexity | Requires `PackageRegistryService` (ADR-014) which is not yet implemented; backend would have no way to know custom binary names |
| **Keep as-is** | No immediate code churn | Continues to cause version-prefix bugs, lost config in bulk import, and broken `registry` type |

### 3.4 Implementation Tasks (Future Work)
- [ ] Update manifest schema and frontend form so only `detection.path` is editable (optional).
- [ ] Derive `type` and `expectedVersion` in `ArtifactIngestService` and `WorkloadRunDispatcher`.
- [ ] Harden `PackageDetector` with a centralized alias map.
- [ ] Fix or remove the `registry` detection stub.

---

## 4. Testing Guide Log-Line Discrepancy

### 4.1 Acknowledged Issue
The testing guide described aspirational log lines that do not exist in the actual source code:
- "Diff result: 1 changed, 1 unchanged"
- "UpgradeBehavior=InPlace — skipping Phase 1 uninstall"
- "Phase 2: Installing changed package..."
- "Running installer: ..."

The actual `PipelineExecutor.cs` emits:
- `Pipeline diff computed: Added=..., Removed=..., Changed=..., Unchanged=...`
- No explicit log for skipping Phase 1 uninstall on `InPlace`/`SideBySide` packages.
- The `InstallOrUpgrade` step logs: `Executing install command: FileName=..., Arguments=...`

### 4.2 Recommended Action
- [ ] If a testing guide document exists in a wiki or external repo, update it to match the actual log lines found in `PipelineExecutor.cs`.
- [ ] If no such document exists in this repo, no file changes are required; the discrepancy is noted here for future test authors.

---

## 5. Roll-out Checklist

1. [x] Merge all code changes (validator, tests, pipeline executor).
2. [x] Merge all documentation changes (ADRs, PRD, deep-dive, rollback).
3. [x] Merge script refactor.
4. [ ] Run full test suite:
   - `UpgradeBehaviorValidatorTests` — **BLOCKED** (requires .NET 10 runtime)
   - Agent pipeline integration tests for `InPlace` (DBeaver) and `UninstallFirst` (Python, SQL Server) — **BLOCKED** (requires .NET 10 runtime)
5. [x] Verify `SideBySide` no longer appears in any code, manifest, or generated artifact.
6. [ ] Schedule detection-field simplification as a follow-up PR.
