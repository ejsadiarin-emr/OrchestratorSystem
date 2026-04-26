# Decision: Node Online Status as Derived Property (Ephemeral)

**Date:** 2026-04-26

**Context:** The `NodeEntity.Status` column ("Online" / "Offline") was actively written to the database by four separate components: `AgentRuntimeHub` on connect/disconnect, `NodeWorkloadStateService` on heartbeat, and `NodeHeartbeatMonitorService` on timeout detection. This created write amplification (a background service flipping stale nodes every 30s), race conditions (node reconnects between SELECT and UPDATE), and stale reads (poll interval + timeout window). The status was always a lagging indicator of the authoritative `LastSeenUtc` timestamp.

## Decisions

### 1. Eliminate Status Column Writes

**Decision:** Remove all writes to the `NodeEntity.Status` column. The `Status` column remains in the database schema but is no longer actively maintained.

**Rationale:**
- `LastSeenUtc` is the single source of truth for connectivity
- Eliminates 30s write amplification in `NodeHeartbeatMonitorService`
- Eliminates race condition between heartbeat timeout and agent reconnect
- Reduces DB write load and index churn

### 2. Derive Online Status at Query Time

**Decision:** Compute `Status` as a derived property in the API response layer (`NodesController`, `EnrollmentController`) using the threshold:

```
Status = LastSeenUtc >= (UtcNow - 2min) ? "Online" : "Offline"
```

**Rationale:**
- Aligns with industry-standard pattern (Kubernetes node conditions, Azure IoT Hub device connection state, Prometheus `up` metric, Datadog agent connectivity)
- Eliminates the stale-read problem — status is always current to the query time
- No background writes needed

### 3. Simplify NodeHeartbeatMonitorService

**Decision:** Remove DB writes from `NodeHeartbeatMonitorService`. The service continues to run for observability logging only.

**Rationale:**
- The service's sole purpose was persisting `Status = "Offline"`
- Without that responsibility, it acts as a passive health reporter
- Can be removed entirely in a future cleanup if observability is covered elsewhere

## Affected Files

| File | Change |
|------|--------|
| `AgentRuntimeHub.cs:33` | Remove `node.Status = "Online"` |
| `AgentRuntimeHub.cs:82-94` | Remove `Status = "Offline"` write + `SaveChangesAsync` on disconnect |
| `NodeWorkloadStateService.cs:157` | Remove `node.Status = "Online"` |
| `NodeHeartbeatMonitorService.cs:35-48` | Remove status filter + write; keep logging |
| `NodesController.cs` | Derive `Status` from `LastSeenUtc` in all mapping |
| `EnrollmentController.cs:106,126` | Remove write, derive at response |

## Status

Accepted
