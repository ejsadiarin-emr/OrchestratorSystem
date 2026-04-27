# DeploymentPoC Bug Fix Implementation Plan

## Executive Summary

The deployment pipeline has 6 bugs that prevent MSI packages from installing and cause verification to fail even when installation succeeds. All 6 bugs are independently fixable and should be addressed in priority order.

---

## Bug 1: MSI files cannot be executed directly as Process.FileName (CRITICAL)

**File:** `apps/agent/backend/Steps/InstallOrUpgrade.cs` lines 21-29, 56-64

**Problem:** When `InstallAdapterConfig.Command` is `{artifactPath}`, the artifact path (e.g., `C:\...\node-v22.14.0-x64.msi`) is used directly as `process.StartInfo.FileName` with `UseShellExecute = false`. Windows cannot execute `.msi` files directly — they must be invoked via `msiexec /i <path>`. This causes `Win32Exception` with `NativeErrorCode 1155` (ERROR_NO_ASSOCIATION), which is NOT caught by the elevation-retry handler (which only catches error 740).

**Fix:** When `installType` is `"msi"` and the command resolves to the artifact path, rewrite the process invocation to use `msiexec` as the command and pass the artifact path as an argument:

```csharp
// In InstallOrUpgrade.ExecuteAsync, after line 29 (command resolution):
if (string.Equals(config.Type, "msi", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(command, artifactPath, StringComparison.OrdinalIgnoreCase))
{
    command = "msiexec";
    arguments = $"/i \"{artifactPath}\" {arguments}";
}
```

**Testing:** Verify node-v22.14.0-x64.msi installs silently when run as `msiexec /i "path\to\file.msi" /quiet /norestart`. Also verify that when `config.Command` is explicitly set to something other than `{artifactPath}`, it is NOT rewritten.

---

## Bug 2: version_manifest detection searches wrong binary names (HIGH)

**File:** `apps/agent/backend/Steps/PackageDetector.cs` lines 111-157

**Problem:** The `DetectVersionManifestAsync` method searches for the `config.Path` value directly and with `.exe` appended. For `nodejs`, it searches for `nodejs` and `nodejs.exe` in PATH + common directories, but the actual binary is `node.exe`. For `python`, the hardcoded search paths only go up to `Python312`, not `Python313`. The `AppData\Local\Programs\Python` directory is checked but `python.exe` lives in a `Python313` subdirectory.

**Fix:** Add a well-known binary name mapping and expand the search to include subdirectory scanning:

```csharp
// Add a binary name alias map for common packages
private static readonly Dictionary<string, string[]> BinaryAliases = new(StringComparer.OrdinalIgnoreCase)
{
    ["nodejs"] = ["node"],
    ["node"] = ["node"],
    ["python"] = ["python", "python3"],
    ["git"] = ["git"],
};

// In DetectVersionManifestAsync, replace lines 129-157 with:
foreach (var dir in searchPaths.Distinct(StringComparer.OrdinalIgnoreCase))
{
    var namesToSearch = BinaryAliases.TryGetValue(path, out var aliases)
        ? aliases.Concat([path]).ToArray()
        : [path];

    foreach (var name in namesToSearch)
    {
        var fullPath = Path.Combine(dir, name);
        if (File.Exists(fullPath))
        {
            return DetectFileAsync(fileConfig with { Path = fullPath }, ct);
        }

        if (OperatingSystem.IsWindows())
        {
            var exePath = fullPath + ".exe";
            if (File.Exists(exePath))
            {
                return DetectFileAsync(fileConfig with { Path = exePath }, ct);
            }
        }
    }
}
```

Also add `Python313` and `Python314` to the hardcoded Python paths (lines 124-127), and consider replacing the hardcoded list with a dynamic directory scan of `Program Files\Python*` and `AppData\Local\Programs\Python\Python*`.

**Testing:** Verify that detecting `nodejs` finds `C:\Program Files\nodejs\node.exe` and detecting `python` finds `C:\Program Files\Python313\python.exe` or `C:\Users\<user>\AppData\Local\Programs\Python\Python313\python.exe`.

---

## Bug 3: ExpectedVersion == prefix never stripped (HIGH)

**File:** `apps/agent/backend/Steps/PackageDetector.cs` lines 162-167

**Problem:** `VersionEquals(actual, expected)` calls `NormalizeVersion` which strips trailing `.0` segments but does NOT strip the `==` prefix from version constraint syntax. Manifest files may declare `ExpectedVersion: "==22.14.0"` but after normalization the comparison becomes `"22.14.0" == "==22.14.0"`, which fails.

**Fix:** Strip leading comparison operators (`==`, `>=`, `<=`, `>`, `<`, `=`) in `NormalizeVersion`:

```csharp
private static string NormalizeVersion(string version)
{
    var v = version.Trim();

    // Strip leading comparison operators (==, >=, <=, >, <, =)
    var opMatch = System.Text.RegularExpressions.Regex.Match(v, @"^(==|>=|<=|>|<|=)");
    if (opMatch.Success)
    {
        v = v[opMatch.Length..];
    }

    // Strip trailing .0 segments
    while (v.EndsWith(".0", StringComparison.Ordinal) && v.Count(c => c == '.') > 1)
    {
        v = v[..^2];
    }

    return v;
}
```

**Testing:** Verify `VersionEquals("22.14.0", "==22.14.0")` returns `true`, `VersionEquals("22.14.0", ">=22.14.0")` returns `true`, and `VersionEquals("22.14.1", "==22.14.0")` returns `false`.

---

## Bug 4: BulkImport doesn't set DetectionConfigJson (HIGH)

**File:** `apps/orchestrator/backend/Controllers/WorkloadsController.cs` lines 568-580

**Problem:** When `BulkImport` creates a `PackageEntity`, it does not set `DetectionConfigJson`. This causes `WorkloadRunDispatcher.BuildDetectionConfig()` to fall back to a default `DetectionConfig { Type = "version_manifest", Path = "nodejs", ExpectedVersion = "22.14.0" }`, where `Path` is the package name (not the actual binary name) and `ExpectedVersion` lacks the context of what detection type/mode to use.

**Fix:** Modify `ResolvePlaceholderAdapter` to also return detection config, and set it on the `PackageEntity`. Change the return type from tuple to a richer model:

```csharp
// New return type
private sealed class AdapterResolution
{
    public string InstallType { get; set; } = "exe";
    public string SourcePath { get; set; } = "{artifactPath}";
    public string InstallArgs { get; set; } = "";
    public string UninstallArgs { get; set; } = "";
    public string ExpectedExitCodesJson { get; set; } = "[0]";
    public int TimeoutSeconds { get; set; } = 300;
    public string? DetectionConfigJson { get; set; }  // NEW
}

// In ResolvePlaceholderAdapter, after reading the manifest's InstallAdapter,
// also read the Detection section:
private async Task<AdapterResolution> ResolvePlaceholderAdapter(string packageId, string version)
{
    var manifestJson = await _artifactStore.GetResolvedManifestAsync(packageId, version);
    if (!string.IsNullOrWhiteSpace(manifestJson))
    {
        try
        {
            var manifest = System.Text.Json.JsonSerializer.Deserialize<ResolvedManifest>(manifestJson, ...);
            if (manifest?.InstallAdapter is not null)
            {
                var adapter = manifest.InstallAdapter;
                string? detectionConfigJson = null;
                if (manifest.Detection is not null)
                {
                    var detectionConfig = new DetectionConfig
                    {
                        Type = manifest.Detection.Type ?? "version_manifest",
                        Path = manifest.Detection.Path ?? packageId,
                        ExpectedVersion = manifest.Detection.ExpectedVersion ?? version
                    };
                    detectionConfigJson = System.Text.Json.JsonSerializer.Serialize(detectionConfig);
                }

                return new AdapterResolution
                {
                    InstallType = string.IsNullOrWhiteSpace(adapter.Type) ? "exe" : adapter.Type,
                    SourcePath = string.IsNullOrWhiteSpace(adapter.Command) ? "{artifactPath}" : adapter.Command,
                    InstallArgs = adapter.Arguments ?? "",
                    UninstallArgs = "",
                    ExpectedExitCodesJson = adapter.ExpectedExitCodes is { Count: > 0 }
                        ? System.Text.Json.JsonSerializer.Serialize(adapter.ExpectedExitCodes)
                        : "[0]",
                    TimeoutSeconds = adapter.TimeoutSeconds > 0 ? adapter.TimeoutSeconds : 300,
                    DetectionConfigJson = detectionConfigJson
                };
            }
        }
        catch (System.Text.Json.JsonException) { /* fallback to default */ }
    }

    return new AdapterResolution();
}
```

Then in `BulkImport` (line 568), add `DetectionConfigJson = adapter.DetectionConfigJson`:

```csharp
var packageEntity = new PackageEntity
{
    PackageId = deterministicId,
    Name = packageId,
    Version = version,
    SourcePath = adapter.SourcePath,
    InstallType = adapter.InstallType,
    InstallArgs = adapter.InstallArgs,
    UninstallArgs = adapter.UninstallArgs,
    ExpectedExitCodesJson = adapter.ExpectedExitCodesJson,
    TimeoutSeconds = adapter.TimeoutSeconds,
    DetectionConfigJson = adapter.DetectionConfigJson,  // NEW
    CreatedAtUtc = now
};
```

**Testing:** After bulk importing `nodejs-22.14.0`, verify the `PackageEntity` row has `DetectionConfigJson` populated with `{"Type":"version_manifest","Path":"node","ExpectedVersion":"22.14.0"}` (or whatever the manifest specifies).

---

## Bug 5: Frontend sends wrong packageId type (HIGH)

**File:** `apps/orchestrator/web/src/pages/Workloads.tsx` lines 127, 462

**Problem:** The `onCreateRevision` handler uses `artifact.id` (a composite string like `"nodejs-22.14.0"`) as `packageId` in the revision creation request, but the backend `WorkloadPackageInput.PackageId` is type `Guid`. The correct field is `artifact.packageEntityId`, which holds the `DeterministicGuid` value.

**Fix in `Workloads.tsx`:**

Line 127 — change `artifact.id` to `artifact.packageEntityId`:
```tsx
packageSteps: selectedPackages.map((artifact, index) => ({
  packageId: artifact.packageEntityId ?? artifact.id,  // prefer GUID
  packageName: artifact.manifest.packageId ?? artifact.fileName,
  packageVersion: artifact.manifest.version ?? '0.0.0',
  packageIndex: index + 1,
  stepId: 'install-or-upgrade',
})),
```

Line 462 — change `value={artifact.id}` to `value={artifact.packageEntityId ?? artifact.id}`:
```tsx
<option key={artifact.id} value={artifact.packageEntityId ?? artifact.id}>
```

Line 112 — update `selectedPackages` filter:
```tsx
const selectedPackages = useMemo(() => {
  return artifacts.filter(artifact =>
    revisionForm.packageIds.includes(artifact.packageEntityId ?? artifact.id))
}, [artifacts, revisionForm.packageIds])
```

Also fix the validation constraint on line 115 to allow 1+ packages (currently requires 2-3):
```tsx
const canCreateRevision = revisionForm.packageIds.length >= 1
```

**Testing:** Create a revision draft from the UI, verify the POST body contains a valid GUID in `packages[].packageId` and the backend accepts it.

---

## Bug 6: ResolvePlaceholderAdapter doesn't resolve Detection config (MEDIUM)

**File:** `apps/orchestrator/backend/Controllers/WorkloadsController.cs` lines 391-425

**Problem:** The current `ResolvePlaceholderAdapter` method only reads `InstallAdapter` from the resolved manifest, ignoring the `Detection` section entirely. This is the root cause behind Bug 4 — even when the manifest has detection config, it's not persisted to `DetectionConfigJson`.

**Fix:** This is addressed in the Bug 4 fix above — the enhanced `ResolvePlaceholderAdapter` now also returns `DetectionConfigJson` populated from `manifest.Detection`.

---

## Implementation Order

| Priority | Bug | Risk | Effort |
|----------|-----|------|--------|
| 1 | Bug 1: MSI execution fix | Critical — blocks all MSI installs | Small |
| 2 | Bug 4+6: BulkImport + ResolvePlaceholderAdapter detection | High — detection data lost | Medium |
| 3 | Bug 3: ExpectedVersion == prefix stripping | High — verification always fails | Small |
| 4 | Bug 2: Binary name search aliases | High — nodejs/python never detected | Medium |
| 5 | Bug 5: Frontend packageId fix | High — UI revision creation broken | Small |

## Files to Modify

| File | Bugs Addressed |
|------|---------------|
| `apps/agent/backend/Steps/InstallOrUpgrade.cs` | Bug 1 |
| `apps/agent/backend/Steps/PackageDetector.cs` | Bugs 2, 3 |
| `apps/orchestrator/backend/Controllers/WorkloadsController.cs` | Bugs 4, 6 |
| `apps/orchestrator/web/src/pages/Workloads.tsx` | Bug 5 |

## Testing Strategy

1. **Bug 1 (MSI):** Run agent pipeline with node-v22.14.0-x64.msi — should invoke `msiexec /i` instead of direct execution
2. **Bug 2 (Detection paths):** Unit test `PackageDetector` with known Windows paths for node/python/git
3. **Bug 3 (Version prefix):** Unit test `VersionEquals("22.14.0", "==22.14.0")` returns true
4. **Bugs 4+6 (Detection config):** Bulk import workloads-older.json, verify `DetectionConfigJson` column in DB
5. **Bug 5 (Frontend):** Create revision draft from UI, confirm POST sends GUID not composite string