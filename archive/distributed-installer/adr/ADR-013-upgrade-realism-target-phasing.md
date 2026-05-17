# ADR-013: Upgrade Realism Target Phasing for PoC

Date: 2026-04-11

## Status

Accepted for PoC.

## Context

The PoC must prove configuration backup/migration/restore behavior, but the team does not yet have full Day 3 current-state depth for all legacy installer paths. Using a SQL Server-grade installer as the first proof target risks mixing framework validation with package-specific complexity.

Day 3 output requires a current-state assessment and modernization map before final package-level commitments are made.

## Decision

For phase 1 proof, use a **DeltaV-adjacent workstation component** (file + service lifecycle) as the reference package for upgrade/config persistence validation.

Defer SQL Server-grade installer validation to phase 2.

Package-specific exact target selection remains **intentionally deferred** until Day 3 outputs are completed:

- current-state install flow and component map
- legacy technology map and constraints
- modernization map with risk/effort

## Alternatives Considered

### Alternative 1: SQL Server-grade installer first

- **Pros**: High realism and stakeholder confidence for complex installs.
- **Cons**: High setup complexity can hide framework issues and slow feedback cycles.
- **Why not**: Too much package-specific complexity for first contract validation.

### Alternative 2: Synthetic/demo-only package first

- **Pros**: Fastest path to technical validation.
- **Cons**: Weak domain credibility and limited transferability to real operations.
- **Why not**: Insufficient realism for the PoC's modernization objective.

## Consequences

### Positive

- Validates core framework contracts quickly (backup, migration, rollback behavior).
- Keeps phase 1 scope focused and implementation-friendly.
- Preserves a clear phase 2 path for heavier installer classes.

### Negative

- Final package identity is not fully fixed yet.
- Requires explicit follow-up after Day 3 outputs.

### Risks

- **Risk**: Team drifts into low-fidelity package choice that misses critical legacy constraints.
  - **Mitigation**: Gate exact package selection on Day 3 artifact completion and document rationale in implementation planning.
