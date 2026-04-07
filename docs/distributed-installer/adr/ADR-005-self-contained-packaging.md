# ADR-005: Self-Contained Packaging for Orchestrator and Agent

Date: 2026-04-06

## Status

Accepted for PoC.

## Context

Target environments may be air-gapped and should minimize prerequisite dependencies.

## Decision

Publish Orchestrator and Agent as self-contained .NET binaries (single-file where practical), with signed artifacts and controlled update workflow.

## Consequences

### Positive

- Reduced runtime dependency friction.
- Better portability in constrained environments.

### Negative

- Larger binaries.
- Runtime/security patch cadence responsibility shifts to product team.
