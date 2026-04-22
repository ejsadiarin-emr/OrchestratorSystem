# Dry-run validation confidence framework

Pre-install validation (dry-run) checks report their own confidence level based on how directly they can verify a condition. Overall deployment confidence is the minimum of all individual check confidences. The update workflow is fully automatic: pre-check → detect risk → display status in Orchestrator UI → proceed via pre-defined upgrade paths. No manual approval gate blocks progress; operators can cancel but not approve.

| Level | Confidence | Basis |
|---|---|---|
| Deterministic | 100% | Direct, authoritative check (checksums, signatures, schema) |
| High | ~95% | OS-reported state that could drift (version checks, registry) |
| Medium | ~70-80% | External state or timing dependent (disk space, network) |
| Low | ~50-60% | Indirect or heuristic (historical patterns, manufacturer data) |

Action thresholds: 100% → proceed automatically; ≥90% → proceed with risk status displayed in UI; ≥70% → proceed with risk status displayed in UI; <70% → block deployment. Confidence accuracy is tracked empirically over time.

**Status**: accepted

**Consequences**: Operators see risk level before deploying via the Orchestrator UI (no agent UI — the Agent is headless). Fully automatic workflow with no manual approval bottleneck. Conservative blocking at Low confidence protects production systems. A single update workflow applies to all version transitions — no separate major/minor paths (Decision 3).

## Amendment

Ported from ADR-011. Changes from original:
- Added explicit statement that there is no manual approval gate (Decision 3: single update workflow)
- Added reference to headless Agent model — risk status is only visible in Orchestrator UI (Decision 1)
- Removed implied major/minor workflow distinction (Decision 3)
- Clarified that running a workload from the Orchestrator UI is the key demo goal (Decision 7)