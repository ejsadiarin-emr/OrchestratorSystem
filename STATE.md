# STATE.md — Known Issues & Pending Fixes

Last updated: 2026-05-17

## 1. Admin Edit Contaminates Heartbeat

**Severity:** Medium  
**Files:** `NodesController.cs` (line 130)  
**ADR:** ADR-002 (Known Issue), ADR-003 (Known Issue)

`NodesController.Update` (`PUT /api/nodes/{id}`) sets `entity.LastSeenUtc = DateTime.UtcNow` when an admin edits a node's hostname, description, or IP address. This is semantically wrong — an admin edit is not a heartbeat.

**Impact:** If a node stopped polling but an admin edits its metadata, the node will appear "online" for up to 2 minutes on a falsely fresh `LastSeenUtc`.

**Fix:** Remove `LastSeenUtc = DateTime.UtcNow` from `NodesController.Update`. The `/pending` endpoint is the sole heartbeat mechanism (ADR-002). Admin edits should not touch liveness timestamps.

---

## 2. Dormant Heartbeat Paths Bypass Stale-Threshold Gate

**Severity:** Low (currently unreachable)  
**Files:** `NodeWorkloadStateService.cs` (HandleLeaseHeartbeatAsync, line 181-207), `AgentRuntimeHub.cs` (Identify, line 34-39)  
**ADR:** ADR-002 (Dormant Code Risk), ADR-003 (Dormant Code)

Two code paths update `LastSeenUtc` on every invocation without the stale-threshold optimization:

- `NodeWorkloadStateService.HandleLeaseHeartbeatAsync` — writes `Status = "Online"` and `LastSeenUtc = DateTime.UtcNow` unconditionally. Only reachable via SignalR `LeaseHeartbeat` message type, which the polling agent never sends.
- `AgentRuntimeHub.Identify` — writes `Status = "Online"` and `LastSeenUtc = DateTime.UtcNow` unconditionally. Only reachable when an agent connects via SignalR hub, which is disabled.

**Impact:** Currently none — both paths are unreachable while SignalR is disabled and the agent uses HTTP polling. If re-enabled without applying the stale-threshold pattern from ADR-003, these paths would re-introduce per-message SQLite writes and undermine the optimization.

**Fix:** If either path is re-enabled, apply the same `Heartbeat:StaleThresholdSeconds` gate before writing. `HandleLeaseHeartbeatAsync` also carries `OsVersion` and `AgentVersion` telemetry — preserve that logic inside the stale threshold gate so telemetry updates still flow.

---

## 3. Node Status Inconsistency Between Database and API Projections

**Severity:** Low  
**Files:** `NodesController.cs` (multiple locations), `WorkloadRunsController.cs` (line 636)

The `NodeEntity.Status` column stores `"Online"` / `"Offline"` (PascalCase from heartbeat writes), but `NodesController` read endpoints **recompute** status from `LastSeenUtc` using `LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2)`, producing `"online"` / `"offline"` (lowercase). Meanwhile, `WorkloadRunsController.GetPending` writes `Status = "Online"` (PascalCase).

**Impact:** The `Status` column in the database is never read for liveness — `NodesController` always derives status from `LastSeenUtc`. The stored `Status` column is only written, creating a write-only field that may drift from the computed value. No current bug because reads don't use it, but it's misleading.

**Fix:** Either (a) remove the `Status` column and always compute from `LastSeenUtc`, or (b) make `NodesController` read from the stored `Status` and have `NodeHeartbeatMonitorService` be the sole writer. Option (a) is simpler and aligns with ADR-002's liveness model.