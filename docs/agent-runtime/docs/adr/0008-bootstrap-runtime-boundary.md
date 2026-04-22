# Browser-based bootstrap vs runtime orchestration boundary

GPO/SCCM and the distributed installer solve different layers. GPO/SCCM are software distribution frameworks (copy/install at scale). The distributed installer is a domain-aware installation orchestrator (deterministic multi-step workflows, prechecks, rollback semantics, step telemetry, and operator UX). The boundary is strict: enterprise tools (GPO/SCCM) are allowed as bootstrap and agent lifecycle channels (install/register/update the Agent); the distributed installer control plane is the only runtime orchestration surface for install/upgrade/rollback workflows. Bootstrap is browser-based — the operator downloads a signed agent.exe with an embedded enrollment token from an Orchestrator URL. No WinRM or push scripts.

**Status**: accepted

**Considered Options**: (1) Use only GPO/SCCM for package deployments, (2) Allow direct runtime deployment from CI/tooling and scripts, (3) Strict two-layer model: enterprise tools for agent provisioning, orchestrator for runtime.

**Consequences**: Clear product justification — complements rather than replaces GPO/SCCM. Single control plane for install behavior, policy, and auditability. Runtime behavior stays deterministic and testable through typed contracts. Scales bootstrap through enterprise channels without weakening orchestration design. The Agent is headless with no local UI (Decision 1); bootstrap uses browser-based download with enrollment-token authorization (Decision 2).

## Amendment

Ported from ADR-012. Changes from original:
- Replaced all WinRM references with browser-based bootstrap (Decision 2)
- Added explicit headless Agent reference (Decision 1)
- "GPO/SCCM/WinRM" → "GPO/SCCM" — WinRM is no longer a bootstrap channel (Decision 2)
- Removed "job" references, replaced with workload run terminology
- Added enrollment-token language consistent with the Agent Runtime bounded context