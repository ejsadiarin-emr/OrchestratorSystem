# Distributed Installer Project — Unified Context (Phase 1)

## 1) Project Purpose and Outcome

Build a Windows-first, on-prem distributed installer platform that lets operators centrally install, update, rollback, cancel, and observe software deployments (called "Workloads") across multiple nodes on a LAN (including air-gapped environments), with no cloud dependency.

The project is proving three core outcomes in Phase 1:

1. Runtime behavior is deterministic and auditable.
2. Trust boundaries are explicit and testable.
3. Operators can diagnose and recover from failures quickly.

---

## 2) Product Direction and Core Shift

The platform has moved from a package-centric operating model to a **workload-first** model.

A workload is the first-class runtime object:

- WorkloadDefinition: stable logical identity.
- WorkloadRevision: immutable ordered package set (once published).
- WorkloadRun: execution request against targets.
- NodeWorkloadState: current applied revision and package states per node.

This shift exists to remove ambiguity in update behavior, improve operator ergonomics, and harden traceability.

---

## 3) Scope for PoC Phase 1

### In scope

- Single orchestrator, Windows-first deployment domain.
- Agent-based distributed execution on LAN.
- Workload lifecycle: install, update, rollback, cancel.
- Immutable workload revisions.
- Runtime sequencing, lease ownership, idempotent status ingest.
- Typed install adapters for MSI/EXE.
- Config snapshot/migration/restore contract.
- Internal artifact source and ingestion.
- Self-contained orchestrator binary with embedded UI.
- API/UI/CLI runtime operations.

### Out of scope (deferred)

- Multi-orchestrator HA/DR.
- Linux agents.
- Direct workstation deployment from CI/CD jobs.
- Automatic package removal during update.
- Automatic package dependency graph solving.
- Fleet-scale hardening workflows beyond PoC baseline.

---

## 4) Non-Negotiable Constraints

- Runtime operations are orchestrator-driven only (API/UI/CLI).
- Runtime artifact source is internal-only.
- SignalR is for control/status; artifact bytes move via HTTP endpoints.
- `/api/jobs` mutation endpoints are removed from the runtime API surface.
- New lifecycle actions must use `/api/workload-runs`.
- Workload revisions are immutable once published.
- Single active run per `(nodeId, workloadId)`.
- Update mode does not remove packages in Phase 1.
- Orchestrator runs as a self-contained executable on a clean Windows host (no preinstalled runtime/IIS).

---

## 5) Deterministic Lifecycle Semantics

Supported modes:

- `install`: execute all packages in revision order.
- `update`: execute changed packages only, preserving canonical order.
- `rollback`: execute approved rollback path using snapshot/restore contract.
- `cancel`: interrupt at safe boundaries and persist explicit reason code.

Determinism rules:

1. Run snapshots exact workload revision content at creation time.
2. Package order is fixed by published revision order.
3. Node revision promotes only after required steps fully succeed.
4. Replay/status ingestion is idempotent and sequence-aware.

---

## 6) Runtime Architecture (High Level)

### Components

- **Orchestrator**: API, runtime hub, planning/policy, persistence, embedded UI.
- **Agent**: persistent Windows service, local typed pipeline executor, with embedded UI.
- **Artifact store**: internal artifact source with digest/signature metadata.
- **Operator surfaces**: API/UI/CLI.

### Runtime sequence (canonical)

`Connect -> Register/Authenticate -> AssignRun -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

### Lease defaults

- TTL: 90s
- Heartbeat interval: 15s
- Stale threshold: 3 misses
- Bounded stale timeout: auto-fail after reassignment/time limits

---

## 7) API and Contract Posture

Primary Phase 1 API families:

- Workload definitions/revisions/publish/list/detail.
- Workload runs (create/get/steps/cancel).
- Node listing and enrollment.
- Artifact ingestion.

Legacy migration note:

- Runtime lifecycle actions use `/api/workload-runs` only.
- Historical docs may reference an earlier migration period where `/api/jobs` returned `410 Gone`.

Status ingest/idempotency:

- Upsert key includes run/node/package/step/sequence.
- Out-of-order and stale updates are rejected.
- Same-key payload mismatch is rejected and audited.
- Reconnect resumes from last acknowledged sequence + 1.

---

## 8) Artifact Ingestion and Manifest Resolution

Artifact ingestion is streaming multipart with:

- Required binary file and manifest.
- Optional detached signature.

Minimal required admin fields:

- packageId, version, channel, artifactType (unless inferable).

Deterministic field resolution chain:
`admin -> template -> analyzer -> default`

Policy/security outcomes:

- Trust verification `fail` => reject ingest (fail-closed).
- Trust verification `warn` => force elevated risk and approval-required posture.

Persisted manifest requirements:

- Schema-valid resolved record.
- Per-field provenance (`admin|template|analyzer|default`).
- Immutable digest/signature metadata and origin metadata.

---

## 9) Security Model

### Trust boundaries

- Admin caller -> Orchestrator API.
- Agent -> runtime channel.
- Orchestrator/Agent -> artifact source.
- Orchestrator -> audit/observability stores.

### Baseline controls

- Enrollment token bootstrap, then bound mTLS identity.
- RBAC authorization for runtime actions.
- Signature/hash verification for artifacts and executable trust checks.
- Least-privilege adapter execution with constrained argument handling.
- No plaintext secret storage.
- Tamper-evident audit linkage.
- Anti-downgrade/version-floor controls for signed binaries.

### Threat priorities (STRIDE-aligned)

- Spoofing (rogue node/operator path abuse)
- Tampering (artifact/binary substitution)
- Repudiation (missing actor-linked audit)
- Information disclosure (secret leakage)
- DoS (runtime flood/retry abuse)
- Elevation of privilege (unsafe process execution)

---

## 10) Observability and Diagnostics

Observability baseline:

- OpenTelemetry as canonical telemetry standard.
- Collector + operator-queryable stores (Loki/Grafana baseline in current Phase 1 posture).

Required correlation dimensions:

- workloadId, workloadRevision, runId, nodeId, packageId, stepId, sequence, reasonCode.

Operator requirement:

- Reconstruct package-step timeline and terminal reason codes from telemetry/audit evidence.
- File export may exist as fallback, but required operational queries must be available via the observability stack.

---

## 11) Config Mutation Safety Contract

Any mutating install/upgrade path must:

1. Create pre-mutation config snapshot.
2. Run deterministic migration chain only.
3. Restore snapshot on failure.
4. Emit linked audit evidence for snapshot/migration/restore outcomes.

---

## 12) Testing, Verification, and Done Criteria

Testing policy:

- Contract-first, deterministic, behavior-focused.
- Failure paths are first-class (equal rigor to happy path).
- Production bugs become regression tests.

Required layers:

- Unit (planning, sequencing, policies, idempotency, adapters).
- Integration (API/runtime/persistence/security contracts).
- E2E (operator flows).
- Fault/chaos (disconnect/reconnect, stale leases, checksum mismatch, retry exhaustion).
- Compatibility checks for legacy migration behavior (historical, non-runtime).

Phase 1 is done only when:

- All mapped functional and non-functional acceptance criteria have executable evidence.
- Security negative tests pass.
- Packaging and clean-host startup constraints are proven.
- Deferred hardening items are documented but not blocking.

---

## 13) DevOps Boundary and Release Policy

Pipeline responsibilities:

- CI, publish, packaging validation, orchestrator deploy, integration tests, E2E tests.

Hard boundaries:

- Pipeline deploys orchestrator only.
- Workstation runtime actions are always triggered through orchestrator APIs/CLI.
- No direct workstation deployment from CI jobs.
- No internet dependency for required release gates in on-prem/air-gap scenarios.

---

## 14) Implementation Program Status (Phase 1)

Execution model is dependency-ordered and workload-first:

- Contract freeze and migration map.
- Domain schema and workload APIs.
- Workload-run API and removal of `/api/jobs` from runtime surface.
- Artifact ingest validation/default/provenance logic.
- Runtime protocol + sequence/idempotency + lease manager.
- Agent service hardening + token-to-mTLS + ordered local pipeline.
- Config snapshot/migration/restore integration.
- Security baseline and observability MVP.
- UI/CLI workload surfaces.
- Self-contained packaging validation and final evidence closure.

Historical baseline tasks are completed for early contracts/persistence/API transport groundwork; the workload-first board drives current delivery.

---

## 15) Team Learning and Capability Ramp

The implementation strategy includes a structured capability ramp:

1. C#/.NET fundamentals, DI, generics, API testability.
2. React standalone development and UI testing.
3. Legacy installer ecosystem understanding (C++/InstallScript/VB6/.NET Framework).
4. Deployment/packaging/automation research in on-prem constraints.
5. Requirements formalization with deterministic/testable/modular/distributed principles.
6. Threat modeling and security architecture.
7. High-level architecture.
   8-9. Detailed design (core + cross-cutting concerns).
8. End-to-end CI/CD pipeline with unit/integration/E2E gates.

This learning plan is not separate from delivery; it directly feeds architecture, contracts, and implementation quality.

---

## 16) Guidance for AI Agents Working on This Project

- Treat workload-first semantics as canonical.
- Do not introduce new runtime dependency on scripts.
- Keep runtime flow contracts deterministic and idempotent.
- Treat legacy `/api/jobs` behavior as historical context only; do not reintroduce it into runtime APIs.
- Keep trust, auditability, and observability as first-class acceptance gates.
- Enforce Phase 1 boundaries; do not pull deferred hardening scope into critical path.
- Prioritize changes that map cleanly to acceptance evidence.
