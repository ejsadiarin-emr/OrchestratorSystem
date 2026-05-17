# ADR-009: Persistent Agent Model (Not Ephemeral)

Date: 2026-04-07

## Status

Accepted for PoC.

## Context

Two agent lifecycle models were considered: ephemeral agents that spin up per job and tear down after completion, versus persistent agents that run continuously as services on target machines.

## Decision

Use **persistent lightweight Windows services** as agents, following the GitHub Actions runner model.

Agents:
- run continuously as Windows services on target machines
- maintain a persistent SignalR connection to the orchestrator
- receive individual job assignments (not full DAGs)
- spawn isolated child processes for each job execution
- report results back to the orchestrator

There is no spin-up/spin-down lifecycle per job. The agent is always present, always connected, always ready.

## Consequences

### Positive

- Simpler operational model — no VM/container orchestration needed
- Faster job execution — no agent startup latency
- Easier bootstrap — one-time install, then managed remotely
- Matches enterprise endpoint management patterns (SCCM, PDQ)
- Child process isolation provides safety without agent restart

### Negative

- Agents consume resources continuously (minimal for a Windows service)
- Agent updates require service restart (handled by self-update workflow)
- Less suitable for cloud-native auto-scaling scenarios (not a PoC concern)
