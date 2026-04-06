# ADR-002: Agent Pull-First Distribution Model

Date: 2026-04-06

## Status

Accepted for PoC.

## Context

On-prem and potentially segmented enterprise networks make controller push connectivity brittle in some environments.

## Decision

Agents will poll/claim jobs (pull-first). Push remains a possible future optimization for urgent actions.

## Consequences

### Positive

- Better resilience across constrained networks.
- Simpler firewall posture.
- Cleaner reconnection/recovery behavior.

### Negative

- Slightly higher latency from poll intervals.
- Requires careful queue/claim logic to avoid contention.
