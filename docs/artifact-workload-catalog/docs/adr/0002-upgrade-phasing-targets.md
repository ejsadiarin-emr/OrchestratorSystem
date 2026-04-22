# Upgrade phasing targets for PoC

Phase 1 proof uses a DeltaV-adjacent workstation component (file + service lifecycle) as the reference package for upgrade/config persistence validation. SQL Server-grade installer validation is deferred to Phase 2. Package-specific exact target selection remains intentionally deferred until Day 3 outputs (current-state install flow, legacy technology map, modernization map) are completed. All version transitions use a single update workflow — no separate major/minor paths.

**Status**: accepted

**Considered Options**: (1) SQL Server-grade installer first, (2) Synthetic/demo-only package first, (3) DeltaV-adjacent workstation component first.

**Consequences**: Validates core framework contracts quickly (backup, migration, rollback behavior). Keeps Phase 1 scope focused. Preserves a clear Phase 2 path for heavier installer classes. Workload definitions are imported from a global JSON file containing 2–3 workloads (Decision 6).

## Amendment

Ported from ADR-013. Changes from original:
- Added single update workflow reference (Decision 3: no major/minor split)
- Added reference to global workload JSON file (Decision 6)