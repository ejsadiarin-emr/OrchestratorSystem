# Uninstall Pipeline & Pre-Check Architecture Audit

## RESOLVED (2026-05-02)

All 11 issues resolved.

### ~~1. Artifact download when no UninstallCommand is wrong~~ FIXED
### ~~2. Registry detection is a stub~~ FIXED
### ~~3. UninstallPackage fallback chain is dangerous~~ FIXED
### ~~4. Frontend displays wrong version in node list~~ FIXED
### ~~5. UninstallCommand not exposed in Package API model~~ FIXED
### ~~6. Pre-check is manual, not automatic~~ FIXED
### ~~7. Duplicate StepStatusPayload/FinalizationPayload classes~~ FIXED
### ~~8. DetectionConfig fallback is too generic~~ FIXED
### ~~9. WorkloadPreCheck only checks disk space~~ FIXED
### ~~10. UI is cramped~~ FIXED
### ~~11. No uninstall dry-run~~ FIXED

## Resolution Summary

| # | Issue | Resolution |
|---|-------|------------|
| 1-3 | Uninstall pipeline regressions | Three-tier command resolution: explicit UninstallCommand → registry lookup → skip. Removed dangerous fallbacks. |
| 4 | Wrong version displayed | Changed `workloadRevision` to `currentRevisionId` in node list. |
| 5 | UninstallCommand not in API | Added `UninstallCommand`/`UninstallArgs` to Package model, CreatePackageRequest, and controller. |
| 6 | Manual pre-check | Auto-runs via `useEffect` when workload + revision selected. Manual button preserved. |
| 7 | Duplicate DTOs | Consolidated `StepStatusPayload`/`FinalizationPayload` into shared contracts. |
| 8 | DetectionConfig fallback | Added `DetectionType`/`DetectionPath`/`ExpectedVersion` to Package API model; serialized to `DetectionConfigJson`. |
| 9 | WorkloadPreCheck limited | Added admin privilege, package count, and process lock checks as non-blocking warnings. |
| 10 | UI cramped | Widened modal to 56rem, increased node list to max-h-64, badges to text-xs. |
| 11 | No dry-run | Added `GET /api/workload-runs/preview` endpoint returning per-node package diff. |

## Commits

| Commit | Issues |
|--------|--------|
| `6eb5d8f` | #1, #2, #3 |
| `e7ec2e8` | #4, #6 |
| `a1a62dd` | #5 |
| `cfa9ca1` | #7 |
| `1b4ca31` | #9 |
| `77be992` | docs |
