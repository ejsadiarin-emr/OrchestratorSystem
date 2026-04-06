# ADR-006: Security Baseline (Signed Artifacts, RBAC, Least Privilege)

Date: 2026-04-06

## Status

Accepted for PoC.

## Context

Installer frameworks are privileged systems with high blast radius if compromised.

## Decision

PoC must enforce:

- artifact signature/hash validation,
- role-based authorization,
- least-privilege execution posture,
- auditable append-only event model.

## Consequences

### Positive

- Substantially reduced spoofing/tampering and misuse risk.
- Better auditability and stakeholder confidence.

### Negative

- Increased implementation complexity versus unsecured prototypes.
