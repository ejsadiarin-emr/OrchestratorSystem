# Channel<T> for Agent-side workload run buffering

The Agent uses in-memory `Channel<T>` with `BackgroundService` for buffering and executing workload runs received via SignalR. Agents do not need persistent queuing — the Orchestrator is the source of truth for workload run state. If an Agent restarts, it re-queries the Orchestrator for in-progress runs.

**Status**: accepted

**Considered Options**: (1) Local persistent queue (SQLite/RocksDB), (2) Channel<T> + BackgroundService in-memory, (3) Persistent agent-local Hangfire instance.

**Consequences**: Agents remain lightweight with no SQL dependency. Channel<T> is in-memory so a restart loses queued-but-not-started runs, but this is mitigated by re-querying the Orchestrator for in-progress workload runs after reconnection. The Agent never makes local policy decisions — it executes what the Orchestrator dispatches.

## Amendment

Ported from ADR-008 (agent portion). Changes from original:
- "job buffering" → "workload run buffering" (Decision: workload run terminology)
- "in-progress jobs" → "in-progress workload runs" (Decision: workload run terminology)
- Added explicit statement that the Agent never makes local policy decisions (aligns with headless Agent model, Decision 1)