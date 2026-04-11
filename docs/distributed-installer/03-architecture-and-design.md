# Architecture and Design: Distributed Installer Framework

Date: 2026-04-07  
Scope: Internship PoC (implementation-bounded, decision-rich)  
Target stack: React + .NET 8 (upgrade-ready to .NET 10)

## 1. Goals and non-goals

## Goals

- Prove remote installation works across at least 2 nodes (dev + 1 VM)
- Demonstrate deterministic job execution with explicit state transitions
- Demonstrate idempotent behavior for re-run requests
- Demonstrate at least one rollback/compensation flow
- Demonstrate observability and failure diagnosis with correlation IDs
- Keep architecture compatible with future distributed cross-platform expansion

## Non-goals (PoC)

- Full enterprise fleet-scale optimization
- Full Linux agent implementation (design-ready only)
- Complete replacement of all legacy installer paths
- Full production-grade HA and DR topology

## 2. Functional scope (PoC)

## Included

- Agent bootstrap via WinRM (one-time push install)
- Agent registration and heartbeat via SignalR
- Submit install/upgrade job from UI and API
- Job queue (Hangfire), assignment, execution, and status tracking
- Step-level install pipeline with prechecks and post-verification
- Adapter execution for MSI and EXE classes
- Basic rollback/compensation flow
- Audit trail and telemetry visibility in UI

## Deferred

- Scheduling calendar sophistication
- Multi-tenant boundary models
- Complex dependency orchestration across many packages
- Autonomous remediation policies

## 3. System overview

## Components

### 3.1 Orchestrator (ASP.NET Core)

Single ASP.NET Core application hosting:

- **REST API** — job intake, node management, status queries (UI-facing)
- **SignalR Hub** — real-time bidirectional channel to agents (commands, events, heartbeats)
- **Hangfire server** — persistent job queue with retries, scheduling, and dashboard
- **SQL Server** — job state, node registry, audit events, Hangfire storage

### 3.2 Agent (Windows Service)

Lightweight persistent Windows service on each target machine:

- **SignalR client** — maintains connection to orchestrator hub
- **Channel<T> + BackgroundService** — in-memory job buffer and executor
- **Child process spawner** — isolated execution context per install job
- **OTel instrumentation** — traces, metrics, logs per pipeline step

### 3.3 React UI

- Node list with health status
- Job submission form
- Job detail with step timeline and live logs (SignalR push)
- Failure and rollback indicators
- Hangfire dashboard (embedded or linked)

### 3.4 Package source

- Internal UNC or HTTPS artifact repository
- Signed artifacts with checksum metadata

### 3.5 Telemetry pipeline

- OTel collector + backends (logs/metrics/traces)

## 4. Communication architecture

### 4.1 Agent ↔ Orchestrator: SignalR

- Persistent WebSocket connection from agent to orchestrator SignalR hub
- Orchestrator pushes job assignments and operational commands after agent connection/auth
- Agent pushes heartbeats, status updates, and log streams
- Built-in automatic reconnection with exponential backoff
- Connection state tracked in orchestrator (online/offline/last-seen)

Canonical runtime protocol sequence:

`Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

Lease and stale-assignment defaults (PoC):

- Lease TTL: `90s`
- Heartbeat interval: `15s`
- Stale threshold: `3` missed heartbeats
- Stale state: `AssignedStale`
- Stale timeout bound: auto-fail with `lease_timeout_exhausted` after 2 reassignment attempts or 15 minutes total stale duration

### 4.2 UI ↔ Orchestrator: REST + SignalR

- REST API for CRUD operations (jobs, nodes, manifests)
- SignalR for live job progress updates and log streaming to UI

### 4.3 Bootstrap: WinRM (PoC)

- One-time push installation of agent onto fresh machines
- Remote PowerShell script downloads agent binary, registers Windows service, starts it
- After bootstrap, agent connects to orchestrator via SignalR and never uses push again

## 5. Agent model

Agents are **persistent lightweight services** (not ephemeral VMs), following the GitHub Actions runner model:

1. Agent runs as a Windows service on each target machine
2. Maintains a persistent SignalR connection to the orchestrator
3. Receives individual job assignments (not full DAGs)
4. Spawns isolated child processes for each job execution
5. Reports results back to orchestrator

This means: no spin-up/spin-down lifecycle per job. The agent is always present, always connected, always ready.

## 6. Job lifecycle and state machine

## State definitions

- `Queued`: accepted and validated by orchestrator
- `Assigned`: claimed by agent via SignalR
- `AssignedStale`: prior lease holder timed out; safe reassignment policy applies
- `Prechecking`: running dry-run validation
- `PrecheckPassed`: all prerequisites satisfied
- `PrecheckFailed`: environment prerequisite failed
- `Downloading`: fetching artifacts from package source
- `Installing`: executing install steps
- `Verifying`: post-install verification
- `VerifyFailed`: install succeeded but verification failed
- `Failed`: terminal failure without successful rollback
- `RollbackInProgress`: compensation/rollback running
- `RolledBack`: rollback completed
- `Succeeded`: full success
- `Cancelled`: manually aborted before terminal success

`AssignedStale` transitions to terminal `Failed` with reason code `lease_timeout_exhausted` when stale-timeout bounds are exceeded.

## Transition rule

No state transition may occur without:

- timestamp
- actor/system identity
- correlation ID
- reason code

## 7. Pipeline design

Modular step contracts executed sequentially:

1. `PreConditionCheck` — dry-run validation (OS version, disk space, dependencies)
2. `AcquireArtifact` — download from package source
3. `ValidateSignatureAndHash` — cryptographic verification
4. `DetectCurrentState` — check if already installed (idempotency)
5. `InstallOrUpgrade` — execute installer adapter
6. `PostInstallVerify` — confirm installation succeeded
7. `EmitFinalization` — emit final telemetry and audit events

Optional compensation step chain:

- `RollbackOrCompensate`
- `RollbackVerify`

Each step must emit telemetry and structured outcome.

### 7.1 Upgrade config persistence contract (PoC)

Upgrade executions are contractually bound to config safety requirements:

- Capture pre-mutation config snapshot (`configSnapshotId`).
- Run deterministic migration path (`vN -> vN+1`) only.
- Restore snapshot on migration/verification failure.
- Emit audit-linked config events for snapshot, migration, and restore outcomes.

Canonical contract source: `docs/distributed-installer/11-config-persistence-contract.md`.

## 8. Manifest contract (Ansible-inspired, C#-typed execution)

Manifest defines:

- package identity and target version
- artifact location and integrity metadata
- execution mode (`install`, `upgrade`, `rollback`)
- detection rules
- install arguments profile
- rollback/compensation strategy
- expected return codes
- reboot policy handling
- per-job targeting rules (OS version, architecture, capabilities)

Manifest is data-driven, not script-driven.

## 9. Machine heterogeneity

Machines do **not** need the same configuration. The system handles heterogeneity through:

- **Per-job targeting**: manifests specify which machines/groups receive a job
- **Detection rules**: agents report capabilities during registration (OS version, architecture, installed software, disk space)
- **Pre-check validation**: each job runs environment-specific checks before execution
- **Conditional steps**: pipeline steps can be skipped based on detection results

## 10. Security model (PoC baseline)

- Mutual trust boundary between orchestrator and agent (SignalR + certificate/credential)
- Operator authentication and role authorization
- Signed artifact and checksum verification pre-execution
- Least-privilege execution where feasible; elevated actions explicit and audited
- Secrets not stored in plaintext in config/logs
- Append-only audit events with integrity-friendly schema

## 11. Observability model

## Trace model

- Root span: `installer.job`
- Child spans: one per pipeline step
- Mandatory attributes:
  - `job.id`
  - `node.id`
  - `package.id`
  - `package.version`
  - `step.name`
  - `result.status`
  - `error.code` (if any)

## Metrics

- `installer.job.duration`
- `installer.step.duration`
- `installer.job.failure.count`
- `installer.job.retry.count`
- `agent.heartbeat.latency`
- `orchestrator.queue.depth`

## Logs

Structured JSON logs with correlation IDs and no sensitive payload leakage.

## 12. Demo scenarios

## Scenario A: happy path install

- Submit valid package manifest
- Observe queue -> assigned -> precheck passed -> installing -> succeeded
- Verify telemetry and audit entries

## Scenario B: controlled failure + rollback

- Trigger failure in install or verify phase
- Observe rollback execution
- Confirm terminal consistent state and operator-facing diagnostics

## Scenario C: replay/idempotency

- Re-submit same idempotency key or equivalent request
- Show no duplicate harmful side effects

## 13. Known PoC limitations

- Not designed for large-scale concurrency yet
- Adapter coverage limited to key installer types only
- No formal multi-region/failover topology
- Linux agent is architectural placeholder for later phase

## 14. Future-phase roadmap

1. Add Linux agent and cross-platform step abstraction
2. Add richer policy engine (rings/canary/maintenance windows)
3. Add stronger artifact provenance/SBOM enforcement
4. Add scale-oriented queueing and partitioning strategies
5. Add advanced diagnostics and self-healing playbooks
