# Dashboard Wireframes and UI Comparison Notes

Date: 2026-04-06

## UX goals

- Fast operator situational awareness.
- Immediate visibility of failures and blocked nodes.
- Step-level progress and logs without leaving the job view.
- Clear separation between command actions and audit evidence.

## Screen 1: Node Overview

Primary panels:

1. Node table (node ID, status, last heartbeat, current version, last job result)
2. Health badges (Healthy/Warning/Offline)
3. Quick filters (environment, group, online state)

Why this matters: operators first need to know "which machines are controllable right now".

## Screen 2: Job Submit

Fields:

- Target nodes/group
- Package and target version
- Action type (`install`, `upgrade`, `rollback`)
- Change ticket / reason field
- Dry-run or validation-only option (future)

Safety controls:

- warning banner for high-risk actions,
- explicit confirmation for multi-node action.

## Screen 3: Job Detail (Most Important)

Left column:

- current state,
- progress timeline,
- affected node list.

Right column:

- step-level execution logs,
- telemetry summary (latency, retries, error code),
- rollback section when applicable.

## Screen 4: Audit and Diagnostics

Show immutable-style event stream:

- who triggered,
- what was requested,
- when each state transition occurred,
- outcome and error taxonomy.

## UI comparison with Ansible-like experiences

### Borrow

- concise declarative job intent summary,
- inventory-centric targeting model,
- status clarity per node.

### Improve beyond typical infra-automation UI

- richer step-level diagnostics for installer-specific failures,
- explicit rollback narrative,
- first-class legacy adapter evidence fields,
- stronger operator-centric troubleshooting flow.

## PoC mockup fidelity recommendation

For current phase, low-fidelity wireframes are sufficient. Prioritize:

- information architecture,
- failure-state clarity,
- correlation ID visibility,
- auditability.

Visual polish can follow once workflow is validated.
