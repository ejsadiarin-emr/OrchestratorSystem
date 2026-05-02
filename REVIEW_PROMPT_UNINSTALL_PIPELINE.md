# Review Request: Uninstall Pipeline & Pre-check Architecture

## Problem Statement

The uninstall workflow in the Run Creator modal has several issues that need architectural review and refactoring:

### 1. Uninstall downloads artifacts unnecessarily
- **Current behavior:** When a package has no dedicated `UninstallCommand`, the `PipelineExecutor` downloads the full artifact to a temp path and passes it to `UninstallPackage.ExecuteAsync()` as a fallback.
- **Log evidence:** `Step AcquireArtifactForUninstall` initiates chunked download (~119MB artifact), then `UninstallPackage` fails with `exit_code_1`.
- **Expected behavior:** Uninstall should **NOT** download artifacts. It should either:
  - Use a dedicated `UninstallCommand` (e.g., `C:\Program Files\DBeaver\unins000.exe`) with `UninstallArgs`.
  - Or use system-level uninstall mechanisms (registry-based, winget, msiexec product code).
  - The artifact is the **INSTALLER**, not the uninstaller — downloading it for uninstall is wasteful and often wrong.

### 2. Pre-checks are manual and not informative
- **Current behavior:** User must click "Run pre-check" button manually.
- When issues are found, the badge shows "pre-check: issues" but doesn't display **WHAT** failed without hovering.
- **Expected behavior:** Pre-checks should happen automatically when the Run Creator modal opens (background), and results should be visible inline.

### 3. UI is cramped
- The Run Creator modal is `w-[min(92vw,48rem)]` — too narrow to show node details, pre-check results, and version info clearly.
- Node list shows multiple inline badges (OS, version, pre-check status, drift) causing overflow.

## Context: Already Applied Fixes

1. **Backend (`NodeWorkloadStateResponse.cs`, `NodesController.cs`)**: Added `CurrentRevisionId` to fix uninstall node filtering.
2. **Frontend (`api.ts`, `types.ts`, `WorkloadRuns.tsx`)**: Fixed version string vs GUID mismatches in uninstall filtering.
3. **Agent (`PipelineExecutor.cs`)**: Removed early halt for missing `UninstallCommand` and added artifact acquisition fallback (this introduced the download problem).
4. **Frontend badge tooltip**: Enhanced to show actual failing pre-check items.

## Files Involved

- `apps/agent/backend/Pipeline/PipelineExecutor.cs` — main pipeline orchestration
- `apps/agent/backend/Steps/UninstallPackage.cs` — uninstall execution logic
- `apps/orchestrator/backend/Controllers/NodesController.cs` — pre-check endpoint (`ReconcileProbeResults`)
- `apps/orchestrator/backend/Services/WorkloadRunDispatcher.cs` — payload assembly
- `apps/orchestrator/web/src/pages/WorkloadRuns.tsx` — Run Creator UI
- `shared/contracts/Runtime/RunPayloads/InstallAdapterConfig.cs` — adapter config schema

## Goal

Design and implement a complete uninstall pipeline that:

1. **Never downloads artifacts for uninstall** — instead relies on `UninstallCommand` + `UninstallArgs` or system-level detection.
2. **Auto-runs pre-checks** — when `/workload-runs` page loads OR when Run Creator opens, pre-check all online nodes in background for the selected workload.
3. **Shows pre-check results inline** — expand UI width and display per-node pre-check status with expandable detail (not just hover tooltip).
4. **Handles missing `UninstallCommand` gracefully** — either:
   - Fail fast with clear error: "Package 'X' has no uninstall command configured"
   - Or auto-detect uninstaller from registry/Add Remove Programs using the package name

## Review Request

Please review the current architecture and provide:

1. **Architectural assessment** — Is downloading artifacts for uninstall fundamentally wrong? What should the uninstall contract look like?
2. **Uninstall strategy options** — How should the agent uninstall packages without the installer artifact? (registry lookup, dedicated uninstall command, etc.)
3. **Pre-check integration design** — Should pre-checks be:
   - Triggered automatically on modal open?
   - Cached per-node and refreshed periodically?
   - Run for ALL modes (install/update/uninstall) since they detect current state?
4. **UI/UX recommendations** — How wide should the modal be? How to display per-node pre-check details without clutter?
5. **Implementation plan** — Step-by-step changes needed across backend, agent, and frontend.
