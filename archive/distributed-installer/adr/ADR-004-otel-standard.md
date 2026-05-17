# ADR-004: OpenTelemetry as Standard Observability Layer

Date: 2026-04-06

## Status

Accepted for PoC.

## Context

Distributed install orchestration needs correlated traces, metrics, and logs for failure diagnosis and operational trust.

## Decision

Adopt OpenTelemetry conventions for traces, metrics, and logs in orchestrator and agent.

## Consequences

### Positive

- Standardized telemetry model and backend portability.
- Better cross-component root cause analysis.

### Negative

- Requires schema discipline and instrumentation effort early.
