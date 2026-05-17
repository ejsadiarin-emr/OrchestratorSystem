# ADR-002: HTTP Polling as Agent Communication

**Status:** Accepted  
**Date:** 2026-05-15  
**Context:** DeploymentPoC orchestrator ↔ agent communication pattern

## Problem

Agents need to receive work assignments from the orchestrator and report results back. Two patterns were considered: server-push (SignalR/WebSockets) vs. client-pull (HTTP polling).

## Decision

**HTTP polling is the permanent architecture**, not a temporary placeholder.

The agent polls `GET /api/workload-runs/pending?agent_id={nodeId}` every 10 seconds (configurable via `Agent:PollIntervalSeconds`). The orchestrator returns pending workload runs or an empty list.

## Alternatives Considered

### SignalR (Server-Push)

SignalR exists in the codebase (`/hubs/agent` hub, `AgentRuntimeHub`) but is **disabled** — push dispatch is commented out on both orchestrator and agent sides. It will not be re-enabled.

**Why rejected:**
- Adds connection lifecycle complexity — reconnect logic, heartbeat keepalive, connection state tracking
- Requires persistent connections per agent — memory and scaling pressure on the orchestrator
- Firewall/proxy unfriendly — WebSockets long-polling may be blocked in corporate environments
- Debugging is harder — state is spread across connection lifetime, not discrete request/response pairs
- The poll interval (10s) is acceptable latency for deployment workloads — real-time push is unnecessary

**Dormant code risk:** `NodeWorkloadStateService.HandleLeaseHeartbeatAsync` and `AgentRuntimeHub.Identify` both update `LastSeenUtc` without the stale-threshold optimization (ADR-003). These paths are unreachable while SignalR is disabled. Any re-enablement must apply the same stale-threshold pattern.

### Long Polling

Agent opens a request; orchestrator holds the connection open until work is available or a timeout expires.

**Why rejected:**
- Held connections consume server resources (threads, memory) per agent
- Still requires a fallback poll interval for cases where no work arrives
- No significant advantage over short polling at 10s intervals for deployment workloads

## Consequences

### Positive
- **Simpler agent** — no persistent connection management, no reconnect logic, no heartbeat keepalive
- **Firewall-friendly** — standard HTTP requests work through all corporate proxies and firewalls
- **Stateless** — each poll is independent, no server-side connection state to maintain
- **Natural backpressure** — agent controls its own poll rate; orchestrator doesn't push faster than agent can handle
- **Easier debugging** — every interaction is a discrete HTTP request/response pair

### Negative
- **Latency floor** — minimum 10-second delay between work assignment and agent pickup (poll interval)
- **Unnecessary traffic** — most polls return empty results when no work is pending
- **No real-time telemetry** — orchestrator only knows agent state at poll time, not continuously
- **Scalability ceiling** — at large node counts, frequent polling creates load on the orchestrator

## Trade-offs Accepted

- Work assignment latency of up to `PollIntervalSeconds` is acceptable for deployment workloads (not real-time systems)
- Empty poll overhead is acceptable given the PoC scale (< 100 nodes)
- Heartbeat is conflated with `/pending` polling — no separate keepalive channel (see ADR-003)

## Related

- Agent poller: `AgentRuntimeService.cs`
- Pending endpoint: `WorkloadRunsController.GetPending()`
- Liveness model: ADR-003
- Dormant alternate paths: `NodeWorkloadStateService.HandleLeaseHeartbeatAsync`, `AgentRuntimeHub.Identify`