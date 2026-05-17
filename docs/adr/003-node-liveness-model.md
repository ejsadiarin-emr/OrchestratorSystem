# ADR-003: Node Liveness Model

**Status:** Accepted  
**Date:** 2026-05-17  
**Context:** How the orchestrator determines whether a node is alive, tracks last-seen timestamps, and detects offline nodes

## Problem

The orchestrator must know which nodes are alive to make scheduling decisions and display node status in the UI. The system needs a consistent model for what constitutes a heartbeat, how liveness is stored, and how offline detection works.

## Decision

### 1. `/pending` is the sole heartbeat mechanism

Only `GET /api/workload-runs/pending?agent_id={nodeId}` updates `LastSeenUtc` and `Status = "Online"` on the node record. Other agent-facing endpoints (`PATCH` status updates, `POST` timeline events) intentionally do **not** update `LastSeenUtc`.

This is intentional: if the agent stops polling `/pending`, it is effectively dead, even if it sporadically reports step status. The outer poll loop in `AgentRuntimeService.ExecuteAsync` always runs, even when a pipeline is executing in the background via `Task.Run`.

### 2. Offline detection via background monitor

`NodeHeartbeatMonitorService` runs every 30 seconds and marks nodes as `Offline` if `LastSeenUtc` is older than 2 minutes. This is independent of the heartbeat write path — it reads `LastSeenUtc` and flips status.

### 3. Stale-threshold write optimization

Since the `/pending` endpoint is called every 10 seconds per agent, writing `LastSeenUtc` on every call creates unnecessary SQLite write contention. SQLite serializes all writes, so each heartbeat blocks the next.

Only write the heartbeat when `LastSeenUtc` is stale by more than a configurable threshold (default: **15 seconds**):

```csharp
if (node is not null && (DateTime.UtcNow - node.LastSeenUtc).TotalSeconds > _heartbeatStaleThresholdSeconds)
{
    node.Status = "Online";
    node.LastSeenUtc = DateTime.UtcNow;
    await _db.SaveChangesAsync();
}
```

Configured via `appsettings.json`:
```json
{ "Heartbeat": { "StaleThresholdSeconds": 15 } }
```

**Why 15 seconds** (1.5× the 10s poll interval):

| Time | Delta | Action |
|---|---|---|
| t=0 | 0s | SKIP (first poll after write) |
| t=10 | 10s | SKIP |
| t=20 | 20s | WRITE |
| t=30 | 10s | SKIP |
| t=40 | 20s | WRITE |

This writes approximately once every 20 seconds — ~50% reduction in SQLite writes. The threshold must be greater than the poll interval to have any effect.

### 4. Liveness is derived, not stored

`NodesController` read endpoints **never** read the `Status` column from the database. They always compute status from `LastSeenUtc` using `LastSeenUtc >= DateTime.UtcNow.AddMinutes(-2)`, producing `"online"` / `"offline"` in lowercase.

Meanwhile, heartbeat writes store `"Online"` / `"Offline"` (PascalCase) in the `Status` column. The column is effectively write-only — its stored value is never the source of truth for liveness queries.

## Invariants

1. **`/pending` is the sole heartbeat path** — only this endpoint updates `LastSeenUtc` (with the stale-threshold gate)
2. **`NodeHeartbeatMonitorService` is unaffected** — it scans every 30s with a 2-minute offline threshold; the stale-threshold gate only reduces write frequency
3. **`AsNoTracking` reads** — the `/pending` endpoint reads all workload data with `AsNoTracking`, then performs a separate `FindAsync` on the node entity for the conditional heartbeat write
4. **Liveness is computed** — `NodesController` always derives status from `LastSeenUtc`, never reads the `Status` column

## Known Issues

### Admin Edit Contamination

`NodesController.Update` (PUT `/api/nodes/{id}`) sets `entity.LastSeenUtc = DateTime.UtcNow` when an admin edits a node's hostname or description. This is semantically wrong — an admin edit is not a heartbeat. If a node stopped polling and an admin edits its metadata, the node appears "online" for up to 2 minutes on a falsely fresh `LastSeenUtc`.

**Fix:** Remove the `LastSeenUtc` assignment from `NodesController.Update`.

### Dormant Heartbeat Paths

`NodeWorkloadStateService.HandleLeaseHeartbeatAsync` and `AgentRuntimeHub.Identify` both update `LastSeenUtc` without the stale-threshold gate. These are unreachable under current architecture (SignalR disabled). If re-enabled, they must apply the stale-threshold pattern from this ADR.

### Status Column Inconsistency

The `Status` column stores PascalCase (`"Online"`/`"Offline"`) from heartbeat/monitor writes, but `NodesController` always computes lowercase (`"online"`/`"offline"`) from `LastSeenUtc`. The stored `Status` is never read for liveness — it's effectively write-only.

**Fix:** Either remove the `Status` column and always compute from `LastSeenUtc`, or make `NodesController` read from stored `Status` and have `NodeHeartbeatMonitorService` be the sole writer.

## Related

- Heartbeat write path: `WorkloadRunsController.GetPending()` (stale-threshold gate)
- Offline detection: `NodeHeartbeatMonitorService.cs` (2-minute threshold)
- Transport architecture: ADR-002 (polling designates `/pending` as heartbeat)
- Agent poller: `AgentRuntimeService.cs` (10-second default poll interval)