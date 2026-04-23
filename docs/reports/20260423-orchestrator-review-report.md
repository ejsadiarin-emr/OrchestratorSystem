# Orchestrator Pre-Demo Review Report

**Date:** 2026-04-23
**Scope:** Full stack review of `apps/orchestrator/` — backend, frontend, integration, and E2E flows
**Goal:** Verify demo-readiness for core flows: install, enrollment, artifact upload, workload definition import, and workload execution

---

## Executive Summary

The app **builds and runs** successfully, many individual APIs work in isolation, and the UI renders correctly. However, **the end-to-end demo flow is critically blocked** by several integration gaps and mock-data dependencies. The single biggest blocker is that **artifact upload does not integrate with the package entity system required for workload revision creation** — without this fix, you cannot create a revision or run a workload from the UI.

**Verdict:** Not demo-ready without fixes. Priority items are listed below.

---

## Critical Issues (Demo-Blocking)

### 1. Dashboard is Entirely Mock Data
- **Files:** `web/src/services/api.ts` (lines 256–360), `web/src/pages/Dashboard.tsx`
- **Issue:** `getOrchestratorHomeData()` returns a hardcoded object with 24 fake nodes, fake events, and fabricated KPIs. No real APIs are called.
- **Demo Impact:** Opening the orchestrator after install shows fake data instead of real state.
- **Fix:** Replace with real API calls to `GET /api/nodes`, `GET /api/workload-runs`, `GET /api/artifacts`.

### 2. Real-Time Run Progress is Broken for Real Runs
- **Files:** `web/src/services/api.ts` (lines 953–1019), `web/src/services/realtime.ts`
- **Issue:** `advanceWorkloadRun()` searches a local mock `runs` array. Any real run created via backend throws `"WorkloadRun not found"`. The UI never updates progress.
- **Demo Impact:** The PRIMARY demo goal (run a workload and observe progress) is impossible.
- **Fix:** Replace the mock `advanceWorkloadRun` with either polling `GET /api/workload-runs/{runId}/steps` or a SignalR/WebSocket feed.

### 3. Workload Revision Creation is Broken End-to-End
- **Files:** `web/src/services/api.ts` (lines 702–755), `backend/Controllers/WorkloadsController.cs` (line 46)
- **Issue:** The frontend sends string artifact IDs (e.g. `test-artifact-1.0.0`) in the revision payload, but `WorkloadPackageEntity.PackageId` expects a **Guid** referencing `PackageEntity`. Artifact upload does **not** create a `PackageEntity` record, so no valid Guid exists.
- **Demo Impact:** Submitting a revision returns **400 Bad Request**. You cannot create a workload revision.
- **Fix:** Either (a) make artifact ingest upsert a `PackageEntity` record, or (b) change the revision API to accept artifact string IDs and resolve them internally.

### 4. `detachedSignature` is Dropped by Backend
- **Files:** `web/src/services/api.ts` (line 449), `backend/Controllers/ArtifactsController.cs` (lines 40–42)
- **Issue:** Frontend sends `detachedSignature` in the multipart form, but the controller only reads `file` and `manifest`. The signature never reaches `ArtifactIngestService`.
- **Demo Impact:** Signature verification (fail-closed requirement AC-009 / AC-102) is completely bypassed.
- **Fix:** Update `ArtifactsController.Ingest` to read `form.Files.GetFile("detachedSignature")` and pass it to the service for validation.

### 5. Missing Workload Definition Import from JSON
- **Files:** `web/src/pages/Workloads.tsx`
- **Issue:** The PRD (User Story 3) requires importing workload definitions from a global JSON file. There is no import button, modal, or file handler. The backend `WorkloadImportService` exists but has **no controller endpoint**.
- **Demo Impact:** Demo step 4 (ingest/upload workload definitions) cannot be performed from the UI.
- **Fix:** Add a `POST /api/workloads/import` endpoint and a frontend file-upload modal.

### 6. `wwwroot` Path Mismatch in `dotnet run`
- **Files:** `backend/Program.cs`
- **Issue:** `Program.cs` sets `ContentRootPath = AppContext.BaseDirectory`, so `dotnet run` looks for `wwwroot` in `bin/Debug/net10.0/linux-x64/` instead of the project root. The frontend build outputs to `backend/wwwroot/`, but the runtime can't find it.
- **Demo Impact:** Running via `dotnet run` produces a warning (`WebRootPath was not found`) and fails to serve the embedded UI.
- **Fix:** Remove the explicit `ContentRootPath` override (let ASP.NET use the project directory during dev), or add a build step that copies `wwwroot` to the output directory.

### 7. `AgentLocal` Page Contradicts PRD
- **Files:** `web/src/pages/AgentLocal.tsx`, `web/src/App.tsx`, `web/src/components/layout/Sidebar.tsx`
- **Issue:** The PRD states the agent is **headless** with no local UI. This page shows a mock agent-local console with hardcoded data.
- **Demo Impact:** Architecturally misleading for Phase 1.
- **Fix:** Remove the page and its sidebar link, or repurpose it to show real node workload state from the orchestrator API.

### 8. WorkloadRuns Create Form Defaults to Empty Revision
- **Files:** `web/src/services/api.ts` (lines 625–637), `web/src/pages/WorkloadRuns.tsx` (lines 198–230)
- **Issue:** `listWorkloads()` never populates `latestRevision` (always `undefined`). The create-run modal defaults `revisionId` to an empty string. The revision field is a free-text input, not a dropdown.
- **Demo Impact:** Operator must manually know and type a raw revision UUID.
- **Fix:** Populate `latestRevision` in `listWorkloads()` or fetch revisions per workload and show a dropdown.

### 9. Workloads Page "Latest Version" Always Shows "No version yet"
- **Files:** `web/src/services/api.ts` (lines 625–637), `web/src/pages/Workloads.tsx` (lines 351–365)
- **Issue:** Same root cause as #8 — `latestRevision` is never populated.
- **Demo Impact:** The workload catalog looks broken.
- **Fix:** Same as #8.

---

## High Issues (Significant Problems)

### 10. `listArtifacts` Reconstructs Fake Manifest Objects
- **Files:** `web/src/services/api.ts` (lines 526–565)
- **Issue:** Backend returns flat fields; frontend reconstructs nested `ArtifactManifest` shapes with **hardcoded defaults** that differ from backend resolution:
  - Frontend: `expectedExitCodes: [0]`, `timeoutSeconds: 300`
  - Backend: `expectedExitCodes: [0, 3010]`, `timeoutSeconds: 1800`
- **Fix:** Backend should return full nested objects, or frontend should display flat fields directly.

### 11. `listNodeWorkloadStates` Field Mapping is Wrong
- **Files:** `web/src/services/api.ts` (lines 1021–1035)
- **Issue:** Maps `currentRevisionId` and `state`, but backend returns `workloadRevision` and `status`.
- **Fix:** Use `s.workloadRevision` and `s.status`.

### 12. `EnrollmentToken.singleUse` Type is Literal `true`
- **Files:** `web/src/types.ts` (line 75)
- **Issue:** Declared as `singleUse: true` instead of `boolean`.
- **Fix:** Change to `singleUse: boolean`.

### 13. Dashboard Action Buttons Are Non-Functional
- **Files:** `web/src/pages/Dashboard.tsx` (lines 407–411, 556–560)
- **Issue:** "Start Update", "Approve Risky Update", "Cancel Run", and "Open Run Timeline" buttons have **no `onClick` handlers**.
- **Fix:** Wire to real API calls or remove until implemented.

### 14. WorkloadRuns Target Node Hostnames Always Empty
- **Files:** `web/src/services/api.ts` (lines 828–842)
- **Issue:** Backend `WorkloadRunDetailResponse` only returns `NodeIds` (GUIDs), not hostnames. Frontend maps `targetNodeHostnames: []`.
- **Fix:** Enrich backend response with hostnames, or map IDs to hostnames in frontend.

### 15. Revision Package Steps Show Empty Names/Versions
- **Files:** `web/src/services/api.ts` (lines 654–668, 742–755)
- **Issue:** Backend `WorkloadPackageDto` only returns `PackageId` and `PackageIndex`. Frontend hardcodes `packageName: ''` and `packageVersion: ''`.
- **Fix:** Backend should join to `PackageEntity` to include `Name` and `Version`.

### 16. Missing `GET /api/artifacts/{artifactId}` Integration (API-016)
- **Files:** `web/src/services/api.ts`
- **Issue:** The PRD requires artifact detail. No `getArtifact()` function exists. `Install.tsx` detail modal works only against local state.
- **Fix:** Add `getArtifact` fetch wrapper.

### 17. `createWorkloadRun` Idempotency Key is Non-Deterministic
- **Files:** `web/src/services/api.ts` (line 852)
- **Issue:** Key includes `Date.now()`: `` `${request.workloadId}-${request.revisionId}-${request.mode}-${request.targetNodeIds[0]}-${Date.now()}` ``
- **Fix:** Remove `Date.now()` to make idempotency actually work.

### 18. `WorkloadRun` Response Missing Frontend-Expected Fields
- **Files:** `web/src/types.ts` (lines 149–163), `backend/Models/WorkloadRunResponseModels.cs` (lines 10–23)
- **Issue:** Frontend expects `workloadName`, `targetNodeHostnames`, `diagnostics`, `startedAt`. Backend does not return them.
- **Fix:** Add these fields to `WorkloadRunDetailResponse`.

---

## Medium Issues (Should Fix, Not Demo-Blocking)

| # | Issue | Files |
|---|-------|-------|
| 19 | `InfoHint` auto-opens on hover (violates AC-107) | `web/src/pages/dashboard/InfoHint.tsx` |
| 20 | "cancel" exposed as run creation mode | `web/src/pages/WorkloadRuns.tsx` |
| 21 | `CommandCenter.tsx` is dead mock code | `web/src/pages/CommandCenter.tsx`, `App.tsx` |
| 22 | `Packages.tsx` uses legacy styling and confusing label | `web/src/pages/Packages.tsx` |
| 23 | `PolicyTagsInput` includes Phase 2 deferred fields | `web/src/types.ts` |
| 24 | Frontend `RiskLevel` type uses `'med'` instead of PRD `'medium'` | `web/src/types.ts` |
| 25 | No error handling for silent fetch failures in `Install.tsx` | `web/src/pages/Install.tsx` |
| 26 | Legacy `InstallController` and `PackagesController` are dead surface area | `backend/Controllers/InstallController.cs`, `PackagesController.cs` |

---

## Build & Runtime Status

| Component | Status |
|-----------|--------|
| Backend `dotnet build` | PASS (0 warnings, 0 errors) |
| Frontend `pnpm build` | PASS |
| Backend starts | PASS (migrations apply) |
| Swagger UI | PASS |
| Artifact upload UI | PASS |
| Workload definition create UI | PASS |
| Workload revision create UI | **FAIL** (400 Bad Request) |
| Workload run creation UI | **BLOCKED** (no enrolled nodes) |
| Dashboard | Loads, but shows **mock data** |

---

## Recommended Fix Priority for Demo

| Priority | Fix | Files |
|---|---|---|
| **P0** | Fix artifact → package entity bridge so workload revisions can be created | `ArtifactIngestService`, `WorkloadsController`, `api.ts` |
| **P0** | Fix `wwwroot` serving during `dotnet run` | `Program.cs` |
| **P0** | Replace `advanceWorkloadRun` with real step-status polling | `api.ts`, `realtime.ts` |
| **P0** | Replace `getOrchestratorHomeData` with real API calls | `api.ts`, `Dashboard.tsx` |
| **P0** | Populate `latestRevision` in `listWorkloads` | `api.ts`, `Workloads.tsx`, `WorkloadRuns.tsx` |
| **P0** | Add workload definition JSON import UI + backend endpoint | `Workloads.tsx`, `WorkloadsController` |
| **P1** | Fix `listNodeWorkloadStates` field mapping | `api.ts` |
| **P1** | Pass `detachedSignature` through controller to service | `ArtifactsController.cs`, `ArtifactIngestService` |
| **P1** | Wire Dashboard action buttons or remove them | `Dashboard.tsx` |
| **P1** | Map node IDs to hostnames in run list | `api.ts`, `WorkloadRuns.tsx` |
| **P2** | Fix `InfoHint` hover behavior | `InfoHint.tsx` |
| **P2** | Remove dead `CommandCenter` code | `CommandCenter.tsx`, `App.tsx` |
| **P2** | Remove "cancel" from run creation mode | `WorkloadRuns.tsx` |

---

## Conclusion

The orchestrator is structurally sound but has **critical integration gaps** that prevent the core demo flow from working end-to-end. The highest-value fixes are:
1. Closing the artifact/package entity bridge.
2. Fixing the `wwwroot` path for local runs.
3. Replacing the mock run-progress mechanism with real API polling.
4. Populating `latestRevision` so workload runs can actually be created.

Once these four items are resolved, the app will be demo-ready. The remaining issues can be addressed incrementally.
