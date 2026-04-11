# ADR-012: Enterprise Bootstrap vs Runtime Orchestration Boundary

Date: 2026-04-11

## Status

Accepted for PoC.

## Context

A key stakeholder question is why this distributed installer should exist if enterprise admins can already deploy software with GPO or SCCM. The project also has a no-scripting runtime principle in `learning-plan.md`, while enterprise bootstrap methods commonly use scripts for initial provisioning.

The architecture needs a clear boundary so the system is not seen as duplicating endpoint-management tooling.

## TL;DR

_Why create a distributed installer if system admins can install packages via GPO/SCCM in the first place?_

GPO/SCCM and the distributed installer solve different layers.

- **GPO/SCCM** are software distribution frameworks (copy/install package at scale).
- **Distributed installer** is a domain-aware installation orchestrator (deterministic multi-step workflows, prechecks, rollback semantics, step telemetry, legacy adapter control, and operator UX tailored to DeltaV/industrial constraints).

Why GPO/SCCM alone is insufficient for target outcomes:

- They do not natively model a typed `IInstallStep` pipeline contract with per-step deterministic behavior.
- They do not provide domain-specific dry-run confidence scoring and policy gates.
- Rollback/compensation across heterogeneous installers (MSI/EXE/custom) becomes ad hoc script logic.
- Cross-package/job dependencies and install-state modeling get fragmented across deployment objects/scripts.
- Deep correlated telemetry (job/step trace + audit + reasoned states) is weaker and less unified.
- They are strong at "deploy this package" and weaker at "safely execute this installer lifecycle with domain rules."

Best framing:

- Use GPO/SCCM to deploy/manage the agent.
- Use the distributed installer to execute and govern installs/upgrades/rollbacks.

## Decision

Define a strict responsibility split:

- **GPO/SCCM/WinRM** are allowed as **bootstrap and agent lifecycle channels** (install/register/update the agent).
- The distributed installer control plane is the **only runtime orchestration surface** for install/upgrade/rollback workflows.
- Runtime automation remains **C# + typed manifests + REST/CLI** (no script-based runtime orchestration surface).

In short: enterprise tools distribute/manage agents; the distributed installer governs package execution logic.

## Alternatives Considered

### Alternative 1: Use only GPO/SCCM for package deployments

- **Pros**: Leverages existing enterprise tooling; reduces new platform scope.
- **Cons**: Weak domain-specific pipeline control, fragmented rollback semantics, weaker unified telemetry/audit model for installer workflows.
- **Why not**: Does not satisfy core PoC goals around deterministic step orchestration, dry-run confidence gating, typed install contracts, and centralized installer-specific state model.

### Alternative 2: Allow direct runtime deployment from CI/tooling and scripts

- **Pros**: Flexible and familiar for ad hoc operations.
- **Cons**: Splits operational truth across tools, weakens policy/audit consistency, increases nondeterministic script sprawl.
- **Why not**: Conflicts with no-scripting runtime direction and bypasses orchestrator controls.

## Consequences

### Positive

- Clear product justification: complements (does not replace) GPO/SCCM.
- Preserves a single control plane for install behavior, policy, and auditability.
- Keeps runtime behavior deterministic and testable through typed contracts.
- Scales bootstrap through enterprise channels without weakening orchestration design.

### Negative

- Introduces integration work with enterprise bootstrap channels.
- Requires teams to adopt a two-layer model (provisioning layer + orchestration layer).

### Risks

- **Risk**: Teams bypass orchestrator and execute runtime installs directly from enterprise tools.
    - **Mitigation**: Document and enforce policy that enterprise channels may deploy/update agents only; package jobs must be submitted via Orchestrator API/CLI.
