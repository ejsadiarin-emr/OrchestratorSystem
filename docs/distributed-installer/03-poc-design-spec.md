# PoC Design Spec: Distributed Installer Framework

Date: 2026-04-06  
Scope type: Internship PoC (implementation-bounded, decision-rich)  
Target stack: React + .NET 8 (upgrade-ready to .NET 10)

## 1. Goals and non-goals

## Goals

- Prove remote installation works across at least 2 nodes (dev + 1 VM).
- Demonstrate deterministic job execution with explicit state transitions.
- Demonstrate idempotent behavior for re-run requests.
- Demonstrate at least one rollback/compensation flow.
- Demonstrate observability and failure diagnosis with correlation IDs.
- Keep architecture compatible with future distributed cross-platform expansion.

## Non-goals (PoC)

- Full enterprise fleet-scale optimization.
- Full Linux agent implementation (design-ready only).
- Complete replacement of all legacy installer paths.
- Full production-grade HA and DR topology.

## 2. Functional scope (PoC)

## Included

- Agent registration and heartbeat.
- Submit install/upgrade job from UI and API.
- Job queue, assignment, execution, and status tracking.
- Step-level install pipeline with prechecks and post-verification.
- Adapter execution for MSI and EXE classes.
- Basic rollback/compensation flow.
- Audit trail and telemetry visibility in UI.

## Deferred

- Scheduling calendar sophistication.
- Multi-tenant boundary models.
- Complex dependency orchestration across many packages.
- Autonomous remediation policies.

## 3. Architecture overview

## Components

1. **Orchestrator API (.NET)**
   - Job intake, validation, assignment, and status APIs
   - RBAC and audit event writer
   - OTel root span creation and context propagation

2. **Orchestrator UI (React)**
   - Node list with health
   - Job submit form
   - Job detail with step timeline and logs
   - Failure and rollback indicators

3. **Agent service (.NET worker on Windows)**
   - Registration/heartbeat
   - Pull job claim and execution
   - Pipeline step runner + adapter invocations
   - Telemetry emitter

4. **State store**
   - Job and step state
   - Node state and heartbeat metadata
   - Audit events

5. **Package source**
   - Internal UNC/HTTPS artifact source
   - Signed artifact and checksum metadata

6. **Telemetry pipeline**
   - OTel collector + backends (logs/metrics/traces)

## 4. Job lifecycle and state machine

## State definitions

- `Queued`: accepted and validated
- `Assigned`: claimed by agent
- `PrecheckFailed`: environment prerequisite failed
- `Installing`: one or more steps executing
- `VerifyFailed`: install happened but verification failed
- `Failed`: terminal failure without successful rollback
- `RollbackInProgress`: compensation/rollback running
- `RolledBack`: rollback completed
- `Succeeded`: full success
- `Cancelled`: manually aborted before terminal success

## Transition rule

No state transition may occur without:

- timestamp,
- actor/system identity,
- correlation ID,
- reason code.

## 5. Pipeline design

Use modular step contracts similar to:

1. `PreConditionCheck`
2. `AcquireArtifact`
3. `ValidateSignatureAndHash`
4. `DetectCurrentState`
5. `InstallOrUpgrade`
6. `PostInstallVerify`
7. `EmitFinalization`

Optional compensation step chain:

- `RollbackOrCompensate`
- `RollbackVerify`

Each step must emit telemetry and structured outcome.

## 6. Manifest contract (Ansible-inspired, C#-typed execution)

Manifest should define:

- package identity and target version,
- artifact location and integrity metadata,
- execution mode (`install`, `upgrade`, `rollback`),
- detection rules,
- install arguments profile,
- rollback/compensation strategy,
- expected return codes,
- reboot policy handling.

Manifest is data-driven, not script-driven.

## 7. Security model (PoC baseline)

- Mutual trust boundary between orchestrator and agent.
- Operator authentication and role authorization.
- Signed artifact and checksum verification pre-execution.
- Least-privilege execution where feasible; elevated actions explicit and audited.
- Secrets not stored in plaintext in config/logs.
- Append-only audit events with integrity-friendly schema.

## 8. Observability model

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

Use structured JSON logs with correlation IDs and no sensitive payload leakage.

## 9. Test strategy requirements

PoC acceptance requires:

- unit tests around pipeline orchestration and step decisions,
- integration tests for orchestrator-agent contract,
- E2E tests for key operator flow (submit -> observe -> terminal state),
- one failure-path test proving rollback/compensation.

## 10. Demo scenarios

## Scenario A: happy path install

- Submit valid package manifest
- Observe queue -> assigned -> installing -> succeeded
- Verify telemetry and audit entries

## Scenario B: controlled failure + rollback

- Trigger failure in install or verify phase
- Observe rollback execution
- Confirm terminal consistent state and operator-facing diagnostics

## Scenario C: replay/idempotency

- Re-submit same idempotency key or equivalent request
- Show no duplicate harmful side effects

## 11. Known PoC limitations

- Not designed for large-scale concurrency yet.
- Adapter coverage limited to key installer types only.
- No formal multi-region/failover topology.
- Linux agent is architectural placeholder for later phase.

## 12. Future-phase roadmap (aligned to vision)

1. Add Linux agent and cross-platform step abstraction.
2. Add richer policy engine (rings/canary/maintenance windows).
3. Add stronger artifact provenance/SBOM enforcement.
4. Add scale-oriented queueing and partitioning strategies.
5. Add advanced diagnostics and self-healing playbooks.
