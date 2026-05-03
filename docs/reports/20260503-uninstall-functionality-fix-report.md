# Uninstall Functionality — Comprehensive Fix Report

**Date:** 2026-05-03  
**Status:** Root cause identified — fix plan below  
**Severity:** High — uninstall pipeline completes with "success" but performs zero work  

---

## 1. Executive Summary

The uninstall pipeline reports success but silently skips every package with the warning:
> "No uninstall command configured or found in registry for {PackageName}. Skipping."

**Root cause:** `UninstallCommand` is never populated in the `PackageEntity` database table during workload import, so the orchestrator sends empty `UninstallCommand` in the dispatch payload, and the agent has nothing to execute.

Three interrelated bugs were found across the orchestrator codebase. The fix plan targets one primary bug (missing field mapping) and two secondary bugs (existing-entity-update and manifest-update guards).

---

## 2. Root Cause Analysis

### 2.1 Data Flow Diagram

```
┌──────────────────┐     ┌───────────────────────┐     ┌─────────────────┐     ┌──────────────┐
│ Artifact Manifest │     │ Bulk Import Controller │     │ PackageEntity     │     │ Dispatcher   │
│ (dist/artifacts/) │     │ WorkloadsController   │     │ (DB Table)        │     │ BuildPayload │
│                   │     │ .BulkImport()          │     │                   │     │              │
│ installAdapter:   │     │                        │     │ UninstallCommand  │     │ InstallAdapter│
│   uninstallCommand│ ──> │ ResolvePlaceholderAdapter│ ──> │  = "" (BUG!)     │ ──> │  .UninstallCmd│
│   uninstallArgs   │     │   (reads manifest)     │     │ UninstallArgs     │     │  = ""        │
│                   │     │                        │     │  = "" (BUG 2!)    │     │              │
│ ✅ HAS values     │     │ AdapterResolution      │     │                   │     │              │
│  for dbeaver,     │     │   .UninstallCommand    │     │                   │     │              │
│  sqlserver        │     │   MISSING! (BUG 1)     │     │                   │     │              │
└──────────────────┘     └───────────────────────┘     └─────────────────┘     └──────────────┘
                                                                                      │
                                                                                      ▼
                                                                              ┌──────────────┐
                                                                              │ Agent        │
                                                                              │ PipelineExec │
                                                                              │              │
                                                                              │ "No uninstall │
                                                                              │  command..." │
                                                                              └──────────────┘
```

### 2.2 Manifest Files (have the data)

| Package | `uninstallCommand` | `uninstallArgs` |
|---------|-------------------|-----------------|
| dbeaver | `%ProgramFiles%\DBeaver\uninstall.exe` | `/S` |
| sqlserver | `%ProgramFiles%\Microsoft SQL Server\150\Setup Bootstrap\SQL2019\setup.exe` | `/ACTION=Uninstall /FEATURES=SQLEngine /INSTANCENAME=SQLEXPRESS /Q /IACCEPTSQLSERVERLICENSETERMS` |
| python | "" (empty — Python has no uninstall binary) | "" |

---

## 3. Detailed Findings

### BUG 1 (Primary): `AdapterResolution` class missing `UninstallCommand` field

**File:** `apps/orchestrator/backend/Controllers/WorkloadsController.cs`  
**Lines:** 699–709, 714–758, 1014–1028

The `AdapterResolution` private class at line 699 defines the fields extracted from a resolved manifest:

```csharp
// Line 699-709
private sealed class AdapterResolution
{
    public string InstallType { get; set; } = "exe";
    public string SourcePath { get; set; } = "{artifactPath}";
    public string InstallArgs { get; set; } = "";
    public string UninstallArgs { get; set; } = "";
    public string UpgradeBehavior { get; set; } = "InPlace";
    public string ExpectedExitCodesJson { get; set; } = "[0]";
    public int TimeoutSeconds { get; set; } = 300;
    public string? DetectionConfigJson { get; set; }
    // ⚠ MISSING: public string UninstallCommand { get; set; } = "";
}
```

The `ResolvePlaceholderAdapter` method (line 714) reads a stored resolved manifest and maps its fields to `AdapterResolution`. It reads `UninstallArgs` at line 742:

```csharp
// Line 742
UninstallArgs = adapter.UninstallArgs ?? "",
// ⚠ MISSING: UninstallCommand = adapter.UninstallCommand ?? "",
```

But it does **NOT** read `adapter.UninstallCommand`.

Then at the PackageEntity creation block (line 1014–1028), only `UninstallArgs` is set:

```csharp
// Line 1022
UninstallArgs = adapter.UninstallArgs,
// ⚠ MISSING: UninstallCommand = adapter.UninstallCommand,
```

**Impact:** Even though the stored manifest has `installAdapter.uninstallCommand` populated (verified in `dist/artifacts/*.manifest.json`), the field is silently dropped during package creation. Every package created via the Bulk Import path ends up with `UninstallCommand = ""` in the database.

---

### BUG 2: Existing `PackageEntity` is never updated with new uninstall fields

**Affected locations:**
- `WorkloadsController.cs` BulkImport, lines 1041–1052
- `WorkloadImportService.cs` EnsurePackageEntitiesAsync, lines 55–58

Both paths check if a `PackageEntity` already exists (by name+version or by deterministic GUID) and return early without updating any fields:

```csharp
// WorkloadsController.cs line 1007-1009
var existingPackage = await _db.Packages
    .Where(p => p.PackageId == deterministicId)
    .FirstOrDefaultAsync();

if (existingPackage is null)
{
    // ... creates new entity with fields populated
}
else
{
    // ⚠ Just links to existing — does NOT update UninstallCommand/UninstallArgs
    revision.Packages.Add(new WorkloadPackageEntity { ... });
}
```

```csharp
// WorkloadImportService.cs line 52-58
var existing = await _db.Packages
    .SingleOrDefaultAsync(p => p.Name == manifest.PackageId && p.Version == manifest.Version);

if (existing is not null)
{
    return [existing.PackageId]; // ⚠ No update
}
```

**Impact:** Once a package is in the database with empty uninstall fields (created before the `AddUninstallCommandToPackageEntity` migration of 2026-05-02, or via a manifest that lacked the fields), re-importing the workload will **never** populate the missing values. The only workaround is to manually delete the database row and re-import.

---

### BUG 3: `ArtifactStoreService.SaveResolvedManifestAsync` blocks manifest updates

**File:** `apps/orchestrator/backend/Services/ArtifactStoreService.cs`, line 110–121

```csharp
public async Task<bool> SaveResolvedManifestAsync(string packageId, string version, ...)
{
    if (ExistsAny(packageId, version))
    {
        return false; // ⚠ Returns false without updating
    }
    // ... writes manifest
}
```

**Impact:** If a resolved manifest file was saved before the `uninstallCommand` field was added to the manifest JSON, re-uploading the artifact with an updated manifest will silently fail (`return false`). The old manifest without uninstall fields persists. This compounds with Bug 1 since `ResolvePlaceholderAdapter` reads from the persisted manifest.

---

### BUG 4: `BuildPendingPackageDto` missing `UninstallCommand` in WorkloadRunsController

**File:** `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`, lines 1143–1154

The `BuildPendingPackageDto` method (used for API responses that preview pending runs) sets `UninstallArgs` but omits `UninstallCommand`:

```csharp
// Line 1143-1154
InstallAdapter = new InstallAdapterConfig
{
    Type = installType,
    Command = pkg?.SourcePath ?? "{artifactPath}",
    Arguments = installArgs,
    UninstallArgs = pkg?.UninstallArgs ?? "",
    // ⚠ MISSING: UninstallCommand = pkg?.UninstallCommand ?? "",
    UpgradeBehavior = ...,
    ExpectedExitCodes = ...,
    TimeoutSeconds = ...
},
```

**Impact:** The UI preview (GET `/api/workload-runs/{id}` pending run details) shows empty `UninstallCommand` even if the DB has it. This does not affect the actual dispatch pipeline, but could confuse operators inspecting pending runs.

**Note:** This is the same pattern as Bug 1 — the `UninstallCommand` column was added later via migration (`20260502102225`) and this code path was not updated to include it.

### BUG 5 (Informational): Python has no uninstall command and registry resolution won't match

**File:** `dist/artifacts/python-3.13.3-amd64.manifest.json` (and python-3.14.4)

Python's manifest has both `uninstallCommand` and `uninstallArgs` set to empty strings. The registry fallback (`ResolveRegistryUninstaller("python")`) does an exact `DisplayName` match, but Python registers as `"Python 3.13.3 (64-bit)"` or similar — not `"python"`. This means **Python uninstall will always be skipped** with no way to work.

### BUG 6 (Minor): Pre-check runs for packages that should be skipped in uninstall mode

**File:** `apps/agent/backend/Pipeline/PipelineExecutor.cs`, lines 42–46

The PreCheckProbe phase gathers `targetPackages.Concat(baseDiff.Removed)`. In uninstall mode, `baseDiff.Removed` is already a subset of `targetPackages` (since the diff engine merges changed into removed). The `Concat` + `GroupBy` dedup is correct, but wasteful. This is cosmetic — the probe results are used to mark already-gone packages as `UninstallSkippedAlreadyGone`, which is correct behavior.

**Verdict:** Not a bug, but worth noting that the pre-check logic influenced the diff to show `Unchanged=3` (when `currentPackages` are populated, they match the target, and pre-check results confirm they're present).

---

## 4. Fix Plan

### 4.1 Fix BUG 1: Populate `UninstallCommand` during Bulk Import

**Priority:** P0 — this is the primary blocker

**File:** `apps/orchestrator/backend/Controllers/WorkloadsController.cs`

**Changes required:**

1. **Add `UninstallCommand` field to `AdapterResolution`** (line ~704):
   ```csharp
   public string UninstallCommand { get; set; } = "";
   ```

2. **Read `UninstallCommand` in `ResolvePlaceholderAdapter`** (line ~742):
   ```csharp
   UninstallCommand = adapter.UninstallCommand ?? "",
   ```

3. **Set `UninstallCommand` on new `PackageEntity`** (line ~1022):
   ```csharp
   UninstallCommand = adapter.UninstallCommand,
   ```

### 4.2 Fix BUG 2: Update existing PackageEntity when re-imported

**Priority:** P1 — needed if DB already contains stale records

**File:** `apps/orchestrator/backend/Controllers/WorkloadsController.cs` (line 1041)

```csharp
// In the else branch (existing package found), add:
else
{
    // Update uninstall fields if they were previously empty
    if (string.IsNullOrEmpty(existingPackage.UninstallCommand) && 
        !string.IsNullOrEmpty(adapter.UninstallCommand))
    {
        existingPackage.UninstallCommand = adapter.UninstallCommand;
        existingPackage.UninstallArgs = adapter.UninstallArgs;
    }
    
    revision.Packages.Add(new WorkloadPackageEntity { ... });
}
```

**File:** `apps/orchestrator/backend/Services/WorkloadImportService.cs` (line 55)

```csharp
if (existing is not null)
{
    // Update uninstall fields from the fresh manifest
    if (string.IsNullOrEmpty(existing.UninstallCommand))
    {
        existing.UninstallCommand = manifest.InstallAdapter?.UninstallCommand ?? string.Empty;
    }
    if (string.IsNullOrEmpty(existing.UninstallArgs))
    {
        existing.UninstallArgs = manifest.InstallAdapter?.UninstallArgs ?? string.Empty;
    }
    await _db.SaveChangesAsync();
    return [existing.PackageId];
}
```

### 4.3 Fix BUG 3: Allow manifest overwrites in ArtifactStoreService

**Priority:** P2 — needed for re-ingesting artifacts with updated manifests

**File:** `apps/orchestrator/backend/Services/ArtifactStoreService.cs` (line 112)

Option A — Always overwrite:
```csharp
if (ExistsAny(packageId, version))
{
    // Overwrite: delete old manifest before writing new one
    var manifestPath = GetManifestPath(packageId, version);
    if (File.Exists(manifestPath))
        File.Delete(manifestPath);
}
```

Option B — Add an overwrite parameter:
```csharp
public async Task<bool> SaveResolvedManifestAsync(string packageId, string version, 
    string manifestJson, bool overwrite = false, CancellationToken cancellationToken = default)
```

### 4.4 Fix BUG 4: Add `UninstallCommand` to `BuildPendingPackageDto`

**Priority:** P1 — affects UI consistency for pending run previews

**File:** `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs` (line 1148)

```csharp
// After line 1148, add:
UninstallCommand = pkg?.UninstallCommand ?? "",
```

This matches the pattern already used in `WorkloadRunDispatcher.BuildPackageAssignments()` at line 222.

### 4.5 Fix BUG 5: Python uninstall strategy

**Priority:** P2 — Python currently cannot be uninstalled

**Options:**
1. **Add explicit manifest fields** — Set `uninstallCommand` to `powershell` and use `Get-Package` from PackageManagement module
2. **Improve registry resolution** — Expand `ResolveRegistryUninstaller` to support substring/fuzzy matching (larger scope)
3. **Accept as known limitation** — Document that Python requires manual uninstall via Windows Add/Remove Programs

**Recommendation:** Option 3 for now (document limitation). The critical fix is making dbeaver and sqlserver uninstall work.

---

## 5. Acceptance Criteria

| # | Criterion | Verification |
|---|-----------|-------------|
| AC-1 | `UninstallCommand` flows from manifest JSON → `PackageEntity` DB row → dispatcher payload → agent pipeline | Query DB: `SELECT Name, UninstallCommand FROM Packages WHERE Name IN ('dbeaver','sqlserver')` — must return non-empty values |
| AC-2 | Explicit uninstall mode executes `UninstallCommand` for each package (dbeaver, sqlserver) | Agent logs show `UninstallPackage` step with actual command, not `UninstallSkippedNoCommand` |
| AC-3 | Python (no uninstall command) gracefully skips uninstall (or succeeds via registry resolution) | Agent logs show `exit_code_0` from `python -m pip uninstall...` or `UninstallSkippedNoCommand` with appropriate message |
| AC-4 | Re-importing a workload updates stale `UninstallCommand` on existing packages | After re-import, same DB query returns updated values |
| AC-5 | Workload-update removal path (diff-based) also uses correct `UninstallCommand` | Remove a package from workload v2, run update; removed package uses its `UninstallCommand` from DB |
| AC-6 | Pipeline reports correct success/failure (not false-positive "success" when nothing happened) | Verify `UninstallSkippedNoCommand` step transitions run to `warning` or `failed` if all packages were skipped |

---

## 6. Affected Files Summary

| File | Issue | Fix Type |
|------|-------|----------|
| `apps/orchestrator/backend/Controllers/WorkloadsController.cs` | `AdapterResolution` missing `UninstallCommand`; package creation missing `UninstallCommand` assignment; existing package skip without update | Add field, read from manifest, set on entity, update existing |
| `apps/orchestrator/backend/Services/WorkloadImportService.cs` | Early return on existing package skips update of uninstall fields | Add update block before return |
| `apps/orchestrator/backend/Services/ArtifactStoreService.cs` | `SaveResolvedManifestAsync` refuses to overwrite | Allow overwrite or add parameter |
| `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs` | `BuildPendingPackageDto` missing `UninstallCommand` in InstallAdapterConfig | Add line for `UninstallCommand` |
| `dist/artifacts/python-3.13.3-amd64.manifest.json` | Python has empty uninstallCommand/uninstallArgs, registry match fails | Known limitation (document) or add explicit command |
| `apps/agent/backend/Pipeline/PipelineExecutor.cs` | Cannot find uninstall command (symptom, not cause) | No change needed — fixed by orchestrator-side data |

---

## 7. Additional Consideration: Python Uninstall

Python's manifest has both `uninstallCommand` and `uninstallArgs` empty. There is no dedicated uninstaller binary for Python on Windows. Options:

1. **Add a manifest entry** for `python` with `uninstallCommand: "pip"` and `uninstallArgs: "uninstall -y <package>"` — not useful since pip handles Python packages, not Python itself
2. **Add Windows uninstall string:** `%LocalAppData%\Programs\Python\Python313\python.exe` with args to trigger the uninstall — the actual uninstaller is registered in Windows Add/Remove Programs
3. **Rely on registry resolution** (`UninstallPackage.ResolveRegistryUninstaller`) which already searches the Windows uninstall registry for a `DisplayName` match — this is the intended fallback

**Recommendation:** Add explicit `uninstallCommand` and `uninstallArgs` to `python-3.13.3-amd64.manifest.json` (and `python-3.14.4-amd64.manifest.json`). The Python installer registers its uninstaller in the registry, which can be used. For completeness:

```json
"uninstallCommand": "%LocalAppData%\\Programs\\Python\\Python313\\python.exe",
"uninstallArgs": "-m pip uninstall -y pip setuptools wheel && %LocalAppData%\\Programs\\Python\\Python313\\python.exe -c \"import shutil, sys; shutil.rmtree(sys.prefix, ignore_errors=True)\""
```

Alternatively, keep it empty and let registry resolution handle it. The current registry resolution is the correct fallback for packages with no explicit uninstall command.

---

## 8. Verification Report

### Pre-Fix State

| Check | Expected | Actual | Status |
|-------|----------|--------|--------|
| DB `dbeaver.UninstallCommand` | `%ProgramFiles%\DBeaver\uninstall.exe` | `""` (empty) | FAIL |
| DB `sqlserver.UninstallCommand` | `%ProgramFiles%\Microsoft SQL Server\150\...` | `""` (empty) | FAIL |
| DB `python.UninstallCommand` | `""` (empty) | `""` (empty) | PASS (expected empty) |
| Agent receives payload with UninstallCommand | Non-empty for dbeaver/sqlserver | Empty for all | FAIL |
| Uninstall pipeline actually runs commands | Executes uninstall for dbeaver/sqlserver | Skips all 3 | FAIL |

### Post-Fix Verification (to be run after implementation)

```bash
# 1. Verify DB records
sqlite3 dist/deployment-poc.db "SELECT Name, UninstallCommand, UninstallArgs FROM Packages WHERE Name IN ('dbeaver','sqlserver','python')"

# 2. Re-import workload and verify updated fields
# (Via API: POST /api/workloads/bulk-import with workload JSON)

# 3. Run explicit uninstall and check agent logs
# Expected: "Step UninstallPackage: PackageIndex=..." followed by successful exit codes
# NOT: "No uninstall command configured or found in registry for {PackageName}. Skipping."

# 4. Run workload update (remove a package) and verify diff-based uninstall
# Remove sqlserver from v2 → run update → agent should uninstall sqlserver with correct command

# 5. Unit test the fix
dotnet test tests/agent/unit/UninstallPackageTests.cs --filter "uninstall"
dotnet test tests/agent/integration/PipelineExecutorTests.cs --filter "uninstall"
```
