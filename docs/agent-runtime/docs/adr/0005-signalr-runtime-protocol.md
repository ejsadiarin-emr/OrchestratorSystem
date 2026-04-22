# SignalR as Agent-Orchestrator runtime protocol

The Agent communicates with the Orchestrator exclusively via ASP.NET Core SignalR for all runtime concerns: workload run assignments, heartbeats, step-level status, and log streaming. REST is used only for the Orchestrator's own UI-to-backend CRUD operations and artifact downloads.

**Status**: accepted

**Considered Options**: (1) HTTP polling, (2) gRPC streaming, (3) raw WebSockets, (4) SignalR with fallback transports.

**Consequences**: SignalR gives native .NET typed hub contracts, automatic transport negotiation (WebSocket → SSE → Long Polling), built-in reconnection, and bidirectional push. The runtime dispatch sequence is canonicalized as `Connect → Register/Authenticate → AssignRun → AckClaim → LeaseHeartbeat → StepStatus* → Complete/Fail → LeaseClose`. Message handling is idempotent: `AssignRun` carries `assignmentId`, `leaseId`, and monotonic `sequence`; status updates upsert by `(workloadRunId, stepId, sequence)`; stale/out-of-order updates are discarded; reconnect uses resume handshake with last acknowledged sequence. Cross-platform Linux agents (Phase 2) will need a SignalR client or alternative transport.

## Amendment

Ported from ADR-007. Changes from original:
- `AssignJob` → `AssignRun` (Decision: workload run terminology)
- `jobId` → `workloadRunId` in idempotency key (Decision: workload run terminology)
- Removed "UI-to-orchestrator" qualifier on REST scope — the Agent is headless; REST is for the Orchestrator's embedded UI and artifact downloads only (Decision 1)
- "job assignments" → "workload run assignments" (Decision: workload run terminology)
- Added clarification that workloads are dispatched and monitored from the Orchestrator UI — running a workload from the UI is the key demo goal (Decision 7)