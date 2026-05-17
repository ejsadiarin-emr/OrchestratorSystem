# ADR-011: Dry-Run Validation Confidence Framework

Date: 2026-04-07

## Status

Accepted for PoC.

## Context

Pre-install validation (dry-run) checks need to report how confident they are that the deployment will succeed. Not all checks are equally reliable — a checksum verification is deterministic, while a disk space check could become stale between validation and execution.

## Decision

Implement a **confidence level framework** where each pre-check reports its own confidence based on how directly it can verify the condition:

| Level | Confidence | Basis |
|---|---|---|
| Deterministic | 100% | Direct, authoritative check (checksums, signatures, schema) |
| High | ~95% | OS-reported state that could drift (version checks, registry) |
| Medium | ~70-80% | External state or timing dependent (disk space, network) |
| Low | ~50-60% | Indirect or heuristic (historical patterns, manufacturer data) |

Overall deployment confidence is the **minimum** of all individual check confidences. Action thresholds:

- 100%: proceed automatically
- High (≥90%): proceed automatically with risk status displayed in UI
- Medium (≥70%): proceed automatically with risk status displayed in UI
- Low (<70%): block deployment

Confidence accuracy is tracked empirically over time to adjust levels based on real-world success rates.

## Consequences

### Positive

- Operators see risk level before deploying (risk status visible in orchestrator UI)
- Fully automatic workflow: pre-check → risk detection → status display → proceed via pre-defined upgrade paths
- No manual approval bottleneck — operator can cancel if needed
- Conservative blocking at Low confidence protects production systems
- Empirical tracking improves confidence accuracy over time
- Clear, structured interface for adding new pre-checks

### Negative

- May proceed with risks that could cause issues (requires good pre-defined upgrade paths)
- Requires discipline in assigning confidence levels to new checks
- Empirical tracking requires sufficient deployment volume to be meaningful
