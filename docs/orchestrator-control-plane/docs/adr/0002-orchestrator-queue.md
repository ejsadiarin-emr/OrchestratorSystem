# Hangfire for Orchestrator run queue

The Orchestrator uses Hangfire for persistent SQL-backed workload run queuing with retries, scheduling, and a monitoring dashboard. The orchestrator is the source of truth for workload run state — it plans, schedules, and dispatches runs to agents via SignalR.

**Status**: accepted

**Considered Options**: (1) Custom queue on raw SQL, (2) RabbitMQ or external broker, (3) Hangfire with SQL Server storage.

**Consequences**: Hangfire provides production-grade queue management, retry logic, a monitoring dashboard, and job scheduling using the same SQL Server already required by the Orchestrator. At very large scale, partitioning may be needed (deferred to production). Running a workload from the Orchestrator UI is the key demo goal — Hangfire's dashboard and queue visibility directly support that operator experience.

## Amendment

Ported from ADR-008 (orchestrator portion). Changes from original:
- "job queue management" → "workload run queuing" (Decision: workload run terminology)
- "Hangfire ... job scheduling" → "workload run scheduling" (Decision: workload run terminology)
- Expanded to clarify that the Orchestrator is the single control plane for run planning and dispatch (Decision 7: running workload from orchestrator UI is key demo goal)