# Deep Research Report: Distributed Installer Framework PoC

Generated: 2026-04-06  
Scope: Emerson internship PoC (Windows-first, air-gapped-capable, hybrid control plane)  
Environment assumption: dev machine + 1 VM  
Confidence: High on Windows/.NET installer architecture patterns; Medium on future cross-platform rollout details

## Executive summary

The best-fit PoC architecture is a **hybrid distributed installer**: a custom .NET Orchestrator + lightweight Agent, using **Ansible-inspired declarative idempotency patterns** but avoiding heavy dependence on Ansible as the execution substrate.

For your current constraints, this model gives the best balance of:

- practical PoC delivery speed,
- control over Emerson-specific legacy installers,
- auditable security boundaries,
- replayability and rollback,
- future extensibility toward true multi-node distributed operation.

The primary risk in this domain is not coding complexity alone; it is **operational correctness under failure** (partial installs, retries, reboots, network partitions, installer side effects, credential boundaries). Therefore, the architecture should prioritize deterministic state transitions, artifact trust verification, and telemetry-first execution.

## Problem framing in enterprise terms

Your pain point maps to a common enterprise gap:

- legacy installer pipelines can install software, but are difficult to orchestrate uniformly across many machines,
- remote install support is often fragmented,
- observability is weak, so failures are expensive to diagnose,
- modernization is slow because legacy components cannot be replaced all at once.

The PoC should prove that you can modernize the control plane **without forcing immediate rewrite of all installers**.

## Core architecture decision

Use a **job-oriented orchestrator-agent model** with explicit state machine and adapter-based installer execution.

### Recommended topology

- **Orchestrator** (ASP.NET Core API + embedded React UI + job store)
- **Agent** (Windows service on target node, pulls jobs, executes install pipeline)
- **Package source** (internal UNC or HTTPS mirror in LAN)
- **Event/telemetry pipeline** (OpenTelemetry collectors/backends on-prem)

### Why this is superior for PoC

1. **Aligns with your stack**: C#/.NET and React end-to-end.
2. **Supports air-gapped operation**: LAN-only dependencies.
3. **Handles legacy safely**: adapter steps can wrap MSI/EXE/custom installers.
4. **Gives replayability**: job state and per-step outcomes are persisted.
5. **Enables gradual migration**: wrap first, rewrite later.

## Push vs pull: control transport choice

### Recommended default: pull by agents

Agents periodically heartbeat and poll/subscribe for jobs.

Benefits:

- easier through restricted network/firewall boundaries,
- cleaner trust model in segmented on-prem networks,
- resilient reconnection after temporary outages.

### When push is still useful

- urgent operational actions (high-priority hotfix),
- tightly controlled same-segment environments.

Tradeoff conclusion: implement pull-first in PoC; keep push as future optional mode.

## Packaging and installer modality strategy

No single installer format covers all enterprise realities.

### PoC package support set

- **MSI**: first-class support (strong enterprise fit, logging, rollback hooks)
- **EXE**: supported via explicit detection + controlled argument profiles
- **MSIX**: design-ready but optional in PoC implementation

### Why not MSIX-only

MSIX is operationally clean, but many legacy and specialized enterprise installers remain MSI/EXE/custom and cannot be migrated quickly.

### Why not EXE-only

EXE-only orchestration lacks standardized metadata and robust uniform detection semantics; idempotency/replay is harder.

## Idempotency and replayability model

Treat idempotency as a contract, not a best effort.

### Execution contract

Each install job should carry deterministic identity keys:

- target node ID
- package ID
- target version
- requested action (`install`, `upgrade`, `rollback`, `uninstall`)
- correlation/request ID

Before apply, agent runs **detect phase**:

- if desired state already true, mark step/job as no-op success,
- if not, execute install with controlled retries.

### Replay semantics

Re-running same request with same idempotency key should not create duplicate side effects.

### Rollback semantics

- MSI: leverage installer rollback + uninstall path where possible.
- EXE/custom: require compensating action definition in manifest (explicitly mark confidence level).

## Background update strategy for self-contained binaries

For Orchestrator/Agent self-updates in restricted networks:

1. Stage signed artifacts internally (UNC/HTTPS).
2. Download in background using resilient transfer (BITS-friendly approach on Windows).
3. Validate hash/signature before swap.
4. Perform atomic replacement or controlled service restart.
5. Run health check; if failed, auto-revert to previous binary.

This pattern gives Discord-like update feel while preserving enterprise safety and auditability.

## State machine baseline (must-have)

Use explicit job state transitions:

- `Queued`
- `Assigned`
- `PrecheckPassed` / `PrecheckFailed`
- `Installing`
- `Verifying`
- `Succeeded`
- `Failed`
- `RollbackInProgress`
- `RolledBack`
- `Cancelled`

Each transition must emit audit + telemetry events.

## Security controls: PoC minimum vs production target

### PoC minimum (must implement)

- agent-orchestrator mutual identity (certificate or equivalent constrained credential model),
- role-based authorization in orchestrator API/UI,
- signed package and checksum verification before execute,
- least-privilege execution context for steps where possible,
- append-only audit event trail with actor + correlation IDs.

### Production hardening (future)

- hardware-backed key protection where available,
- stronger supply-chain attestations (SBOM/provenance),
- immutable/WORM-class audit retention,
- stricter policy governance and rotation cadence.

## Observability baseline (non-negotiable)

Every pipeline step should emit:

- trace span (`installer.step` with step name),
- duration metric (histogram),
- outcome status and error code,
- node, package, version, job, and correlation IDs.

At minimum, capture:

- job latency percentiles,
- step failure rates by step type,
- retry counts and exhaustion events,
- queue depth and staleness,
- rollback trigger/success rates.

## Legacy interop strategy

Use **adapter wrappers** around legacy components (C++/VB/InstallScript-era outputs) in pipeline steps.

Adapter contract should force:

- typed parameters,
- sanitized process invocation,
- standardized exit-code mapping,
- detection/verification hooks,
- telemetry emission.

This avoids large-bang rewrites and allows phased modernization.

## Decision matrix for your PoC choices

| Decision | Recommended PoC choice | Why |
|---|---|---|
| Control plane | Custom .NET Orchestrator + Agent | Emerson-specific behavior and long-term ownership |
| Distribution mode | Agent pull-first | Better resilience in constrained enterprise networks |
| Package support | MSI + EXE (MSIX optional) | Fastest realistic legacy-compatible scope |
| Job model | Durable state machine + event log | Needed for replay, rollback, and observability |
| Update model | Staged signed self-update with health-check rollback | Safe background updates in air-gapped LAN |
| Telemetry | OTel traces/metrics/logs from day 1 | Failure diagnosis and trust for distributed control |

## PoC demonstration blueprint (dev + 1 VM)

Use a scenario that proves real value with minimal scope:

1. Register one agent VM.
2. Submit install job from UI/API.
3. Agent performs precheck -> install -> verify.
4. Show step-level live status and correlated logs.
5. Trigger controlled failure path (bad package or check failure).
6. Demonstrate rollback/compensation and final consistent state.

If this works, your PoC already validates the architecture direction.

## Key tradeoffs to be transparent about

- Building custom control plane increases ownership cost but gives Emerson-fit behavior.
- Supporting EXE/custom installers increases edge cases; strict manifest contracts are required.
- Self-contained binaries simplify prerequisites but require disciplined update/patch lifecycle.
- Exactly-once execution is unrealistic in distributed installs; use at-least-once + idempotency.

## Risks and mitigations

### Risk: legacy installer unpredictability
Mitigation: strict adapter contracts, sandbox testing, known-good profiles.

### Risk: privilege escalation exposure
Mitigation: least privilege, constrained execution policies, audited command construction.

### Risk: retry storms during outages
Mitigation: bounded exponential backoff + jitter, queue controls, circuit-breaking logic.

### Risk: opaque failures
Mitigation: mandatory OTel instrumentation for every step.

## Recommended source highlights

Primary evidence base used included:

- Microsoft docs on .NET self-contained/single-file publishing, MSI rollback, WinGet sources, BITS.
- OpenTelemetry guidance on resiliency and queue/WAL patterns.
- Ansible Windows management and `win_package` behavior.
- Reliability patterns on retries/backoff/jitter (AWS Builders’ Library).
- Additional market and tool landscape synthesis from parallel research.

See `02-market-and-ansible-comparison.md` and `05-security-reliability-observability.md` for deeper sourced sections.
