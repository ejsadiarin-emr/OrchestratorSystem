# Meeting Summary Notes - Distributed Installer PoC (Validation Session)

Date: 2026-04-06  
Audience: Lead/architect/team stakeholders  
Goal of this meeting: Validate direction before deeper implementation

## 1) Problem we are solving

- System admins do not have a modern, unified, reliable remote install workflow for required software and DeltaV-related components.
- Current ecosystem is legacy-heavy and difficult to evolve.
- Failure visibility is limited, so troubleshooting and rollback are expensive.

## 2) Proposed PoC in one sentence

Build a Windows-first, air-gapped-capable **Orchestrator + Agent** installer framework in .NET + React that is idempotent, replayable, and telemetry-first, while supporting legacy installers through adapter steps.

## 3) Why this direction (short and honest)

- **Best PoC fit**: practical scope for internship timeline, but still future-compatible.
- **Better than Ansible-centric runtime for this case**: we borrow Ansible ideas (declarative/idempotent manifests), but keep execution/control in C# for Emerson-specific behavior, richer UI, and stronger domain control.
- **Better than fully bespoke everything**: we avoid rebuilding commodity endpoint-management features in PoC.

## 4) PoC scope to validate now

- 2-node demo path (dev machine + 1 VM).
- Agent registration + heartbeat.
- Remote install job submission from UI/API.
- Step-level execution pipeline (precheck -> install -> verify).
- At least one controlled failure path with rollback/compensation.
- Correlated observability (logs, metrics, traces) and audit trail.

## 5) Key design decisions to validate today

1. **Hybrid control plane**: custom Orchestrator + Agent, Ansible-inspired patterns.
2. **Agent pull-first** job model (push optional later).
3. **Installer support in PoC**: MSI + EXE first (MSIX later).
4. **Delivery semantics**: at-least-once + idempotent steps (not unrealistic exactly-once).
5. **Security baseline**: signed artifacts, RBAC, least privilege, auditable events.
6. **Observability baseline**: OpenTelemetry from day 1.
7. **Legacy strategy**: adapter-based coexistence first, rewrite later.

## 6) Tradeoffs and risks

- More custom control-plane ownership than pure off-the-shelf tooling.
- EXE/legacy installers require strict contracts to remain deterministic.
- Self-contained binaries simplify deployment but shift runtime patch responsibility to us.
- Reliability quality depends on disciplined retry/backoff/jitter and state-machine correctness.

## 7) What success looks like for this PoC

- Stakeholders can trigger a remote install and see deterministic end-to-end status.
- Failure mode is diagnosable in minutes, not hours.
- Rollback/compensation is demonstrated, not just described.
- Team agrees architecture is viable for phased expansion.

## 8) Specific asks from meeting participants

- Confirm if this PoC scope is approved as the baseline.
- Confirm priority package targets for PoC (which MSI/EXE packages first).
- Confirm non-negotiable security controls in PoC vs later hardening.
- Confirm observability backend preference for the lab/on-prem setup.

## 9) Recommended immediate next step after approval

- Lock phase-1 implementation backlog around: job model, agent contract, pipeline steps, telemetry schema, and one rollback scenario.
