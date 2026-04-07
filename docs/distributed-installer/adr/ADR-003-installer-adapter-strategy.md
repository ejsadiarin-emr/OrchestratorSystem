# ADR-003: Multi-Installer Adapter Strategy (MSI + EXE First)

Date: 2026-04-06

## Status

Accepted for PoC.

## Context

Enterprise environments include heterogeneous installer formats and legacy components; no single format is practical for immediate full coverage.

## Decision

Implement adapter-based execution with MSI and EXE support in PoC. Keep MSIX as design-ready extension.

## Consequences

### Positive

- Fastest realistic compatibility for legacy-heavy environments.
- Allows phased modernization.

### Negative

- EXE/custom installers need stricter detection and rollback contracts.
- Greater variability in error handling.
