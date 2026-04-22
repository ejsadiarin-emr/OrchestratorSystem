# Multi-installer adapter strategy (MSI + EXE first)

Enterprise environments include heterogeneous installer formats and legacy components; no single format covers all real-world cases. Implement adapter-based execution with MSI and EXE support in PoC Phase 1. Keep MSIX as a design-ready extension. Each adapter provides typed detection, install, and rollback contracts. The Agent's execution pipeline invokes adapters locally with no UI — all step-level status is reported to the Orchestrator.

**Status**: accepted

**Considered Options**: (1) Single-format MSI-only execution, (2) Shell-script wrapper with format detection, (3) Typed adapter per installer format with standardized contracts.

**Consequences**: Fastest realistic compatibility for legacy-heavy environments. Allows phased modernization. EXE and custom installers need stricter detection and rollback contracts. Greater variability in error handling across adapters.

**Amendments**: Ported from ADR-003 (distributed-installer, 2026-04-06). Added: execution pipeline has no UI, all status reported to Orchestrator (Decision 1).