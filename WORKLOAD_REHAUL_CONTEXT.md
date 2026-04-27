# Workload Installation Rehaul — Project Context & Implementation Plan

> **Purpose:** This document provides the full technical context for another architect agent to review and refine the orchestrator-to-agent workload installation rehaul plan. It corrects misconceptions from the initial plan (e.g., Python stack, new `jobs` table) and incorporates live-debugging findings.

---

## 0. Common Language & Domain Concepts

Before reading the technical sections, here is the vocabulary used throughout this codebase.

### Workload
A **workload** is a named, versioned collection of packages that should be installed together on a target machine. Think of it as a "bundle" or "software stack."

**Example:** `"Dev Tools Stack"` contains `nodejs-24.13.0` and `python-3.14.4`.

**JSON Schema (workload definition):**
```json
{
  "name": "Dev Tools Stack",
  "slug": "dev-tools-stack",
  "description": "Essential development tools including Node.js and Python.",
  "version": "2.0.0",
  "packages": [
    "nodejs-24.13.0",
    "python-3.14.4"
  ]
}
```
- `packages` is an array of **Package IDs** (`<name>-<version>` strings).
- Workloads are ingested via the UI/API and stored as `WorkloadDefinitionEntity` + `WorkloadRevisionEntity` + `WorkloadPackageEntity` in SQLite.

### Artifact
An **artifact** is the physical installer media for a single package. It consists of **two parts**:
1. **Binary file** — the actual `.msi`, `.exe`, `.zip`, etc. installer.
2. **Manifest JSON** — metadata describing the package, its install adapter, detection config, and policy tags.

**Example artifact manifest (`dotnet-runtime.manifest.json`):**
```json
{
  "packageId": "dotnet-runtime",
  "version": "10.0.7",
  "filename": "dotnet-runtime-10.0.7-win-x64.exe",
  "checksum": "sha256:abc123...",
  "installAdapter": {
    "type": "exe",
    "arguments": ["/install", "/quiet", "/norestart"]
  },
  "detection": {
    "type": "registry",
    "key": "HKLM\\SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64\\sharedhost",
    "valueName": "Version",
    "expectedValue": "10.0.7"
  },
  "policyTags": ["runtime", "microsoft"]
}
```
- `installAdapter` tells the agent **how** to run the installer (e.g., `msiexec /i`, `exe /silent`, `dpkg -i`).
- `detection` tells the agent **how to check** if the package is already installed (registry key, file path, etc.).
- Artifacts are uploaded to the orchestrator, stored in the **Local Artifact Store**, and tracked as `PackageEntity` rows in SQLite.

### Package
A **package** is the logical unit represented by a `PackageEntity` in the database. It links an artifact binary + manifest to a deterministic GUID (`PackageEntityId`). When a workload references `"nodejs-24.13.0"`, the orchestrator resolves it to a `PackageEntity`.

### Workload Run
A **workload run** (or "job") is a single execution of a workload against a specific agent node. It tracks state (`Queued`, `InProgress`, `Done`, `Failed`) and is stored as a `WorkloadRunEntity`.
- **Do NOT confuse with a `jobs` table.** The existing `WorkloadRunEntity` is the job table.

### Agent Node
An **agent node** is a target machine (Windows VM, physical box, etc.) that runs the `DeploymentPoC.Agent.exe` process. It enrolls with the orchestrator by storing its `NodeId` and the orchestrator's URL. The agent polls (or currently receives SignalR pushes from) the orchestrator for work.

### Local Artifact Store
The artifact binaries are stored on the orchestrator's **local disk** (not S3, not a CDN). Default path:
```
apps/orchestrator/backend/bin/Release/net10.0/win-x64/publish/artifacts/
```
The orchestrator serves them via `ArtifactsController` (`GET /api/artifacts/{name}/{version}`).

### SQLite Database
The orchestrator uses a single **SQLite** file as its database:
```
apps/orchestrator/backend/bin/Release/net10.0/win-x64/publish/data/deployment-poc.db
```
- Accessed via **EF Core** (`InstallerDbContext`).
- Contains all entities: workloads, packages, artifact metadata, workload runs, timeline events, and node states.
- Migrations are applied automatically on startup.

---

## 1. Tech Stack & Architecture

| Component | Technology |
|---|---|
| **Orchestrator Backend** | .NET 9 C# (port 5000) |
| **Agent Backend** | .NET 9 C# (port 5001) |
| **Database** | SQLite via EF Core (`InstallerDbContext`) with migrations |
| **Real-time Communication** | SignalR (AspNetCore.SignalR) — **to be replaced** |
| **Frontend** | React / TypeScript (Vite) |
| **Agent Host** | `IHostedService` (`AgentRuntimeService`) |
| **Artifact Store** | Filesystem under `artifacts/`, deterministic GUID package IDs |

### Existing Database Entities (SQLite)
- `WorkloadRunEntity`
- `WorkloadRunTimelineEntity`
- `NodeWorkloadStateEntity`
- `PackageEntity`
- `WorkloadDefinitionEntity`
- `WorkloadRevisionEntity`
- `WorkloadPackageEntity`

**Do NOT create a new `jobs` table.** Use `WorkloadRunEntity` for job state.

---

## 2. Current Architecture Flow

### Orchestrator Side
1. `WorkloadRunsController` creates a `WorkloadRunEntity` (state = `Queued`).
2. `WorkloadRunDispatcher.DispatchAsync()` pushes `AssignRun` via SignalR to group `node-{id}`.
3. Orchestrator listens for inbound messages (`AckClaim`, `StepStatus`, `Complete`, `Fail`) via `NodeWorkloadStateService` and updates SQLite.

### Agent Side
1. `AgentRuntimeService` maintains a persistent SignalR connection to `http://<orchestrator>:5000/hubs/agent`.
2. On `AssignRun`, it ACKs with `AckClaim`, then launches `PipelineExecutor`.
3. **Pipeline Steps** (in order):
   - `PreCheckProbe`
   - `DiffEngine`
   - `Uninstall` (reverse order)
   - `AcquireArtifact` (HTTP GET `/api/artifacts/{name}/{version}`)
   - `InstallOrUpgrade` (subprocess with UAC elevation fallback)
   - `PostInstallVerify`
   - `Finalize`
4. Step statuses sent back via SignalR `SendMessage`.

---

## 3. The Actual Failure Mode (CRITICAL)

> **The pipeline itself is fundamentally broken at the `PreCheckProbe → DiffEngine` layer, causing it to skip installation entirely.**

### What Happens Right Now (Live Logs)
```
info: ...AgentRuntimeService[0]
      [INSTRUMENTATION] Received AssignRun envelope: RunId=..., AgentId=..., MessageType=AssignRun
info: ...AgentRuntimeService[0]
      Received AssignRun: Workload=Dev Tools Stack, Packages=3, RunId=...
info: ...AgentRuntimeService[0]
      Sent AckClaim for RunId=...
info: ...Pipeline.PipelineExecutor[0]
      Step PreCheckProbe: PackageIndex=1, PackageId=57aca7ca-...
info: ...Pipeline.PipelineExecutor[0]
      Step PreCheckProbe: PackageIndex=2, PackageId=e4311b64-...
info: ...Pipeline.PipelineExecutor[0]
      Step PreCheckProbe: PackageIndex=3, PackageId=6c81318e-...
info: ...Pipeline.PipelineExecutor[0]
      Pipeline diff computed: Added=0, Removed=0, Changed=0, Unchanged=3
info: ...Pipeline.PipelineExecutor[0]
      Pipeline starting: RunId=..., Workload=Dev Tools Stack, Mode=install, TargetPackages=3
info: ...AgentRuntimeService[0]
      Pipeline completed: RunId=..., Success=True, StepsExecuted=3
```

**Result:** Orchestrator thinks it succeeded. Agent thinks it succeeded. But **nothing was installed** (verified by checking machine for nodejs/python — absent).

---

## 4. Root Cause: PreCheckProbe Stubs

### `PackageDetector` Detection Types

| Type | Implementation | Current Behavior |
|---|---|---|
| `file` | `DetectFileAsync` | Checks `File.Exists(config.Path)` and version if specified. **Works.** |
| `registry` | `DetectRegistryAsync` | **Stub.** Returns `AlreadySatisfied` unconditionally. |
| `version_manifest` | `DetectVersionManifestAsync` | **Stub.** If `config.Path` exists, runs `DetectFileAsync`. Otherwise returns `AlreadySatisfied`. |

### How the Wrong Config Gets Generated
1. Test artifact manifests for `git`, `nodejs`, `python` (generated by `scripts/download-test-artifacts.sh`) **do NOT contain a `detection` block**.
2. During ingestion, `ArtifactIngestService.ResolveDetection` (~lines 207-235) auto-generates a fallback:
   ```csharp
   Type = "version_manifest"
   Path = manifest.PackageId   // e.g., "git", "nodejs", "python"
   ExpectedVersion = $"=={version}"
   ```
3. Agent receives this. It checks if a file named `git`, `nodejs`, or `python` exists in the agent's working directory (`C:\Users\ej\Documents\`). They don't.
4. `DetectVersionManifestAsync` stub falls through to `return AlreadySatisfied`.
5. `DiffEngine` moves all packages to `Unchanged`. The install loop is skipped entirely.

### Conclusion
**The pipeline is currently a no-op for any package without a real detection block.** This is a stub bug, not an architectural failure.

---

## 5. Component Status Matrix

| Component | Status | Notes |
|---|---|---|
| Uploading artifacts/packages | ✅ Works | Via orchestrator UI/API |
| Uploading workload JSON | ✅ Works | Ingests to SQLite |
| Enrolling agent node | ✅ Works | Agent stores `NodeId` + orchestrator URL |
| SignalR dispatch (`AssignRun`) | ⚠️ Brittle | Works when connected; stalls on reconnect. To be replaced. |
| **PreCheckProbe / DiffEngine** | ❌ **Broken** | **Silent no-op installs due to `AlreadySatisfied` stub.** |
| `AcquireArtifact` (HTTP download) | ✅ Works | Downloads binary when called |
| `InstallOrUpgrade` (actual installer) | ✅ Works when called | Has Windows edge cases (`win32_error_1155`, `AdapterType=unknown`, UAC elevation) |
| Status reporting (`SendMessage`) | ⚠️ Best-effort | Lost on disconnect. To be replaced. |

---

## 6. Test Artifacts & Workloads

### Artifact Manifests
Located in `test-artifacts/`:
- `dotnet-runtime.manifest.json` — **Has real `detection` block** (`type: registry`).
- `7z2600-x64.manifest.json` — Has detection metadata.
- `git`, `nodejs`, `python` manifests — **No `detection` block**. Auto-generated fallback is `version_manifest`.

### Workload Definitions
Located in `test-workloads/`:
- `workloads-newer.json` — Dev Tools Stack (nodejs, python), Runtime Environment (dotnet-runtime), Utility Pack (7zip, git).
- `workloads-older.json` — Older versions for diff testing.

> **Note:** `git` was removed from the Dev Tools Stack in the latest edit to focus on nodejs/python.

---

## 7. Implications for the Rehaul Plan

The other agent proposed a 6-step pull-based polling flow. That flow is **fine for dispatch and status reporting**, but if applied naively the agent will still:
1. Poll and get the job.
2. Run the broken pipeline.
3. Report "success" instantly.
4. **Nothing gets installed.**

### Mandatory Fix for the Detection Layer
**Option A (Recommended for POC simplicity):**
Change `DetectVersionManifestAsync` fallback from `AlreadySatisfied` to `NotPresent`. This is a one-line fix that makes the pipeline functional for packages without real detection metadata.

**Option B:**
Update `ArtifactIngestService.ResolveDetection` to default to a more conservative type, or skip detection and always install.

**Option C:**
Update test artifact manifests to include real `detection` blocks (e.g., `file` with path `C:\Program Files\nodejs\node.exe`), but this is brittle across machines.

---

## 8. Recommended Scope for the Rehaul

### Orchestrator Side
- [ ] **Keep `WorkloadRunEntity`** — do not create a `jobs` table.
- [ ] `GET /api/workloadruns/pending?agent_id=X` — return runs in `Queued` state, joined with package metadata.
- [ ] `PATCH /api/workloadruns/{runId}` — agent claims (`status: InProgress`) and reports final status (`Done` / `Failed`).
- [ ] `POST /api/workloadruns/{runId}/timeline` — step-level status reporting (replaces SignalR `SendMessage`).
- [ ] Keep artifact serving as-is (`GET /api/artifacts/{name}/{version}`), but consider adding `GET /api/artifacts/{packageEntityId}/download` for robustness.

### Agent Side
- [ ] Replace SignalR `AssignRun` handler with a polling loop (HTTP `GET /api/workloadruns/pending` every N seconds).
- [ ] On receiving a run: `PATCH` to claim it, then execute existing `PipelineExecutor`.
- [ ] Replace SignalR status reporting with HTTP `POST` to `/api/workloadruns/{runId}/timeline`.
- [ ] **CRITICAL: Fix `DetectVersionManifestAsync` stub** so packages are actually installed.

### UI Side
- [ ] "Deploy" button creates `WorkloadRunEntity` (already works).
- [ ] Add job status view polling `GET /api/workloadruns?agent_id=X`.

---

## 9. Verification Order

1. **Fix `DetectVersionManifestAsync` stub.** Verify `PipelineExecutor` runs `InstallOrUpgrade` for a single package.
2. **Implement polling dispatch.** Orchestrator endpoint + agent loop. Verify end-to-end with stubbed installer.
3. **Implement HTTP status reporting.** Verify UI reflects real-time progress.
4. **Test with real installer.** `msiexec`, `exe`, etc. on Windows, handling UAC elevation.

---

## 10. What NOT to Do

- ❌ Do NOT create a parallel `jobs` table — use `WorkloadRunEntity`.
- ❌ Do NOT rewrite the agent in Python — it's a .NET `IHostedService`.
- ❌ Do NOT scrap `PipelineExecutor` or the step architecture — it's well-structured and tested. The bug is a **stub fallback**, not the architecture.
- ❌ Do NOT keep SignalR as the primary dispatch mechanism — it's the source of queuing/reconnection bugs.
- ❌ Do NOT assume the pipeline works — **it silently no-ops right now**.

---

## 11. Key Files

- `apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs`
- `apps/orchestrator/backend/Services/WorkloadRunDispatcher.cs`
- `apps/orchestrator/backend/Runtime/NodeWorkloadStateService.cs`
- `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`
- `apps/orchestrator/backend/Controllers/ArtifactsController.cs`
- `apps/orchestrator/backend/Data/InstallerDbContext.cs`
- `apps/agent/backend/Services/AgentRuntimeService.cs`
- `apps/agent/backend/Pipeline/PipelineExecutor.cs`
- `apps/agent/backend/Steps/PreCheckProbe.cs`
- `apps/agent/backend/Steps/InstallOrUpgrade.cs`
- `apps/agent/backend/Steps/AcquireArtifact.cs`
- `apps/agent/backend/PackageDetector.cs` (or equivalent detection logic)
- `test-workloads/workloads-newer.json`
- `test-workloads/workloads-older.json`

---

*Last updated: 2026-04-27*
