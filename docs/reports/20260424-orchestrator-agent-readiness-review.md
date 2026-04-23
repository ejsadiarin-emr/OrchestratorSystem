# Orchestrator to Agent Readiness Review

**Date:** 2026-04-24
**Scope:** `apps/orchestrator/backend/`, `apps/orchestrator/web/`, `apps/agent/backend/`, `docs/`
**Purpose:** In-depth readiness review for Agent connecting to Orchestrator and displaying status in the Orchestrator web UI.

---

## Part 1 — Orchestrator Backend Audit Report

### Executive Summary

The Orchestrator backend has a **functionally complete but insecure enrollment flow and a partially complete agent connectivity layer**. Agents can enroll, connect via SignalR, send heartbeats, and receive work assignments. However, **critical gaps exist in authentication, offline detection, and real-time frontend integration** that prevent production readiness.

---

### 1. SignalR Hub — AgentRuntimeHub.cs

**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs`

| Aspect | Finding |
|--------|---------|
| **Endpoint** | Mapped at `/hubs/agent` (Program.cs:127) |
| **Agent → Hub methods** | `Identify(Guid nodeId)` (line 25) and `SendMessage(MessageEnvelope envelope)` (line 41) |
| **Hub → Agent pushes** | `AssignRun` message sent via `IHubContext` from `WorkloadRunsController` (line 253) to group `node-{nodeId}` |
| **Connection lifecycle** | `OnConnectedAsync` logs only (line 65). `OnDisconnectedAsync` unregisters from `AgentConnectionTracker` (line 82) but **does NOT update node status to Offline** |
| **Group management** | Adds connection to group `node-{nodeId}` upon `Identify` (line 38) |
| **Authentication** | **None.** Any client can connect and call `Identify` with any `nodeId` |

**Critical Issue:** `OnDisconnectedAsync` (line 71) only calls `_connectionTracker.Unregister(Context.ConnectionId)`. It does **not** update the `NodeEntity.Status` from "Online" to "Offline", meaning the dashboard will show nodes as online indefinitely after they disconnect.

---

### 2. Node Controller — NodesController.cs

**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Controllers/NodesController.cs`

| Endpoint | Purpose |
|----------|---------|
| `GET api/nodes` | List all nodes with status, hostname, IP, last seen (line 26) |
| `GET api/nodes/{id}` | Get single node details (line 47) |
| `POST api/nodes` | Manual node registration (line 67) |
| `PUT api/nodes/{id}` | Update node metadata (line 105) |
| `DELETE api/nodes/{id}` | Delete node (line 142) |
| `GET api/nodes/workload-states` | List per-node workload states (line 158) |

**Finding:** There is **no HTTP heartbeat endpoint**. Heartbeats are exclusively SignalR messages (`LeaseHeartbeat`). This means agents that lose SignalR connectivity cannot fall back to HTTP to maintain liveness.

---

### 3. Enrollment Controller — EnrollmentController.cs

**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Controllers/EnrollmentController.cs`

The enrollment flow is **complete and functional**:

1. **Issue Token** — `POST api/nodes/enroll` (line 25)
   - Validates TTL (1–120 min)
   - Creates `EnrollmentTokenEntity` with `SingleUse=true`, `Used=false`
   - Returns token value and expiration

2. **List Tokens** — `GET api/enrollment-tokens` (line 55)

3. **Consume Token** — `POST api/enrollment-tokens/{token}/consume` (line 65)
   - Validates token exists, not already used, not expired (410 Gone if expired)
   - Marks `Used=true`, sets `ConsumedAtUtc`
   - Creates a new `NodeEntity` with `Status="Online"`, `FirstConnectedUtc=now`
   - Captures `Hostname`, `IpAddress`, `OsVersion`, `AgentVersion` from request
   - Links token to node via `ConsumedByNodeId`

**Database constraint:** `EnrollmentTokens.Token` has a unique index (InstallerDbContext.cs:100).

---

### 4. Node Entity / Model & DbContext

**Files:**
- `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Data/Entities/NodeEntity.cs`
- `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Data/InstallerDbContext.cs`

**NodeEntity fields (line 3–18):**
- `NodeId` (PK)
- `AgentId` — **nullable, unused in enrollment/connection flow**
- `Hostname` — unique index (DbContext.cs:83)
- `IpAddress`, `Description`, `AgentVersion`, `OsVersion`
- `Status` — check constraint restricts to `'Offline'|'Online'` (DbContext.cs:93)
- `LastSeenUtc`, `FirstConnectedUtc`

**Finding:** The `AgentId` field (line 6) is never populated during enrollment or SignalR identification. The system uses `NodeId` as the authoritative agent identifier everywhere.

---

### 5. Node Services — Runtime Layer

#### NodeWorkloadStateService.cs
**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Runtime/NodeWorkloadStateService.cs`

Processes agent messages via `ProcessMessageAsync` (line 22):

| Message Type | Handler | Behavior |
|-------------|---------|----------|
| `AckClaim` | `HandleAckClaimAsync` | Marks workload run as "Running", upserts node workload state |
| `StepStatus` | `HandleStepStatusAsync` | Updates package-level state JSON, adds timeline entry |
| `Complete` | `HandleCompleteAsync` | Marks run "Completed", adds timeline |
| `Fail` | `HandleFailAsync` | Marks run "Failed", adds timeline |
| `LeaseHeartbeat` | `HandleLeaseHeartbeatAsync` (line 146) | **Updates `node.LastSeenUtc = DateTime.UtcNow` and `node.Status = "Online"`** |

**Finding:** `LeaseHeartbeat` correctly updates liveness. However, there is **no background service or timer** that scans for nodes whose `LastSeenUtc` has exceeded a threshold and marks them "Offline."

#### AgentConnectionTracker.cs
**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Runtime/AgentConnectionTracker.cs`

- Simple in-memory `ConcurrentDictionary` mapping `nodeId ↔ connectionId`
- `Register` / `Unregister` / `TryGetConnectionId`
- **No TTL, no stale-connection sweeper.** If an agent crashes without sending a disconnect frame, the tracker entry is orphaned until the next process restart (though the DB status is what the UI reads, not the tracker).

---

### 6. Program.cs — Configuration

**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Program.cs`

| Concern | Configuration | Lines |
|---------|---------------|-------|
| **CORS** | `AllowAnyMethod()`, `AllowAnyHeader()`. Origins configurable via `Cors:AllowedOrigins`; if empty, falls back to `AllowAnyOrigin()` | 20–44 |
| **SignalR** | `builder.Services.AddSignalR()` — basic, no auth, no scale-out | 71 |
| **Authentication** | **None configured.** No `AddAuthentication`, `AddAuthorization`, `JwtBearer`, or API key | — |
| **Authorization** | **None.** No `[Authorize]` attributes on controllers or hub | — |
| **Hub route** | `app.MapHub<AgentRuntimeHub>("/hubs/agent")` | 127 |
| **Health checks** | `app.MapHealthChecks("/health")` | 128 |

---

### 7. DTOs / Contracts

**Files:**
- `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Contracts/Api/EnrollmentTokenResponse.cs`
- `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Contracts/Api/NodeListResponse.cs`
- `/home/ejs/DeploymentPoC/shared/contracts/Runtime/MessageEnvelope.cs`
- `/home/ejs/DeploymentPoC/shared/contracts/Runtime/MessageTypes.cs`

**Enrollment contracts** are well-defined:
- `IssueEnrollmentTokenRequest` (TTL, requestedBy, orchestratorUrl)
- `ConsumeEnrollmentTokenRequest` (Hostname, IpAddress, OsVersion, AgentVersion)
- `EnrollmentTokenResponse`

**Runtime protocol** (`MessageEnvelope`):
- `MessageType`, `ProtocolVersion`, `MessageId`, `TimestampUtc`, `AssignmentId`, `LeaseId`, `RunId`, `AgentId`, `Sequence`, `Payload`

**Message types supported:**
`AssignRun`, `AckClaim`, `LeaseHeartbeat`, `StepStatus`, `Complete`, `Fail`, `LeaseClose`

---

### 8. Workload Dispatch (How Agents Get Work)

**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`

When a workload run is created (`POST api/workload-runs`, line 35):
1. Persists `WorkloadRunEntity` rows per target node
2. Builds `AssignRunPayload` with package assignments
3. Sends `AssignRun` message via SignalR to group `node-{runEntity.NodeId}` (line 253)

**This is functional, but depends entirely on the agent having already called `Identify` on the hub to join its group.**

---

### 9. Known TODOs / Stubs

| File | Line | Issue |
|------|------|-------|
| `Controllers/AgentDownloadController.cs` | 28 | **TODO:** Validate enrollment token exists and is unused before allowing agent download |
| `Services/ArtifactIngestService.cs` | 280 | TODO: Replace with real template resolution (unrelated to agent connectivity) |

No `NotImplementedException` or stubbed methods were found in the agent connection path.

---

### 10. Frontend Integration Gap

**File:** `/home/ejs/DeploymentPoC/apps/orchestrator/web/src/services/realtime.ts`

The frontend **does not use SignalR**. It implements `subscribeToRunProgress` as a **polling loop** (`setInterval` every 1,200ms) calling `advanceWorkloadRun` (which hits an HTTP endpoint or returns mock data).

**Impact:** The frontend will not receive real-time node status updates when agents connect/disconnect. It must poll `GET /api/nodes` to see status changes.

---

## Part 2 — Orchestrator Frontend Audit Report

### Executive Summary
The frontend has the **basic UI structure** for node enrollment and status viewing, but it is **not ready for production real-time agent connection status**. The Nodes page exists and can generate enrollment tokens, yet it lacks live updates. The Dashboard shows node KPIs and a live table, but its home-data function is still mocked. Most critically, there is **zero SignalR/websocket integration** despite a live `AgentRuntimeHub` on the backend.

---

### 1. Nodes Page — Node Listing & Enrollment Token UI

**File:** `src/pages/Nodes.tsx` (260 lines)

#### What exists
- **Node table** (lines 219–257) showing:
  - `hostname`, `ipAddress`, `status`, `firstConnectedAt`, `osVersion`, `agentVersion`, `lastSeenAt`
- **Enrollment token issuance form** (lines 96–141) with fields for `orchestratorUrl`, `requestedBy`, `ttlMinutes`
- **Token consumption / "first connect" simulator** (lines 144–176)
- **Enrollment tokens table** (lines 179–217) showing token value, URL, expiration, and used/consumed state

#### Gaps & Issues
- **No polling or auto-refresh** — data is fetched once on mount (`useEffect` line 29–33). A user must manually refresh the browser to see a new node appear after enrollment.
- **No delete/edit node actions** — the backend supports `PUT /api/nodes/{id}`, `DELETE /api/nodes/{id}`, and `POST /api/nodes`, but the frontend only calls `GET /api/nodes`.
- **Status is plain text** — there is no visual badge coloring for `online` vs `offline` vs `installing` in the Nodes page table (line 242 just prints `{node.status}`).

---

### 2. Dashboard — Node Status Overview

**File:** `src/pages/Dashboard.tsx` (743 lines)

#### What exists
- **KPI strip** (lines 232–252) with `nodesOnline`, `nodesOffline`, `runningWorkloads`, etc.
- **"Nodes Live Table"** (lines 256–330) showing:
  - `health` (`online`/`warning`/`offline`)
  - `lastCheckInAge`
  - `revisionUpdateAvailable`, `packageUpdatesAvailable`
  - `riskLevel`, `reasonCode`
- **Auto-refresh every 15 seconds** (lines 92–100 via `setInterval`)
- **Node detail modal** (lines 455–565) with health chips, workload signals, mini logs, and action buttons

#### Gaps & Issues
- **`getOrchestratorHomeData` is entirely mocked** (`src/services/api.ts` lines 1249–1251). It returns `structuredClone(orchestratorHome)` instead of calling a real endpoint. The dashboard will never reflect actual backend state until this is wired to a real API.
- The action buttons in the Action Panel (lines 407–411) and inside the node modal (lines 556–561) are **visual stubs** — they have no `onClick` handlers wired to API calls.

---

### 3. API Client / Service Layer

**File:** `src/services/api.ts` (1312 lines)

#### Real HTTP endpoints (backend-connected)
| Function | Endpoint | Notes |
|---|---|---|
| `listNodes` | `GET /api/nodes` | Real |
| `issueEnrollmentToken` | `POST /api/nodes/enroll` | Real |
| `listEnrollmentTokens` | `GET /api/enrollment-tokens` | Real |
| `consumeEnrollmentToken` | `POST /api/enrollment-tokens/{token}/consume` | Real |
| `listWorkloads` | `GET /api/workloads` | Real |
| `createWorkloadDefinitionDraft` | `POST /api/workloads` | Real |
| `listWorkloadRuns` | `GET /api/workload-runs` | Real |
| `createWorkloadRun` | `POST /api/workload-runs` | Real |
| `cancelWorkloadRun` | `POST /api/workload-runs/{id}/cancel` | Real |
| `uploadArtifact` | `POST /api/artifacts` | Real |
| `listArtifacts` | `GET /api/artifacts` | Real |
| `deleteArtifact` | `DELETE /api/artifacts/{pkg}/{ver}` | Real |

#### Mock / stub endpoints (not connected to backend)
| Function | Lines | Issue |
|---|---|---|
| `getOrchestratorHomeData` | 1249–1251 | Returns hard-coded mock data |
| `getDashboardSummary` | 1235–1243 | Returns hard-coded mock data |
| `listAuditEvents` | 1245–1247 | Returns hard-coded mock data |
| `getAgentLocalSummary` | 1253–1257 | Returns hard-coded mock data |
| `runAgentPrecheck` | 1259–1264 | Always returns `passed: true` |
| `startAgentGuidedUpdate` | 1266–1273 | Mutates local mock object only |
| `exportAgentDiagnostics` | 1275–1280 | Returns mock filename |
| `listAgentLocalLogs` | 1282–1284 | Returns hard-coded logs |
| `advanceWorkloadRun` | 1151–1217 | Mutates local mock array only |

---

### 4. Types / TypeScript Definitions

**File:** `src/types.ts` (330 lines)

#### Status
- Clean and complete. Key types:
  - `Node` (lines 88–98) — matches backend `Node` contract
  - `EnrollmentToken` (lines 70–78) — matches backend `EnrollmentTokenResponse`
  - `NodeStatus` (line 86) — `'online' | 'offline' | 'installing' | 'enrolling' | 'unknown'`
  - `DashboardNodeRow`, `DashboardKpiSummary`, `NodeHealth`, `NodeRunState` — all present

#### Type safety check
- `tsc --noEmit` passes **cleanly** (zero errors).
- All 49 vitest tests pass.

---

### 5. SignalR / Real-Time Connection

**Backend hub:** `apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs`
**Backend route:** `/hubs/agent` (`Program.cs` line 127)

#### Frontend status
- **No SignalR client library** in `package.json` (no `@microsoft/signalr`).
- **No websocket/EventSource/Socket.io code** anywhere in `src/`.
- **File:** `src/services/realtime.ts` (43 lines) — this is **not** a SignalR connection. It is a `setInterval` poller that calls `advanceWorkloadRun` (a mock function) every 1.2 seconds for workload run timeline simulation.

#### Implication
The backend can push agent connection events in real-time, but the frontend is completely deaf to them. Node online/offline transitions depend on the hub's `Identify` and `OnDisconnectedAsync` handlers updating the database; the frontend will only learn about them via polling.

---

### 6. Routing & Navigation

**File:** `src/App.tsx`

#### Accessible routes
- `/` → Dashboard
- `/nodes` → Nodes
- `/workloads` → Workloads
- `/workload-runs` → WorkloadRuns
- `/artifacts` → ArtifactStore
- `/packages` → ArtifactStore (duplicate)
- `/agent-local` → AgentLocal

#### Issues
- **Orphan pages** — `src/pages/CommandCenter.tsx` and `src/pages/Install.tsx` exist but are **not routed** in `App.tsx`. They are unreachable.
- **Sidebar mismatch** — `Sidebar.tsx` links to `/artifacts`, but `Layout.tsx` `pageTitles` only defines `/packages`. When on `/artifacts`, the topbar title falls back to `"Orchestrator"` instead of `"Artifact Packages"`.

---

### 7. Broken Imports / Missing Pages / Stubbed Components

| Issue | File | Line | Details |
|---|---|---|---|
| No broken imports | — | — | All imports resolve correctly |
| Orphan `CommandCenter` | `src/pages/CommandCenter.tsx` | — | Not in `App.tsx` routes |
| Orphan `Install` | `src/pages/Install.tsx` | — | Not in `App.tsx` routes |
| Stubbed dashboard actions | `src/pages/Dashboard.tsx` | 407–411 | Buttons with no handlers |
| Stubbed node modal actions | `src/pages/Dashboard.tsx` | 556–561 | Buttons with no handlers |
| Mock home data | `src/services/api.ts` | 1249 | `getOrchestratorHomeData` is mocked |

---

## Part 3 — Agent Backend Readiness Audit Report

**Project:** `/home/ejs/DeploymentPoC/apps/agent/backend/`
**Framework:** .NET 10.0, Self-Contained, Single-File
**Build Status:** Succeeds (0 warnings, 0 errors)
**Platform Tested:** Linux x64

---

### 1. Program.cs / Entry Point

**File:** `Program.cs` (lines 1-101)

**Findings:**
- **CLI parsing** supports `--enroll <token>`, `--orchestrator-url <url>`, and `--reset-enrollment` (lines 15-33).
- **Enrollment flow:** If both `--enroll` and `--orchestrator-url` are provided, it calls `AgentEnrollmentService.ConsumeEnrollmentTokenAsync`, saves `agent.json`, and proceeds to `app.Run()` (lines 46-74).
- **Auto-connect:** If no CLI args, loads persisted config from disk (lines 66-74).
- **WebApplication** is built with:
  - Health endpoint at `/health` (line 94)
  - Static files + fallback to `index.html` (lines 95-97)
  - `AgentRuntimeService` registered as hosted service (line 89)

**Issue — Port Collision Risk:**
- No explicit URL configuration (no `--urls`, no `ASPNETCORE_URLS`, no `builder.WebHost.UseUrls()`).
- Default ASP.NET Core binds to `http://localhost:5000`.
- The orchestrator default URL is also `http://localhost:5000` (`appsettings.json` line 9).
- **If agent and orchestrator run on the same machine, they will conflict on port 5000.**

---

### 2. SignalR Connection Lifecycle

**File:** `Services/AgentRuntimeService.cs` (lines 26-76)

**Findings:**
- **Connection setup:** `HubConnectionBuilder` targets `/hubs/agent` (line 29).
- **Authentication:** `AccessTokenProvider` returns hardcoded `"placeholder-token"` (line 37). **This is a stub and will fail any real auth.**
- **Reconnection:** Uses `WithAutomaticReconnect()` (line 39) but:
  - No custom retry delays configured (defaults to 0s, 2s, 10s, 30s).
  - No `OnReconnecting` or `OnReconnected` event handlers.
  - **Critical:** After reconnection, the agent does **not** re-send `Identify`. If the orchestrator drops connection state, the agent becomes an anonymous connection and won't receive targeted `AssignRun` messages.

**Missing:**
- `OnClosed` handler with manual restart logic for unrecoverable disconnects.
- Re-identify after reconnect.

---

### 3. Enrollment Flow

**File:** `Services/AgentEnrollmentService.cs` (lines 1-98)

**Findings:**
- **HTTP token consumption:** `POST /api/enrollment-tokens/{token}/consume` (line 19).
- **Error handling:** Properly handles `410 Gone` (expired), `409 Conflict` (consumed), `404 NotFound`, and generic failures (lines 22-41).
- **Response parsing:** Expects JSON with `id` property parsed as `Guid` (lines 43-50).
- **Config persistence:**
  - Windows: `%LOCALAPPDATA%\DeploymentPoC\agent.json` (lines 56-59)
  - Linux: `/var/lib/deploymentpoc/agent.json` (line 62)

**Issue — Directory Permissions (Linux):**
- `/var/lib/deploymentpoc` is a system path. If the agent runs as a non-root user, `SaveConfig` will throw `UnauthorizedAccessException` unless the directory is pre-created with correct permissions. There is no permission setup logic.

---

### 4. Heartbeat Logic

**Verdict:** NOT IMPLEMENTED

**File:** `Services/AgentRuntimeService.cs`

**Findings:**
- No periodic heartbeat sender.
- `MessageTypes.LeaseHeartbeat` exists in contracts (`shared/contracts/Runtime/MessageTypes.cs` line 7) but is never used.
- The orchestrator likely expects `LeaseHeartbeat` to keep the assignment lease alive. Without it, the orchestrator may time out and reassign the run to another agent.

**Missing:**
- Background timer/loop to invoke `SendMessage` with `LeaseHeartbeat` at regular intervals (e.g., every 10-30s).

---

### 5. Workload Execution

**File:** `Pipeline/PipelineExecutor.cs` (lines 1-187)

**Findings:**
- **Receives `AssignRun`** and filters by `NodeId` (lines 91-98).
- **Sends `AckClaim`** immediately after accepting the run (lines 106-118).
- **Pipeline steps (per package):**
  1. `AcquireArtifact` — downloads artifact with chunked/resumable support, SHA256 verification, path traversal hardening (lines 42-70).
  2. `InstallOrUpgrade` — executes command with `{artifactPath}` substitution, timeout, exit code validation (lines 71-89).
  3. `PostInstallVerify` — file existence/version check or registry stub (lines 90-109).
- **Step status reporting:** Sends `StepStatus` after each step (lines 115-147).
- **Finalization:** Sends `MessageTypes.Complete` or `MessageTypes.Fail` via `FinalizeAsync` (lines 149-186).
- **Error handling:** Pipeline halts on first failure and finalizes with `Fail`.

**Issue — Best-Effort Reporting:**
- `SendStepStatusAsync` and `FinalizeAsync` swallow all send exceptions silently (lines 143-146, 175-178). If the SignalR connection drops mid-run, the orchestrator may never hear the final status.

---

### 6. Message Types

**Contract:** `shared/contracts/Runtime/MessageTypes.cs`

| Type | Sent By Agent | Received By Agent | Status |
|------|---------------|-------------------|--------|
| `AssignRun` | No | Yes | Implemented |
| `AckClaim` | Yes | No | Implemented |
| `LeaseHeartbeat` | No | No | **Not Implemented** |
| `StepStatus` | Yes | No | Implemented |
| `Complete` | Yes | No | Implemented |
| `Fail` | Yes | No | Implemented |
| `LeaseClose` | No | No | Not Implemented |

**Missing senders:** `LeaseHeartbeat`, `LeaseClose`.

---

### 7. Artifact Download & Execution

**File:** `Steps/AcquireArtifact.cs` (lines 1-429)

**Findings:**
- **Range request support:** Downloads in 8MB chunks with `Range` headers and validates `Content-Range` (lines 94-147).
- **Fallback:** If server returns `200 OK` instead of `206 PartialContent`, falls back to full download (lines 133-138).
- **Security:**
  - URI validation (scheme must be `http`/`https`) (lines 246-250).
  - Optional allowed-hosts whitelist.
  - Destination path canonicalization and symlink traversal check (lines 262-297).
  - SHA256 hash verification (lines 218-228).
- **Cleanup:** Deletes partial file on any failure.

**File:** `Steps/InstallOrUpgrade.cs` (lines 1-91)

**Findings:**
- Spawns process with configurable command, arguments, timeout (default 300s).
- Handles `Win32Exception` for missing commands (error code 2).
- Captures stderr on non-zero exit.

**File:** `Steps/PostInstallVerify.cs` (lines 1-101)

**Findings:**
- `file` detection: Checks file existence and `FileVersionInfo` for version matching.
- `registry` detection: **Stubbed** — returns `Success = true` with comment `// PoC Phase 1: registry detection is a stub.` (lines 70-75).

---

### 8. Configuration & Build

**File:** `DeploymentPoC.Agent.csproj`

**Findings:**
- SelfContained=true
- PublishSingleFile=true
- IncludeNativeLibrariesForSelfExtract=true
- References `Microsoft.AspNetCore.SignalR.Client` (9.0.4)
- Platform-specific hosting packages included (`Systemd`, `WindowsServices`)
- `wwwroot/**` referenced for publish but directory does not exist (harmless but may warn during publish).

**Windows-Specific Dependencies:**
- `Microsoft.Extensions.Hosting.WindowsServices` is referenced but only used when `OperatingSystem.IsWindows()` is true (line 26-27, `HostPlatformConfiguration.cs`). This is safe on Linux.
- `Win32Exception` handling in `InstallOrUpgrade.cs` exists on all .NET platforms; safe.
- `FileVersionInfo.GetVersionInfo` is cross-platform for PE files but may throw on non-Windows binaries without version resources.

---

### 9. Critical Path Gaps & TODOs

| # | Issue | File | Line(s) | Severity |
|---|-------|------|---------|----------|
| 1 | **No heartbeat sender** | `AgentRuntimeService.cs` | N/A | Critical |
| 2 | **Hardcoded auth token** | `AgentRuntimeService.cs` | 37 | Critical |
| 3 | **No re-identify after reconnect** | `AgentRuntimeService.cs` | 39 | High |
| 4 | **Port collision with orchestrator** | `Program.cs` | 76-99 | High |
| 5 | **Registry detection stubbed** | `PostInstallVerify.cs` | 70-75 | High |
| 6 | **No LeaseClose on shutdown/complete** | `PipelineExecutor.cs` | N/A | High |
| 7 | **Silent send failures** | `PipelineExecutor.cs` | 143, 175 | Medium |
| 8 | **Linux config dir permissions** | `AgentEnrollmentService.cs` | 62 | Medium |
| 9 | **Missing `wwwroot` directory** | `.csproj` | 33 | Low |

---

## Part 4 — Documentation & Contracts Audit Report

### Executive Summary

The documentation describes a robust, security-conscious runtime protocol with mTLS identity, lease management, sequence idempotency, stale-assignment detection, and config snapshot safety. The actual implementation covers the **happy-path demo scenario** (connect -> identify -> receive AssignRun -> execute pipeline -> report completion) but **omits or stubs most of the resilience, security, and correctness mechanisms** specified in the contracts. Several tracker tasks marked `Done` have significant residual gaps when checked against the normative documentation.

---

### 2. Contract Specification Analysis

#### 2.1 Enrollment & Authentication

| Requirement | Source Document | Specified Behavior |
|---|---|---|
| Enrollment token | `10-core-contracts-pack.md` section 5, `09-security-pack.md` section 2 TH-001 | Single-use, short-lived token consumed via `POST /api/enrollment-tokens/{token}/consume` |
| Steady-state auth | `09-security-pack.md` section 3 M-001, `adr/ADR-006-security-baseline.md` | After enrollment, per-agent **mTLS certificate identity** bound to persistent agent ID. Reconnect without valid bound cert is rejected. |
| Token error handling | `04-agent-bootstrap-and-communication.md` section 1.5 | 410 (expired), 409 (consumed), 404 (missing) with explicit non-zero exit |
| Auth token in SignalR | `04-agent-bootstrap-and-communication.md` section 2.1 | `AccessTokenProvider` must supply valid identity token |

**Implementation Reality:**
- `EnrollmentController.cs` (lines 25-127) implements token generation and consumption correctly, returning 410/409/404 as specified.
- `AgentEnrollmentService.cs` (lines 17-51) implements the HTTP client for token consumption with correct error handling for 410/409/404.
- `AgentEnrollmentService.cs` (lines 53-63) implements cross-platform config persistence at `%LOCALAPPDATA%/DeploymentPoC/agent.json` (Windows) and `/var/lib/deploymentpoc/agent.json` (Linux).
- **CRITICAL GAP:** `AgentRuntimeService.cs` (line 37) hardcodes `AccessTokenProvider = () => Task.FromResult("placeholder-token")`. There is **no mTLS, no JWT, no certificate exchange, and no bound identity verification** on the SignalR connection. The Security Pack (`09-security-pack.md`) explicitly tags this as `[PoC Phase 1]` required (M-001, TH-001), but it is unimplemented.

#### 2.2 Heartbeat Frequency & Lease Policy

| Requirement | Source Document | Specified Behavior |
|---|---|---|
| Lease TTL | `10-core-contracts-pack.md` section 5, `03-architecture-and-design.md` section 4.1 | `90s` |
| Heartbeat interval | Same sources | `15s` |
| Stale threshold | Same sources | `3` missed heartbeats |
| Stale timeout bound | Same sources | Auto-fail with `lease_timeout_exhausted` after 2 reassignment attempts or 15 minutes total stale duration |
| Lease entity | `10-core-contracts-pack.md` section 2 | `AssignmentLease` with `assignmentId`, `leaseId`, `ttlSeconds`, `lastHeartbeatUtc`, `lastAckedSequence` |

**Implementation Reality:**
- `AssignmentLeaseEntity.cs` exists in schema with `TtlSeconds = 90` and correct fields.
- **CRITICAL GAP:** No `LeaseManager.cs`, no `LeaseTimeoutWorker.cs`, no background scanner for expired leases. The entity table is unused.
- The agent **never sends `LeaseHeartbeat`** during pipeline execution. `AgentRuntimeService.cs` only sends `AckClaim`, `StepStatus`, `Complete`, and `Fail`. There is no heartbeat loop.
- `NodeWorkloadStateService.cs` (lines 146-158) handles `LeaseHeartbeat` messages by updating `node.LastSeenUtc`, but since the agent never sends them, this code path is effectively dead.
- The orchestrator does **not** transition runs to `AssignedStale` or auto-fail with `lease_timeout_exhausted`.

#### 2.3 Message Protocol & Envelope

| Requirement | Source Document | Specified Behavior |
|---|---|---|
| Shared envelope | `10-core-contracts-pack.md` section 5 | `messageType`, `protocolVersion`, `messageId`, `timestampUtc`, `sequence` always required; `assignmentId`, `leaseId`, `runId`, `agentId` required when relevant |
| Idempotency key | `10-core-contracts-pack.md` section 5, `poc-phase1-prd-final.md` section Core contracts | Upsert keyed by `(runId, nodeId, packageId, stepId, sequence)`. Same-key payload mismatch -> reject + audit `sequence_payload_conflict`. Stale/out-of-order rejected. Reconnect resumes from `lastAcknowledgedSequence + 1`. |
| Canonical sequence | `10-core-contracts-pack.md` section 5, `0005-signalr-runtime-protocol.md` | `Connect -> Register/Authenticate -> AssignRun -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose` |

**Implementation Reality:**
- `MessageEnvelope.cs` implements the envelope structure correctly with `ProtocolVersion = "1.0"`.
- `MessageTypes.cs` defines all 7 canonical message types.
- **CRITICAL GAP:** No sequence/idempotency enforcement exists anywhere in the orchestrator backend. `NodeWorkloadStateService.cs` processes `StepStatus` without checking:
  - Whether the sequence is monotonic
  - Whether `(runId, stepId, sequence)` has been seen before
  - Whether the payload hash matches a previously accepted update
  - There is no `sequence_payload_conflict` audit event
- `NodeWorkloadStateService.cs` (line 89) creates timeline entries with `Sequence = envelope.Sequence`, but does not reject duplicates or out-of-order messages.
- There is no `lastAcknowledgedSequence` tracking in the database or in-memory.
- `LeaseClose` message type exists but is **never sent or handled** by either side.

---

### 3. Phase 1 Implementation Tracker Analysis

Source: `docs/implementation-tracker-phase1.md`

#### Tasks Marked `Done` with Residual Gaps

| Task | Status | Claimed Completion | Actual Gap |
|---|---|---|---|
| **W3-01** Windows agent service scaffold + runtime loop | `Done` | SignalR client connects with auto-reconnect; `AssignRun` handler parses payload and sends `AckClaim` | Auth is `placeholder-token`. No heartbeat. No `LeaseClose`. No sequence resume. |
| **W3-02a** Enrollment token generation + agent download endpoint | `Done` | Token generation and consumption APIs exist | Agent download endpoint (`AgentDownloadController.cs`) serves placeholder - the actual binary packaging and signing is not verified. |
| **W3-03** Agent workload pipeline | `Done` | Pipeline halts on failure, emits `StepStatus`, `Complete`, `Fail` | **Missing `PreUpgradeActions` enforcement** (W2-04b dependency). No config snapshot before mutation. No artifact signature validation in pipeline. |
| **W3-04** Node workload state persistence/reporting | `Done` | NodeWorkloadStateService persists state and timeline | Timeline persistence works, but **no lease expiration handling**, **no stale state transitions**, and **no sequence conflict detection**. |
| **W2-01** Runtime contract updates (`AssignRun`) | `Done` | `AssignRunPayload`, `PackageAssignment`, `InstallAdapterConfig`, `DetectionConfig` all exist | `AssignRunPayload.PreUpgradeActions` is always populated as empty list in `WorkloadRunsController.cs` (line 241). The field exists but is never used. |
| **W2-04a** Policy engine - risk detection | `Done` | Risk level evaluation produces low/medium/high | Works correctly for artifact ingest risk elevation, but risk does not block or gate execution (by design per PRD). |

#### Tasks Marked `Not Started` (Confirmed Missing)

| Task | Impact |
|---|---|
| **W0-02** Contract freeze and migration map | Legacy `job` terminology still mixed in `AssignmentLeaseEntity` (field `JobId`), `JobEntity`, and `ConfigSnapshotEntity` (field `JobId`). |
| **W2-02** Sequence/idempotency enforcement | **Blocks AC-003 acceptance.** No replay protection, no out-of-order rejection. |
| **W2-03** Lease manager + stale policy | **Blocks AC-101 acceptance.** No lease expiration, no `AssignedStale`, no auto-fail. |
| **W2-04b** Policy engine - preUpgradeActions enforcement | **Blocks AC-007 partially.** `PreUpgradeActions` list exists in payload but is never executed. |
| **W3-02** Bootstrap token -> mTLS steady-state auth | **Blocks AC-102 acceptance.** No certificate exchange, no identity binding. |
| **W3-02b** Agent CLI enrollment (`--enroll`, `--reset-enrollment`) + config persistence | **Partially implemented since 2026-04-23 audit.** `Program.cs` now parses CLI args, `AgentEnrollmentService` persists `agent.json`. However, there are no integration tests (W8-02a still blocked). |
| **W4-01** Config snapshot/migration/restore | **Blocks AC-007.** `ConfigSnapshotEntity` exists in schema but no service logic creates or restores snapshots. |
| **W5-01a** Security baseline - RBAC + audit integrity | **Blocks AC-102.** No `RbacService`, no `AuthorizationMiddleware`, no role checks on API endpoints. |
| **W5-01b** Security baseline - trust verification + secret hygiene | **Blocks AC-102.** `PostInstallVerify` is a stub. No artifact signature verification in agent pipeline. |
| **W5-02** Observability stack MVP (OTel + Loki + Grafana) | **Blocks AC-103.** No OTel collector, no Loki, no Grafana integration. |
| **W6-01b** Run timeline + node visibility | **Blocks AC-103, AC-105.** Tracker says `Not Started`, but evidence in `W3-04` suggests basic node workload state API exists. UI run timeline detail may be partial. |
| **W8-01a/b** Integration/E2E/chaos suites | Not started. Manual testing only for demo. |

---

### 4. Expected Message Flow vs Actual Implementation

#### Documented Flow
```
Connect -> Register/Authenticate -> AssignRun -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose
```

#### Actual Flow (2026-04-24 Code Reality)
```
Agent starts
  -> Builds HubConnection to /hubs/agent
  -> Connects (with "placeholder-token")
  -> Invokes Identify(nodeId)  [NOT Register/Authenticate]
  -> Waits for AssignRun message
  -> Receives AssignRun
  -> Sends AckClaim
  -> Executes Pipeline (AcquireArtifact -> InstallOrUpgrade -> PostInstallVerify)
    -> Sends StepStatus after each step
  -> Sends Complete OR Fail
  -> [NO LeaseClose]
  -> [NO LeaseHeartbeat during execution]
  -> [NO heartbeat loop at all]
```

#### Gaps in the Flow
1. **No Register/Authenticate handshake:** The PRD and ADR-007 specify that after connection, the agent must register/authenticate. Instead, the agent calls `Identify(nodeId)`, which merely maps `connectionId -> nodeId`. There is no token consumption over SignalR, no cert validation, and no auth gate.
2. **No LeaseHeartbeat:** The agent sends zero heartbeats during the entire execution. If the agent process crashes after `AckClaim` but before `Complete`, the orchestrator has no mechanism to detect staleness and reassign the run.
3. **No LeaseClose:** Neither the agent nor the orchestrator sends or handles `LeaseClose`. The lease (if it existed) would remain `Assigned` indefinitely in the database.
4. **No resume on reconnect:** If the agent disconnects and reconnects mid-pipeline, it re-executes from `Identify` but does not resume from `lastAcknowledgedSequence + 1`. The orchestrator would create duplicate timeline entries.

---

### 5. Design Decisions (ADRs) and Their Implementation Status

| ADR | Decision | Phase 1 Posture | Implementation Status |
|---|---|---|---|
| **ADR-001** Hybrid control plane | Custom orchestrator + agent | Adopted | Implemented |
| **ADR-002/0003** Agent-initiated connection | Agent initiates SignalR; orchestrator pushes assignments | Adopted | Implemented |
| **ADR-003** Adapter strategy | MSI + EXE first | Adopted | Implemented (typed adapters in agent pipeline) |
| **ADR-004** OpenTelemetry observability | OTel baseline | Adopted | **Not implemented** - no OTel collector, no structured tracing with required correlation fields |
| **ADR-005** Self-contained packaging | Single executable, no preinstalled runtime | Adopted | Partial - orchestrator packaging exists but clean-host validation (AC-105) not proven |
| **ADR-006** Security baseline | Signed artifacts, RBAC, least privilege, audit | Adopted | **Not implemented** - no RBAC, no artifact signature verification at runtime, no audit integrity service |
| **ADR-007/0005** SignalR protocol | Canonical sequencing, idempotency, resume | Adopted | Partial - message types and envelope exist, but **no idempotency enforcement, no resume, no stale handling** |
| **ADR-008** Queue/buffer pattern | Hangfire (orchestrator) + Channel<T> (agent) | Adopted | Partial - orchestrator no longer uses Hangfire for workload runs (uses direct SignalR dispatch); agent uses direct invocation, not Channel<T> queue |
| **ADR-009/0007** Persistent agent model | Windows service, always-on | Adopted | Implemented |
| **ADR-010** WinRM bootstrap | WinRM push for PoC | Adopted | **Superseded** - bootstrap is now browser-based download (per `agent-runtime/CONTEXT.md` and ADR-012 amendment) |
| **ADR-011** Dry-run confidence | Two-phase validation with confidence levels | Adopted | Partial - `PolicyEvaluationService` evaluates risk, but no `IPreCheck` pipeline with confidence levels is executed |
| **ADR-012** Enterprise bootstrap boundary | GPO/SCCM for agent provisioning only; orchestrator governs runtime | Adopted | Documented and followed |
| **ADR-013** Upgrade phasing | Deterministic vN->vN+1 migration | Adopted | **Not implemented** - no migration service |
| **ADR-014** Artifact metadata enrichment | Resolution chain + provenance | Adopted | Implemented |

### Notable ADR Gaps
- **ADR-006 (Security Baseline)** is marked `Adopted` but its core mitigations (M-001 through M-006) are largely unimplemented. There is **no ADR documenting the decision to defer mTLS or RBAC** - the deferral exists only in the implementation tracker (`W3-02`, `W5-01a` marked `Not Started` and listed as `MVP-soft`).
- **ADR-007 (SignalR Protocol)** is marked `Adopted` but the agent does not implement the full canonical sequence. The ADR explicitly states: "reconnect uses resume handshake with last acknowledged sequence." This is absent.

---

### 6. Detailed Gap Matrix: Documented Intent vs Code

| Document | Section | Intent | Code Reality | Severity |
|---|---|---|---|---|
| `10-core-contracts-pack.md` | section 5 (Lease defaults) | TTL 90s, heartbeat 15s, stale after 3 misses, auto-fail after 2 reassignments or 15 min | `AssignmentLeaseEntity` exists in schema. No manager, no worker, no stale transition. | High |
| `10-core-contracts-pack.md` | section 5 (Idempotency rule) | Upsert by `(jobId, stepId, sequence)`; reject same-key payload mismatch as `sequence_payload_conflict` | No idempotency check in `NodeWorkloadStateService.cs`. Timeline table has no unique constraint on `(RunId, NodeId, Sequence, StepName)`. | High |
| `10-core-contracts-pack.md` | section 5 (Message envelope) | `assignmentId`, `leaseId`, `jobId`, `agentId` required when relevant | `MessageEnvelope` includes optional fields, but `WorkloadRunsController.cs` sends `AssignRun` with `Sequence = 0`, no `AssignmentId`, no `LeaseId`. | Medium |
| `03-architecture-and-design.md` | section 4.1 (Reconnection) | Resume handshake from `lastAcknowledgedSequence + 1` | No `lastAcknowledgedSequence` stored. Agent does not request resume. | High |
| `03-architecture-and-design.md` | section 6 (State machine) | `AssignedStale` is a valid state; transitions to `Failed` with `lease_timeout_exhausted` | `WorkloadRunEntity.State` check constraint allows only `Queued, Running, Completed, Failed, Cancelled`. `AssignedStale` is not in the database schema. | High |
| `04-agent-bootstrap-and-communication.md` | section 2.3 (Connection lifecycle) | `LeaseHeartbeat every 15s` | Agent never sends heartbeats. | High |
| `04-agent-bootstrap-and-communication.md` | section 2.1 (SignalR auth) | mTLS per-agent identity after enrollment | `placeholder-token` hardcoded. | High |
| `05-orchestration-and-validation.md` | section 2.3 (Lease policy) | Reassignment guard: replay-safe checkpoint + no active prior heartbeat in grace window + mutation checkpoint consistency | No reassignment logic exists. Runs are created as `Queued` and transitioned to `Running` on `AckClaim` only. | High |
| `09-security-pack.md` | section 2 TH-001 | Mitigation: enrollment token + per-agent mTLS identity + cert validation | Token consumption exists. mTLS and cert validation do not. | High |
| `09-security-pack.md` | section 2 TH-002 | Artifact signature + checksum verification before execution | `ArtifactIngestService` validates on ingest (with `warn`/`fail`). Agent pipeline does **not** verify signature before `InstallOrUpgrade`. | Medium |
| `11-config-persistence-contract.md` | section 3 (Migration interface) | `IConfigMigration` with deterministic `vN -> vN+1` chain | Interface exists in `agent-runtime/CONTEXT.md` only. No implementation in agent or orchestrator. | Medium |
| `11-config-persistence-contract.md` | section 4 (Rollback restore) | Trigger: migration failure -> restore from `configSnapshotId` | `ConfigSnapshotEntity` exists in schema. No service creates or restores snapshots. | Medium |
| `poc-phase1-prd-final.md` | section Core contracts (Artifact ingest) | Signature verification `fail` blocks ingest; `warn` elevates risk to `high` | Implemented in `ArtifactIngestService.cs`. | Low |
| `poc-phase1-prd-final.md` | section Core contracts (Workload run APIs) | `POST /api/workload-runs` with idempotency | Implemented in `WorkloadRunsController.cs` with request hash validation and active-run guard. | Low |

---

## Part 5 - Orchestrator to Agent Readiness Review (Summary)

### Bottom Line
**The Agent CAN connect to the Orchestrator and the web UI CAN display its status - but only for a manual, happy-path demo.** It is **not ready for reliable, real-time connection monitoring** due to critical gaps in heartbeat, offline detection, frontend data wiring, and authentication.

---

### What Works Today (Happy Path Demo)

| Step | Status | Evidence |
|------|--------|----------|
| Generate enrollment token in UI | Ready | `Nodes.tsx` has complete token issuance form calling `POST /api/nodes/enroll` |
| Agent consumes token via HTTP | Ready | `AgentEnrollmentService.cs` handles 410/409/404 correctly, persists `agent.json` |
| Agent connects SignalR `/hubs/agent` | Ready | `AgentRuntimeService.cs` builds `HubConnection`, calls `Identify(nodeId)` |
| Orchestrator tracks connection | Ready | `AgentRuntimeHub.cs` registers connection to group `node-{nodeId}` |
| Node appears in UI list | Ready | `Nodes.tsx` calls `GET /api/nodes`, shows `status`, `hostname`, `ipAddress`, etc. |
| Orchestrator pushes work to agent | Ready | `WorkloadRunsController.cs` sends `AssignRun` to SignalR group |
| Agent executes & reports back | Ready | `PipelineExecutor.cs` sends `AckClaim` -> `StepStatus*` -> `Complete`/`Fail` |
| Frontend shows workload runs | Ready | `WorkloadRuns.tsx` lists real runs from `GET /api/workload-runs` |

---

### Critical Blockers for "Ready for Connection"

| # | Issue | Impact | File(s) |
|---|-------|--------|---------|
| **1** | **Agent never sends heartbeats** | Orchestrator cannot detect if agent dies mid-run. Lease expiration, stale detection, and auto-reassignment are all dead code. | `AgentRuntimeService.cs` - no timer loop for `LeaseHeartbeat` |
| **2** | **Node status never goes Offline** | `OnDisconnectedAsync` only unregisters from in-memory tracker. DB `Status` stays "Online" forever. UI will show ghost nodes. | `AgentRuntimeHub.cs:71-82` |
| **3** | **Dashboard uses mocked node data** | The "Nodes Live Table" and KPI strip show hardcoded mock objects, not real backend state. | `api.ts:1249-1251` (`getOrchestratorHomeData`) |
| **4** | **Nodes page has no auto-refresh** | After enrollment, user must manually refresh browser to see the new node. | `Nodes.tsx:29-33` - single `useEffect` fetch |
| **5** | **Zero authentication** | SignalR accepts any `nodeId` in `Identify`. Any client can impersonate any node. | `AgentRuntimeHub.cs`, `AgentRuntimeService.cs:37` |
| **6** | **No frontend SignalR client** | Real-time push updates (node online/offline, run progress) do not reach the UI. | `package.json` lacks `@microsoft/signalr` |
| **7** | **Agent port collides with Orchestrator** | Both default to `:5000`. Running on same machine fails. | `Agent/Program.cs` (no explicit URL) |

---

### Secondary Issues

| Issue | Severity | Detail |
|-------|----------|--------|
| No re-identify after SignalR reconnect | High | Agent uses `WithAutomaticReconnect()` but does not call `Identify` again after reconnection. It becomes anonymous and stops receiving work. |
| Dashboard action buttons are stubs | Medium | "Start Update", "Cancel Run", etc. have no `onClick` handlers wired to APIs. |
| Agent download skips token validation | Medium | `AgentDownloadController.cs:28` has a TODO to validate enrollment token before serving binary. |
| Registry detection stubbed | Medium | `PostInstallVerify.cs:70-75` always returns success for registry checks. |
| Linux config path permission risk | Low | `/var/lib/deploymentpoc/agent.json` may fail for non-root users. |

---

### Readiness Verdict by Concern

| Concern | Verdict | Notes |
|---------|---------|-------|
| **Agent can connect** | Partial | Connects and identifies, but no heartbeat, no auth, no reconnect resilience |
| **Agent can execute work** | Ready | Full pipeline execution works (download -> install -> verify -> report) |
| **Orchestrator tracks status** | Not Ready | Never marks nodes Offline; lease management is schema-only |
| **Web UI shows node list** | Partial | `/nodes` shows real data but requires manual refresh |
| **Web UI shows real-time status** | Not Ready | Dashboard is mocked; no polling/SignalR for live updates |
| **Secure connection** | Not Ready | No auth on SignalR or HTTP APIs |

---

### Recommended Fix Order (to reach "Ready for Connection")

**Priority 1 - Reliability (Must Have)**
1. **Add agent heartbeat loop** in `AgentRuntimeService`: send `LeaseHeartbeat` every 15s while connected.
2. **Add offline detection** in orchestrator: either a background service scanning `LastSeenUtc` or update `OnDisconnectedAsync` to set `Status = "Offline"`.
3. **Wire Dashboard to real API**: replace `getOrchestratorHomeData` mock with a real backend endpoint aggregating node KPIs, or at least poll `GET /api/nodes`.
4. **Add polling to Nodes page**: `setInterval` every 5-10s to refresh the node list.

**Priority 2 - Resilience (Should Have)**
5. **Re-identify on reconnect**: add `OnReconnected` handler to call `Identify(nodeId)` again.
6. **Fix port collision**: set agent to a different default port (e.g., `:5001` or dynamic).
7. **Validate agent download token**: implement the TODO in `AgentDownloadController`.

**Priority 3 - Security (Need Before Production)**
8. **Replace `placeholder-token`**: at minimum, use the enrollment token or a derived JWT as the SignalR access token.
9. **Add `[Authorize]` gates** to SignalR hub and node APIs.

**Priority 4 - Real-Time (Nice to Have)**
10. **Add SignalR client to frontend** (`@microsoft/signalr`) and subscribe to node status changes from `AgentRuntimeHub` if you want true real-time without polling.

---

### Suggested Acceptance Criteria for "Ready"

- [ ] Agent enrolls, connects, and appears in UI within 5 seconds without manual refresh
- [ ] Dashboard shows real node counts (not mock data)
- [ ] Killing the agent process causes its status to flip to `Offline` in UI within 30 seconds
- [ ] Reconnecting the agent restores its `Online` status and ability to receive work
- [ ] Multiple agents can connect simultaneously without port conflicts
- [ ] SignalR connection requires a valid token (no impersonation)

---

*Report compiled on 2026-04-24 from comprehensive audits of all four system layers.*
