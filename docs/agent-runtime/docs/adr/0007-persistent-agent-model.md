# Persistent Agent model — not ephemeral

Agents are persistent lightweight Windows services following the GitHub Actions runner model. An Agent runs continuously, maintains a persistent SignalR connection to the Orchestrator, receives individual workload run assignments (not full DAGs), spawns isolated child processes for each run, and reports results back. There is no spin-up/spin-down lifecycle per workload run — the Agent is always present, always connected, always ready.

**Status**: accepted

**Considered Options**: (1) Ephemeral agents that spin up per workload run, (2) Persistent lightweight Windows services, (3) Container-based agents.

**Consequences**: Simpler operational model with no VM/container orchestration needed. Faster workload run execution with no agent startup latency. Child process isolation provides safety without agent restart. Agent updates require service restart (handled by self-update workflow). The Agent is headless — all operator visibility flows through the Orchestrator UI (Decision 1).

## Amendment

Ported from ADR-009. Changes from original:
- "job assignments" → "workload run assignments" (Decision: workload run terminology)
- "per job" → "per workload run" (Decision: workload run terminology)
- Added headless Agent reference (Decision 1)
- "job execution" → "workload run execution" (Decision: workload run terminology)