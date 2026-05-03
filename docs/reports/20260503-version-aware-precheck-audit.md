# Audit Report: Version-Aware Pre-Check System

**Date:** 2026-05-03
**Scope:** Agent pre-check (`PackageDetector`) → diff computation → pipeline execution
**Status:** CRITICAL — version-blind detection causes updates to be silently skipped

---

## Executive Summary

The update pipeline has a **systemic version-awareness gap**. `PackageDetector` only checks whether a package *exists* (file/registry/PATH), not whether the *correct version* is installed. When updating from v1 to v2:

1. The diff engine correctly classifies packages as `changed` (based on orchestrator state)
2. The pre-check probes the target package's detection config against the node
3. `PackageDetector` finds the old v1 installation → returns `AlreadySatisfied`
4. `PipelineExecutor` sees `AlreadySatisfied` and **skips all install steps**
5. The old version remains on disk; no `InstallOrUpgrade` timeline entry is created
6. The orchestrator fix (commit `43e0757`) correctly does NOT update `CurrentRevisionId`
7. **Result:** The run reports success, but nothing was actually updated

This is a **design-level bug**, not a logic error. The detection system was built for presence/absence, not version verification.

---

## 1. End-to-End Code Path Analysis

### 1.1 Orchestrator Dispatch (`WorkloadRunDispatcher.cs`)

```
Line 60:   Guid? currentRevisionId = nodeState?.CurrentRevisionId;
Line 75-91: Build currentPackages from currentRevisionId's packages
Line 93-110: AssignRunPayload { CurrentPackages = v1, Packages = v2 }
Line 237-256: BuildDetectionConfig() → { Type = "version_manifest", Path = pkg.Name }
```

**Critical finding:** `BuildDetectionConfig` only sends `Type` and `Path`. It does **not** send the expected version. The `PackageEntity.Version` field ("3.14.4", "26.0.3") is available in the database but is **not included** in the `DetectionConfig` sent to the agent.

### 1.2 First Diff Computation (`PipelineExecutor.cs`, line 34)

```csharp
var baseDiff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, null, context.Payload.Mode);
```

- `currentByName` = v1 packages (Python 3.13.3, DBeaver 24.3.0)
- `targetByName` = v2 packages (Python 3.14.4, DBeaver 26.0.3)
- `changed` = both packages (because `c.Version != p.Version`)
- **Result:** `Changed=2`, `Unchanged=0` ✅

### 1.3 Pre-Check Phase (`PipelineExecutor.cs`, lines 38-62)

```csharp
var packagesToProbe = targetPackages
    .Concat(baseDiff.Removed)
    .GroupBy(p => p.Name)
    .Select(g => g.First())
    .ToList();

foreach (var package in packagesToProbe)
{
    var probeResult = await PreCheckProbe.ExecuteAsync(package.Detection, stepCt);
    preCheckResults[package.Name] = probeResult;
}
```

**Key observation:** The pre-check probes **target packages** (v2) using their `DetectionConfig`. But `DetectionConfig` has no expected version — it only knows:
- `Type`: "version_manifest"
- `Path`: "python" or "dbeaver"

### 1.4 PackageDetector — The Root Failure (`PackageDetector.cs`)

#### File Detection (lines 44-53)
```csharp
if (!File.Exists(config.Path))
    return NotPresent;
return AlreadySatisfied;  // ← Never checks version
```

#### Registry Detection (lines 55-109)
```csharp
foreach (var subKey in uninstallKey.GetSubKeyNames())
{
    var displayName = subKey.GetValue("DisplayName") as string;
    if (string.Equals(displayName, displayNameToMatch, StringComparison.OrdinalIgnoreCase))
        return AlreadySatisfied;  // ← Never reads DisplayVersion
}
```

**Critical finding:** `DisplayVersion` is available in the same registry key but is **never read**. The detector finds "Python" (v3.13.3) when looking for "Python" (v3.14.4) and returns `AlreadySatisfied`.

#### Version Manifest Detection (lines 111-196)
```csharp
// Searches PATH and common directories for the binary
if (File.Exists(fullPath))
    return DetectFileAsync(...);  // ← Delegates to file check (already broken)
```

**Critical finding:** The `version_manifest` type is supposed to check version by running the binary, but it **never does**. It just searches for the file and returns `AlreadySatisfied` if found.

### 1.5 Second Diff Computation (`DiffEngine.cs`)

```csharp
var diff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, preCheckResults, context.Payload.Mode);
```

`ApplyPreCheckOverrides` (lines 54-94) has these safe overrides:
- `unchanged` + `WrongVersion` → `changed`
- `unchanged` + `NotPresent` → `changed`
- `added` + `WrongVersion` → `changed`
- `changed` + `NotPresent` → `added`

**But `AlreadySatisfied` is intentionally ignored** (we removed the unsafe overrides in commit `151bc76`). So `changed` stays as `changed`.

### 1.6 Install Phase — The Skip (`PipelineExecutor.cs`, lines 385-394)

```csharp
if (preCheck.Status == PreCheckStatus.AlreadySatisfied)
{
    _logger.LogInformation("Step PreCheckSkipped: ...");
    await SendStepStatusAsync(..., "PreCheckSkipped", ...);
    context.RecordStep("PreCheckSkipped", ...);
    continue;  // ← SKIPS AcquireArtifact, InstallOrUpgrade, PostInstallVerify
}
```

**This is where the silent failure happens.** The package is in `changed` list, but because pre-check returned `AlreadySatisfied`, the entire install pipeline is bypassed.

### 1.7 Post-Install Verification (`PostInstallVerify.cs`)

```csharp
var result = await PackageDetector.DetectAsync(config, ct);
return result.Status switch
{
    AlreadySatisfied => Success = true,   // ← Would also be wrong
    WrongVersion => Success = false,
    NotPresent => Success = false,
};
```

Even if installation had run, `PostInstallVerify` has the same bug: it would find the old version and report success.

---

## 2. Specific Issues Found

### Issue 1: DetectionConfig Has No ExpectedVersion Field

**Location:** `shared/contracts/Runtime/RunPayloads/DetectionConfig.cs`
```csharp
public sealed class DetectionConfig
{
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    // Missing: ExpectedVersion
}
```

**Impact:** The agent cannot verify whether the detected installation matches the target version. It can only answer "exists / not exists."

### Issue 2: PackageDetector Returns AlreadySatisfied for Wrong Version

**Location:** `apps/agent/backend/Steps/PackageDetector.cs`

All three detection methods (`file`, `registry`, `version_manifest`) return `AlreadySatisfied` when they find *any* matching installation, regardless of version.

**Impact:** Updates are silently skipped when the old version is still present.

### Issue 3: version_manifest Type Doesn't Check Version

**Location:** `apps/agent/backend/Steps/PackageDetector.cs`, lines 111-196

The name implies version checking, but the implementation only does file existence. It should:
1. Find the binary
2. Run `binary --version` or equivalent
3. Parse the output
4. Compare against expected version

**Impact:** The most appropriate detection type for versioned packages is non-functional.

### Issue 4: Orchestrator Doesn't Send Version in DetectionConfig

**Location:** `apps/orchestrator/backend/Services/WorkloadRunDispatcher.cs`, lines 237-256

```csharp
private static DetectionConfig BuildDetectionConfig(PackageEntity? pkg)
{
    if (!string.IsNullOrWhiteSpace(pkg?.DetectionConfigJson))
    {
        return JsonSerializer.Deserialize<DetectionConfig>(pkg.DetectionConfigJson) ?? new();
    }
    return new DetectionConfig { Type = "version_manifest", Path = pkg?.Name ?? "" };
}
```

The `pkg.Version` field is available but never added to `DetectionConfig`.

**Impact:** Even if the agent could check versions, it wouldn't know what version to expect.

### Issue 5: PostInstallVerify Has the Same Bug

**Location:** `apps/agent/backend/Steps/PostInstallVerify.cs`

Uses the same `PackageDetector` with the same `DetectionConfig`. If the old version persists after a failed/skipped install, verification passes incorrectly.

### Issue 6: Design Document Assumes Working Version Detection

**Location:** `docs/decisions/update-mode-deep-dive.md`, line 96:
```
Phase 0   PreCheck probes → both packages detected with wrong version → confirmed "changed"
```

The design assumes pre-checks return `WrongVersion` when the installed version doesn't match. The implementation does not support this.

---

## 3. Design Implications

### 3.1 The Two Sources of Truth Problem

The system currently has **two sources of "current state":**
1. **Orchestrator DB:** `CurrentRevisionId` → `CurrentPackages` (can be stale/wrong)
2. **Agent node reality:** What's actually installed on disk/registry (never fully consulted)

The diff engine uses source #1. The pre-check uses source #2, but in a **version-blind** way. This creates a dangerous gap:
- If the DB is wrong → diff is wrong
- If the DB is right but pre-check is blind → install is skipped

### 3.2 The AlreadySatisfied Ambiguity

`AlreadySatisfied` has two possible meanings:
1. "The correct version is already installed" → skip install ✅
2. "Something with this name is installed" → may need update ⚠️

Currently meaning #2 is treated as meaning #1.

### 3.3 Update vs. Install Mode Confusion

The design doc says:
- `InPlace`: Run new installer directly (installer handles replacement)
- `UninstallFirst`: Uninstall old, then install new

But the pipeline skips `InstallOrUpgrade` entirely based on pre-check. This means:
- `InPlace` updates are skipped when old version exists (because pre-check says "already there")
- `UninstallFirst` updates have the same problem in Phase 1 (uninstall also checks pre-check)

---

## 4. Questions Answered

### Q1: Where do we set ExpectedVersion?

**Answer: The system populates it automatically from the package entity.**

The version already exists in the system at multiple levels:

1. **Manifest level:** `ResolvedManifest.Version` (e.g., "3.14.4", "26.0.3")
2. **Database level:** `PackageEntity.Version` (stored during artifact ingestion)
3. **Payload level:** `PackageAssignment.Version` (sent to agent as target version)

Currently, `BuildDetectionConfig()` in `WorkloadRunDispatcher.cs` only sends `Type` and `Path` from `DetectionConfigJson` (manifest) or defaults. It **never includes version**.

**The fix:** `BuildDetectionConfig` should always set `ExpectedVersion = pkg.Version` on the returned `DetectionConfig`, regardless of whether the manifest JSON includes it. This is a **system-populated field**, not a manifest-author concern. Manifests may optionally include version in detection config, but the orchestrator overrides it with `PackageEntity.Version` to ensure consistency.

**Why not manifest-level?**
- Manifest authors already specify version at the package level (`ResolvedManifest.Version`)
- Requiring it again in `Detection` is redundant and error-prone
- The database (`PackageEntity`) is the single source of truth for the orchestrator

### Q2: Pre-Check Logging Specification

**Requirement:** Clear, structured logging that shows the comparison between incoming and existing packages.

**Proposed log format:**

```
info: [PreCheckProbe] Workload='Amazing Workload', Revision='v2', RunId=02155e38...
info: [PreCheckProbe] Incoming packages to evaluate:
info: [PreCheckProbe]   [0] DBeaver (id:7f196ee1...) → target version: 26.0.3
info: [PreCheckProbe]   [1] Python (id:7aec2fb0...) → target version: 3.14.4
info: [PreCheckProbe] Probing package: DBeaver
info: [PreCheckProbe]   Detection type: registry, path: 'DBeaver'
info: [PreCheckProbe]   Found installation: DisplayName='DBeaver', DisplayVersion='24.3.0.202412091607'
info: [PreCheckProbe]   Expected version: 26.0.3 | Actual version: 24.3.0.202412091607
info: [PreCheckProbe]   Result: WrongVersion (version mismatch)
info: [PreCheckProbe] Probing package: Python
info: [PreCheckProbe]   Detection type: version_manifest, path: 'python'
info: [PreCheckProbe]   Found binary: C:\Users\...\python.exe
info: [PreCheckProbe]   Executing: python --version
info: [PreCheckProbe]   Output: Python 3.13.3
info: [PreCheckProbe]   Expected version: 3.14.4 | Actual version: 3.13.3
info: [PreCheckProbe]   Result: WrongVersion (version mismatch)
info: [PreCheckProbe] === Delta Summary ===
info: [PreCheckProbe] DBeaver      : v24.3.0 → update to v26.0.3  [Action: InstallOrUpgrade]
info: [PreCheckProbe] Python       : v3.13.3 → update to v3.14.4  [Action: InstallOrUpgrade]
info: [PreCheckProbe] Total: 2 to install/upgrade, 0 unchanged, 0 not present
```

**Key logging principles:**
- Every package comparison shows **both sides**: expected vs actual
- When no installation found: `"Found installation: none"`
- When version extraction fails: `"Actual version: unknown (extraction failed: {reason})"`
- Delta summary is a single consolidated view at the end
- Use structured logging (key-value pairs) for machine parsing

### Q3: Registry Version Format Normalization

**Challenge:** Windows `DisplayVersion` is free text with no standard format:
- `"3.14.4"` — clean semver
- `"24.3.0.202412091607"` — DBeaver (major.minor.patch.build)
- `"2.48.1.windows.1"` — Git (has string suffix)
- `"16.0.18025.20160"` — SQL Server / Office (major.minor.build.revision)
- `"2019.150.2000.5"` — SQL Server (marketing year + build)

**Proposed normalization strategy:**

```
Step 1: Extract first numeric-dot sequence from the string
  "2.48.1.windows.1" → "2.48.1"
  "24.3.0.202412091607" → "24.3.0"
  "16.0.18025.20160" → "16.0.18025"

Step 2: Split into segments [major, minor, patch, build...]

Step 3: Compare left-to-right with the expected version
  Expected: "26.0.3"    → [26, 0, 3]
  Actual:   "24.3.0"    → [24, 3, 0]
  Compare:  26 != 24    → mismatch

Step 4: Prefix-matching rule
  If expected has fewer segments than actual, match only the prefix
  Expected: "3.14"      → [3, 14]
  Actual:   "3.14.4150" → [3, 14, 4150]
  Compare:  3==3, 14==14 → match (prefix matches)
```

**Edge cases and solutions:**

| Scenario | Solution |
|---|---|
| SQL Server marketing version ("2019") vs build version ("16.0.xxxx") | Manifest should use build version ("16.0") as `PackageEntity.Version`. The marketing name belongs in `PackageEntity.Name`. |
| Version with string prefix ("Python 3.13.3") | Extract numeric part: `/\d+\.\d+(?:\.\d+)?/` |
| Version with non-numeric suffix ("2.48.1.windows.1") | Extract up to first non-numeric-dot character |
| Missing patch segment ("3.14" vs "3.14.0") | Treat missing as wildcard: "3.14" matches "3.14.x" |
| Completely non-standard format | Log warning, default to `WrongVersion` (safe fallback) |

**For SQL Server SSEI specifically:**
The `DisplayVersion` in registry is typically `"16.0.18025.20160"`. If the manifest/version field says `"16.0"`, prefix matching works. If it says `"2019"`, we need to either:
- Change the manifest to use `"16.0"` (recommended — build versions are canonical)
- Add a version mapping table (marketing name → build prefix)

**Recommendation:** Always use **build version numbers** in manifests (`16.0` not `2019`, `15.0` not `2017`). Build versions are stable and comparable. Marketing names are for UI display only.

---

## 5. Recommended Fix Strategy

### Chosen Approach: Option D (Hybrid)

**Rationale:** Option D combines the precision of version-aware detection with the safety of never skipping updates blindly. It fixes both the skip bug and the verification bug while maintaining efficiency for truly up-to-date packages.

**Implementation steps:**

1. **Add `ExpectedVersion` to `DetectionConfig`**
   ```csharp
   public sealed class DetectionConfig
   {
       public string Type { get; set; } = string.Empty;
       public string Path { get; set; } = string.Empty;
       public string ExpectedVersion { get; set; } = string.Empty;  // NEW
   }
   ```

2. **Orchestrator populates `ExpectedVersion`**
   - In `BuildDetectionConfig`, always set `ExpectedVersion = pkg?.Version ?? ""`
   - This comes from `PackageEntity.Version` (the canonical version from manifest ingestion)

3. **Agent `PackageDetector` becomes version-aware**
   - **Registry detection:** Read `DisplayVersion`, normalize, compare to `ExpectedVersion`
   - **version_manifest detection:** Find binary → run `{binary} --version` → parse output → compare
   - **File detection:** Check if path contains version string; if not, use `version_manifest` fallback or companion `.version` file

4. **Agent `PipelineExecutor` updates skip logic**
   ```
   if (preCheck.Status == AlreadySatisfied && VersionsMatch(preCheck.ActualVersion, package.Detection.ExpectedVersion))
   {
       // Skip install — correct version is present
       Log("PreCheckSkipped — version matches: {Actual} == {Expected}");
       continue;
   }
   else
   {
       // Run install — either not present or wrong version
       Log("PreCheck indicates install needed: {Status}, Actual={Actual}, Expected={Expected}");
   }
   ```

5. **Agent `PostInstallVerify` uses same comparison**
   - After install, verify that `ActualVersion` matches `ExpectedVersion`
   - Return `Success = true` only if versions match

6. **Add comprehensive logging**
   - Per-package probe logs (expected vs actual)
   - Delta summary at end of pre-check phase
   - Structured format for machine parsing

---

## 6. Risks and Edge Cases

### Registry Version Formats
`DisplayVersion` in Windows registry is free-text. Examples:
- "3.14.4" (Python)
- "24.3.0.202412091607" (DBeaver)
- "2.48.1.windows.1" (Git)
- "16.0.18025.20160" (Office)

Need normalization: strip build metadata, compare major.minor.patch.

### version_manifest Output Parsing
Running `python --version` outputs:
```
Python 3.13.3
```
Running `dbeaver --version` might output something completely different.

Need per-package version extraction patterns or standardize on a contract field. See Section 4 (Q3) for normalization strategy.

### UninstallFirst with Wrong Pre-Check
If a `changed` package has `UpgradeBehavior = "UninstallFirst"` and pre-check returns `AlreadySatisfied`, the uninstall phase (Phase 1) also needs to run. Currently:
- `DiffEngine` puts it in `changed`
- `PipelineExecutor` Phase 1 handles `changed` with `UninstallFirst`
- But if pre-check returns `AlreadySatisfied`, the **entire package is skipped** in Phase 2, and Phase 1 uninstall for `changed` packages also checks pre-check

Let me verify this...

Actually, looking at `PipelineExecutor.cs` lines 220-257 (uninstall phase for updates):
```csharp
foreach (var package in changed.Where(p => p.InstallAdapter.UpgradeBehavior == "UninstallFirst"))
{
    if (preCheck.Status == NotPresent) { skip; }
    // else run UninstallPackage
}
```

The uninstall phase does NOT check `AlreadySatisfied` — it only skips if `NotPresent`. So for `UninstallFirst` packages:
- Phase 1: Uninstall old version ✅ (runs because NotPresent is false)
- Phase 2: Install new version ❌ (skipped because AlreadySatisfied is true)

**This is a new bug:** For `UninstallFirst` packages, the old version gets uninstalled but the new version never gets installed!

---

## 7. Immediate Workaround

Until version-aware detection is implemented, the safest behavior is:

**Never skip `changed` packages based on pre-check.** Always run the install/upgrade for packages in the `changed` list. Only skip `added` packages if pre-check returns `AlreadySatisfied` (and even then, only if we trust the version).

This is wasteful but correct. It matches the user's intuition: "Pre-check should always probe the agent to check the ACTUAL EXISTING PACKAGES it has."

---

## 8. Files Requiring Changes

| File | Change Type | Description |
|------|-------------|-------------|
| `shared/contracts/Runtime/RunPayloads/DetectionConfig.cs` | Contract | Add `ExpectedVersion` field |
| `apps/orchestrator/backend/Services/WorkloadRunDispatcher.cs` | Orchestrator | Populate `ExpectedVersion` from `PackageEntity.Version` |
| `apps/agent/backend/Steps/PackageDetector.cs` | Agent | Add version comparison to all detection methods |
| `apps/agent/backend/Pipeline/PipelineExecutor.cs` | Agent | Update skip logic to check version match, not just existence |
| `apps/agent/backend/Steps/PostInstallVerify.cs` | Agent | Same version comparison |
| `apps/agent/backend/Pipeline/DiffEngine.cs` | Agent | Re-evaluate pre-check override logic with version awareness |
| `tests/agent/unit/Steps/PackageDetectorTests.cs` | Tests | Add version comparison tests |
| `tests/orchestrator/unit/WorkloadRunDispatcherTests.cs` | Tests | Verify `ExpectedVersion` in payload |

---

## 9. Conclusion

The system has a **design-level gap**: the pre-check system was built to detect presence/absence, not version correctness. This causes updates to be silently skipped when the old version remains installed.

**The immediate fix** (removing unsafe overrides in `DiffEngine` + not updating `CurrentRevisionId` on no-ops) prevents state corruption but does not fix the actual installation skip.

**The proper fix** requires adding version awareness to `DetectionConfig` and `PackageDetector`, then using that information to make `AlreadySatisfied` actually mean "the correct version is installed."

Without this fix, the update pipeline is **fundamentally unreliable** for versioned packages.
