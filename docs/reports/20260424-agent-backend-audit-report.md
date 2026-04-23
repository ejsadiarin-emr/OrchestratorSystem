# Agent Backend Audit Report

**Date:** 2026-04-23  
**Scope:** `apps/agent/backend/` reality-checked against `docs/prd-phase1.md` and `docs/implementation-tracker-phase1.md`  
**Purpose:** Assess readiness for implementing W3-02b (Agent CLI enrollment) and W8-02a (Testcontainers integration tests)

---

## Context

### Audit Objective

This audit was requested to assess the agent backend (`apps/agent/backend/`) for the purpose of enabling work on two tracker tasks:

1. **W3-02b — Agent CLI enrollment** (`--enroll`, `--reset-enrollment`) + config persistence
2. **W8-02a — Testcontainers agent enrollment integration tests**

### Document References

| Document | Purpose |
|---|---|
| `docs/prd-phase1.md` | PoC Phase 1 canonical PRD — requirements, acceptance criteria, architecture |
| `docs/implementation-tracker-phase1.md` | Dependency-ordered engineering work tracker with task status |

### PRD-Defined Agent Core Functionality (for reference)

The PRD identifies these core capabilities as the baseline for the agent:

1. Connect to an orchestrator
2. Run as a persistent background service
3. Accept workload (AssignRun) tasks sent by the orchestrator
4. Parse the workload definition
5. Install the packages defined in the workload definition
6. Send status back to orchestrator using SignalR (notify status of each step/package install step/etc. — see defined in PRD)

---

## 1. Executive Summary

The agent backend has a **solid runtime pipeline and SignalR integration** but is **missing critical enrollment and service lifecycle features** required for production-like testing. The core "connect → receive workload → execute packages → report status" loop is implemented and tested. However, **W3-02b (Agent CLI enrollment)** is entirely unimplemented, which blocks **W8-02a (Testcontainers integration tests)** since there is no way for an agent binary to self-enroll and persist identity.

**Verdict:** The agent runtime pipeline is testable in isolation, but end-to-end enrollment and integration tests require W3-02b to be implemented first.

---

## 2. What's Implemented (Reality vs PRD)

| PRD Requirement | Status | Evidence |
|---|---|---|
| **Connect to orchestrator (SignalR)** | ✅ Done | `AgentRuntimeService.cs` builds `HubConnection` to `/hubs/agent` with `.WithAutomaticReconnect()` |
| **Run as persistent background service** | ✅ Done | `HostPlatformConfiguration.cs` uses `UseWindowsService()` / `UseSystemd()`; `AgentRuntimeService` is an `IHostedService` |
| **Accept workload (AssignRun) tasks** | ✅ Done | `AgentRuntimeService.OnAssignRun` parses payload, validates `RunId`, filters by `NodeId`, sends `AckClaim` |
| **Parse workload definition** | ✅ Done | `ParseAssignRunPayload` handles typed object + `JsonElement`; extracts packages, adapters, detection config |
| **Install packages in order** | ✅ Done | `PipelineExecutor.cs` sorts by `PackageIndex`, runs `AcquireArtifact → InstallOrUpgrade → PostInstallVerify` sequentially |
| **Send status back via SignalR** | ✅ Done | Emits `StepStatus` after each step, `Complete`/`Fail` at pipeline end |
| **Auto-reconnect** | ✅ Done | `.WithAutomaticReconnect()` configured on `HubConnectionBuilder` |
| **Fail-fast on step failure** | ✅ Done | `PipelineExecutor` catches exceptions, calls `FinalizeAsync`, emits `Fail` with step history |
| **AcquireArtifact with range requests** | ✅ Done | 8 MB chunking, SHA-256 verification, path hardening, symlink traversal blocking |
| **NodeId filtering** | ✅ Done | Agent ignores `AssignRun` messages where `payload.NodeId != configured NodeId` |

---

## 3. What's Missing (Blocking W3-02b and W8-02a)

| Tracker Task | PRD Ref | Gap | Impact |
|---|---|---|---|
| **W3-02b** — Agent CLI enrollment | FR-004, AC-005 | No `--enroll`, `--orchestrator-url`, `--reset-enrollment` argument parsing in `Program.cs`. No `AgentEnrollmentService`. No `agent.json` config persistence. | **Blocks all enrollment testing.** Agent cannot self-register without manual config injection. |
| **W3-02b** — Config persistence | FR-004, AC-005 | `Agent:NodeId` is read from `IConfiguration` (appsettings/env) but never persisted to disk. No `agent.json` model or read/write logic. | Agent identity is ephemeral. Reinstall = new node. |
| **W3-02b** — Enrollment HTTP client | FR-004, AC-005 | No HTTP client logic to call `POST /api/enrollment-tokens/{token}/consume`. | Agent cannot complete enrollment handshake. |
| **W3-02b** — Token error handling | FR-004, AC-005 | No handling for 410 (expired), 409 (consumed), 404 (missing) enrollment responses. | Poor operator experience on failure. |
| **W3-02** — mTLS steady-state auth | NFR-002, AC-102 | `AccessTokenProvider` returns hardcoded `"placeholder-token"`. No JWT acquisition or refresh. | Security gap; any agent can connect if it knows the hub URL. |
| **W2-04b** — PreUpgradeActions | FR-006, AC-007 | `AssignRunPayload.PreUpgradeActions` exists but is always empty in orchestrator, and agent pipeline never executes them. | Skipped for demo per tracker (MVP-soft), but required for AC-007. |
| **W2-03** — Lease management | NFR-001, AC-101 | `LeaseHeartbeat` and `LeaseClose` message types exist but agent never sends heartbeats; orchestrator has no lease expiration scanner. | Runs may hang indefinitely on agent disconnect. |
| **W2-02** — Sequence/idempotency | FR-002, AC-003 | Agent does not track `lastAcknowledgedSequence` or resume from it. Orchestrator does not reject stale/out-of-order `StepStatus`. | Reconnects may duplicate or corrupt timeline. |
| **W5-01b** — Trust verification | NFR-002, AC-102 | `PostInstallVerify` registry detection is a stub. No artifact signature verification in agent pipeline. | Cannot validate installed state or artifact integrity at runtime. |

---

## 4. Test Infrastructure Reality

| Test Suite | Status | Notes |
|---|---|---|
| **Agent Integration Tests** | ✅ 35 pass, 0 fail | Covers `AcquireArtifact`, `PipelineExecutor`, `InstallOrUpgrade`, `PostInstallVerify`, `EmitFinalization`, `AgentRuntimeContract`, `HostConfiguration` |
| **Orchestrator Integration Tests** | ⚠️ 44 pass, 6 fail | 5 failures stem from `ArtifactZipService` `ArgumentOutOfRangeException` (zip/stream bug). 1 failure is `PackagingTests` Linux binary health endpoint `NotFound`. |
| **Contracts Tests** | ✅ Pass | xUnit-based, validates payload shapes |
| **Testcontainers / Docker** | ❌ Not present | No `Dockerfile`, no `Testcontainers` package, no real Kestrel in `CustomWebApplicationFactory` |
| **Enrollment Tests** | ❌ Not present | Blocked by W3-02b gap |

**The 6 failing orchestrator tests should be fixed before relying on CI evidence.** The `ArtifactZipService` stream position bug affects artifact ingest, which cascades into workload run risk tests.

---

## 5. Readiness Assessment

### For W3-02b (Agent CLI Enrollment)

**Status: NOT READY**

- There is no code to review or test. The entire CLI enrollment surface needs to be built.
- **Prerequisites before starting:**
  1. Fix the 6 failing orchestrator integration tests (especially artifact ingest).
  2. Implement `AgentEnrollmentService` with HTTP client for token consumption.
  3. Implement `AgentConfig` model and JSON persistence.
  4. Add CLI argument parsing to `Program.cs`.

### For W8-02a (Testcontainers Integration Tests)

**Status: NOT READY**

- **Blocked by W3-02b.** Without CLI enrollment, the agent container cannot self-register.
- **Additional prerequisites:**
  1. Create `tests/agent/Dockerfile` (pre-build + `runtime-deps:9.0` base).
  2. Extend `CustomWebApplicationFactory` to bind real Kestrel on `0.0.0.0` (decision record already specifies this).
  3. Add `Testcontainers` package to orchestrator integration test project.
  4. Implement dynamic host IP detection (`host.docker.internal` vs bridge gateway).

---

## 6. Recommendations

1. **Fix the 6 failing orchestrator tests first.** The `ArtifactZipService` `ReferenceReadStream.Position` bug is a blocker for any artifact-dependent test path. Investigate `CompleteSession_IngestsArtifact` and the `WorkloadRunsRiskTests`.

2. **Implement W3-02b in this order:**
   - `Models/AgentConfig.cs` — simple POCO with `NodeId`, `OrchestratorUrl`, `AuthToken`, `EnrollmentToken`.
   - `Services/AgentEnrollmentService.cs` — HTTP client for `POST /api/enrollment-tokens/{token}/consume`, handles 410/409/404.
   - `Program.cs` CLI parsing — inspect `args` before building the web host. Support:
     - `--enroll <token> --orchestrator-url <url>` → enroll, write `agent.json`, start runtime.
     - `--reset-enrollment` → delete `agent.json`, exit.
     - No flags + `agent.json` exists → read config, start runtime.
     - No flags + no config → exit with error.
   - Config path: `%LOCALAPPDATA%/DeploymentPoC/agent.json` (Windows), `/var/lib/deploymentpoc/agent.json` (Linux).

3. **After W3-02b is done, implement W8-02a** following the existing decision record at `docs/decisions/integration-testing-agent-enrollment.md`. The spec is already comprehensive.

4. **Defer non-blocking gaps to post-demo:**
   - Lease management (W2-03)
   - Sequence/idempotency enforcement (W2-02)
   - PreUpgradeActions (W2-04b)
   - mTLS auth (W3-02)
   - RBAC (W5-01a)

---

## 7. Quick Reference: File Inventory

### Implemented and Tested

- `apps/agent/backend/Program.cs`
- `apps/agent/backend/Services/AgentRuntimeService.cs`
- `apps/agent/backend/Pipeline/PipelineExecutor.cs`
- `apps/agent/backend/Steps/AcquireArtifact.cs`
- `apps/agent/backend/Steps/InstallOrUpgrade.cs`
- `apps/agent/backend/Steps/PostInstallVerify.cs`
- `apps/agent/backend/Services/HostPlatformConfiguration.cs`

### Missing Entirely

- `apps/agent/backend/Services/AgentEnrollmentService.cs`
- `apps/agent/backend/Models/AgentConfig.cs`
- `tests/agent/Dockerfile`
- `tests/orchestrator/integration/AgentEnrollment/*`

### Needs Fix

- `apps/orchestrator/backend/Services/ArtifactZipService.cs` (stream position bug)
- `tests/orchestrator/integration/PackagingTests.cs` (Linux binary health endpoint)

---

**Bottom line:** The agent can execute workloads end-to-end in a mock environment, but it cannot enroll, persist identity, or be tested in containers yet. **Build W3-02b first, then W8-02a.**