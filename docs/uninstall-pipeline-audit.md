# Uninstall Pipeline & Pre-Check Architecture Audit

## RESOLVED (2026-05-02)

### ~~1. Artifact download when no UninstallCommand is wrong~~ FIXED
### ~~2. Registry detection is a stub~~ FIXED
### ~~3. UninstallPackage fallback chain is dangerous~~ FIXED

**Resolution:** Implemented three-tier uninstall command resolution (`PipelineExecutor.cs`, `UninstallPackage.cs`, `PackageDetector.cs`):

1. Priority chain: explicit `UninstallCommand` → Windows registry lookup (`QuietUninstallString` / `UninstallString` from all 4 paths) → skip-with-warning
2. Removed `Command` fallback (never run install command for uninstall)
3. Removed `artifactPath`-as-command fallback (never execute downloaded artifact as uninstaller)
4. Artifact download only occurs when MSI type or `{artifactPath}` placeholder is present
5. `PackageDetector.DetectRegistryAsync` fully implemented: queries `HKLM`/`HKCU` × `Registry64`/`Registry32` for `DisplayName` match and `DisplayVersion` comparison
6. `UninstallPackage.ResolveRegistryUninstaller` queries registry for `QuietUninstallString` (preferred) or `UninstallString`, parses into executable + arguments

### ~~4. Frontend displays wrong version in node list~~ FIXED
### ~~5. UninstallCommand not exposed in Package API model~~ FIXED
### ~~6. Pre-check is manual, not automatic~~ FIXED
### ~~7. Duplicate StepStatusPayload/FinalizationPayload classes~~ FIXED
### ~~9. WorkloadPreCheck only checks disk space~~ FIXED

**Resolution for #4-#9:**

- **#4:** `WorkloadRuns.tsx` — Changed `installedVersion` from `workloadRevision` to `currentRevisionId`
- **#5:** `Package.cs`, `PackagesController.cs` — Added `UninstallCommand`/`UninstallArgs` to API model, `CreatePackageRequest`, and all controller mappings
- **#6:** `WorkloadRuns.tsx` — Added `useEffect` auto-running pre-check when `workloadId` and `revisionId` are both selected; manual button preserved as fallback
- **#7:** Moved `StepStatusPayload` and `FinalizationPayload` from `PipelineExecutor.cs` and `NodeWorkloadStateService.cs` into shared contracts (`StepStatusPayload.cs` in `DeploymentPoC.Contracts.Runtime.RunPayloads`)
- **#9:** `WorkloadPreCheck.cs` — Added admin privilege check, high package count warning, and process lock detection; added `Warnings` list to result

---

## REMAINING ISSUES

### MEDIUM SEVERITY

#### 8. DetectionConfig fallback is too generic

`WorkloadRunDispatcher.cs` — `BuildDetectionConfig` falls back to `type=version_manifest, path=pkg.Name`. No ability to configure registry-based detection per package.

**Fix:** Expose detection type in manifest/Package API so admins can configure registry-based detection.

### LOW SEVERITY / COSMETIC

#### 10. UI is cramped

`w-[min(92vw,48rem)]` modal with `max-h-48` node list and `text-[10px]` badges.

#### 11. No uninstall dry-run

Pipeline doesn't support "what would happen?" preview before executing.

---

## NEXT STEPS

| Priority | Issue | Fix | Effort |
|----------|-------|-----|--------|
| **1** | #8 DetectionConfig fallback | Expose detection type in API | Low |
| **2** | #10 UI cramped | Increase modal size, improve layout | Medium |
| **3** | #11 No dry-run | Preview endpoint or step | Medium |
