# ADR-001: Hybrid Control Plane (Custom Orchestrator + Agent)

Date: 2026-04-06

## Status

Accepted for PoC.

## Context

The problem space includes Windows-heavy enterprise installations, air-gapped-capable operation, legacy installer interop, and strong observability needs. Pure adoption of existing generic automation tools does not fully satisfy Emerson-specific workflow and control requirements.

## Decision

Use a custom .NET Orchestrator + Agent architecture, while borrowing declarative idempotency concepts from Ansible-like models.

## Consequences

### Positive

- Strong domain fit and long-term extensibility.
- Better control over security boundaries and telemetry.
- Clean path for legacy adapter integration.

### Negative

- Increased ownership and maintenance responsibility.
- Requires disciplined architecture to avoid re-creating generic platform complexity.
