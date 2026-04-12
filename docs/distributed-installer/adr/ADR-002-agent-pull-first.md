# ADR-002: Agent-Initiated Connection with Push Assignment

Date: 2026-04-06

## Status

Accepted for PoC.

## Context

On-prem and potentially segmented enterprise networks make controller-initiated connectivity brittle in some environments. The system also needs low-latency assignment and explicit ownership semantics during reconnect windows.

## Decision

Use an agent-initiated persistent connection model:

- Agent initiates and maintains SignalR connectivity to orchestrator.
- Orchestrator sends `AssignJob` over the established channel.
- Agent must `Ack/Claim` with lease ownership before execution.
- Execution lifecycle uses lease + heartbeat + completion/failure closure semantics.

## Consequences

### Positive

- Better resilience across constrained networks (agent initiates outbound connection).
- Lower assignment latency than polling intervals.
- Cleaner ownership/recovery behavior through claim + lease semantics.

### Negative

- Requires careful lease/claim and ordering logic to avoid split ownership.
- Adds protocol complexity compared with simple polling.
