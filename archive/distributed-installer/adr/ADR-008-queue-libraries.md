# ADR-008: Hangfire for Orchestrator Queue, Channel<T> for Agents

Date: 2026-04-07

## Status

Accepted for PoC.

## Context

The system needs job queue management on both the orchestrator and agent sides. The orchestrator requires persistent queuing with retries, scheduling, and monitoring. Agents need lightweight job buffering and execution.

## Decision

Use **Hangfire** on the orchestrator side for job queue management, and **Channel<T> + BackgroundService** on the agent side.

Hangfire provides persistent SQL-backed queuing, retry logic, a monitoring dashboard, and job scheduling — all features the orchestrator needs.

Agents use in-memory Channel<T> because:
- the orchestrator is the source of truth for job state
- agents don't need persistent storage
- jobs arrive in real-time via SignalR
- if an agent restarts, it re-queries the orchestrator for in-progress jobs
- simpler deployment with no SQL dependency on agents

## Consequences

### Positive

- Orchestrator gets production-grade queue management out of the box
- Agents remain lightweight with minimal dependencies
- Clear separation: orchestrator manages persistence, agents manage execution
- Hangfire dashboard provides operational visibility

### Negative

- Hangfire requires SQL Server (already a dependency, so acceptable)
- Channel<T> is in-memory — agent restart loses queued-but-not-started jobs (mitigated by re-querying orchestrator)
- Hangfire at very large scale may need partitioning (deferred to production)
