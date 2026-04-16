# PoC Phase 1 Final PRD

Date: 2026-04-14  
Status: Canonical source of truth for PoC Phase 1

## Purpose

This document is the canonical source of truth for PoC Phase 1 direction and governance. It consolidates functional and non-functional requirements, user stories, implementation decisions, and acceptance criteria into a single traceable artifact. If conflicts exist across documentation, this PRD wins.

Implementation phasing tags used in this document:

- `[PoC Phase 1]` required before implementation-plan signoff
- `[Hardening Phase 2]` explicitly deferred to post-PoC hardening

ID conventions:

- `FR-###` Functional requirement
- `NFR-###` Non-functional requirement
- `AC-###` Acceptance criteria linked to FR/NFR

---

## Problem Statement

Teams need a Windows-first distributed installer flow that can safely install, update, modify, rollback, and observe software deployment across remote nodes from a central orchestrator. Today, the risk is inconsistent behavior across nodes, weak traceability during failures, and unclear runtime security boundaries (artifact trust, child-process execution trust, and agent identity lifecycle). The PoC must prove that installation operations are reliable, auditable, and operationally understandable without requiring enterprise-scale hardening scope in Phase 1.

---

## Solution

Build a single-orchestrator, Windows-first PoC where admins submit jobs via API/UI/CLI and agents execute a local typed pipeline with deterministic runtime contracts. Use SignalR for control/status only and HTTP artifact endpoints for package transfer. Enforce package trust (signature/hash), bootstrap token to mTLS steady-state identity, policy-tagged retry/idempotency/risk handling, and step-level telemetry/audit evidence. Keep scope intentionally constrained to Phase 1 goals and defer hardening/scale extensions to Phase 2.

---

## User Stories

1. As a system administrator, I want to install software to one or more target nodes from one orchestrator, so that rollout is centralized and consistent.
2. As a system administrator, I want to trigger upgrades with explicit version intent, so that I can move workloads from one validated version to another.
3. As a system administrator, I want to request rollback/cancel operations with auditable transitions, so that failed changes can be safely contained.
4. As a system administrator, I want to bootstrap a new agent via a simple manual script in PoC, so that onboarding is fast and practical.
5. As a security reviewer, I want one-time enrollment token usage and mTLS steady-state identity, so that agent identity is not based on reusable bootstrap secrets.
6. As a system administrator, I want package ingestion to happen internally, so that agents do not depend on external package sources at runtime.
7. As a platform engineer, I want orchestrator package delivery to support large payloads over HTTP range/chunk retrieval, so that artifact transport is scalable and reliable.
8. As a runtime engineer, I want SignalR used only for control/status messaging, so that command channel behavior stays deterministic and bounded.
9. As a reliability engineer, I want canonical runtime sequencing and idempotency enforcement, so that replays and reconnects do not corrupt state.
10. As an operator, I want a live step timeline for each job, so that I can see exactly what is running, what failed, and why.
11. As a security reviewer, I want unsigned or tampered artifacts blocked by policy, so that malicious binaries cannot execute.
12. As a platform engineer, I want child process execution constrained and auditable, so that installer processes cannot silently escalate risk.
13. As a release engineer, I want update flows to capture pre-mutation snapshots, so that failed modifications can restore known-good state.
14. As an operator, I want downgrade operations treated as high risk with explicit approval policy, so that unsafe reversions are not automatic.
15. As a system owner, I want retry behavior to be policy-driven and bounded, so that transient failures self-heal without causing destructive loops.
16. As a system owner, I want non-idempotent/high-risk steps to avoid blind auto-retry, so that side effects are controlled.
17. As an auditor, I want job, step, and identity events persisted with correlation keys, so that I can reconstruct who did what, where, and when.
18. As a frontend user, I want dashboard visibility into node health and job outcomes, so that I can operate without checking backend internals.
19. As a CLI user, I want runtime actions available through CLI calls to orchestrator APIs, so that operations are automatable without script-only orchestration logic.
20. As a deployment owner, I want the orchestrator distributed as a self-contained executable with embedded UI, so that clean-host startup is simple.
21. As a product owner, I want strict Phase 1 scope boundaries, so that PoC delivery is not delayed by Phase 2 hardening concerns.
22. As a maintainer, I want a canonical PRD and tracker, so that documentation conflicts do not create implementation drift.
23. As a security engineer, I want trust boundaries documented with control mapping, so that architecture risks are explicit and testable.
24. As a QA engineer, I want acceptance criteria mapped to executable evidence, so that signoff is objective.
25. As an architect, I want single-orchestrator assumptions explicit in Phase 1, so that HA/multi-orchestrator expectations do not leak into PoC commitments.
26. As an operations lead, I want Ping and LeaseHeartbeat semantics explicitly separated, so that liveness and lease ownership behavior are not confused.
27. As a developer, I want contract-first message and API definitions, so that orchestrator-agent integration is stable across components.
28. As a product stakeholder, I want clear deferred items listed, so that future-phase work is visible without blocking current delivery.

---

## Scope and Assumptions

### In Scope (PoC Phase 1)

The following capabilities are in scope for PoC Phase 1 implementation:

- Distributed install orchestration across multiple Windows nodes on LAN.
- Agent bootstrap provisioning via WinRM (PoC), with enterprise channels (GPO/SCCM) as supported alternatives.
- Agent runtime communication over SignalR with claimed assignment and lease semantics.
- Full per-job pipeline execution on agent with orchestrator-owned job-level policy and dependency sequencing.
- Dry-run confidence gating, idempotent handlers, and rollback/compensation paths.
- Self-contained orchestrator executable with embedded React UI.

### Out of Scope (PoC Phase 1)

The following capabilities are explicitly excluded from Phase 1:

- Direct workstation package deployment from Azure DevOps pipelines.
- Linux agent implementation (design-ready only; implementation deferred).
- SQL Server-grade package as first realism target (deferred to Phase 2).
- Full fleet-scale high-availability/disaster-recovery and advanced rollout policy rings.

### Assumptions

The following assumptions must hold for PoC success:

- PoC environment permits bootstrap remoting privileges on target machines.
- SQL Server is available for orchestrator state and queue persistence.
- Artifact signing and trust chain can be managed on-premises.
- Medium-confidence deployments may require operator confirmation before proceeding.

---

## Implementation Decisions

- **Architecture scope**
  - Phase 1 is Windows-first, single-orchestrator, and implementation-focused on proving end-to-end install/update/modify safety and observability.
  - Linux agent support and multi-orchestrator scale behavior are deferred to Phase 2.

- **Operational model**
  - Orchestrator owns job-level policy, assignment, and state transitions.
  - Agent executes full local typed pipeline for each assignment and emits step-level status.

- **Runtime transport boundaries**
  - SignalR is used for control-plane messages (assignments, claims, heartbeats, status updates).
  - Artifact transfer is HTTP endpoint-based; large payloads use chunk/range retrieval.

- **Canonical runtime contract**
  - Required sequence: `Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`.
  - Step status ingestion uses idempotent keys and replay/conflict guards.
  - Reconnect semantics resume from last acknowledged sequence contract.

- **Core modules (deep-module oriented)**
  - `Runtime Protocol Module`: encapsulates sequence validation, idempotent ingest, replay/conflict detection, and reconnect behavior.
  - `Lease and Ownership Module`: encapsulates lease TTL, heartbeat policy, stale detection, and reassignment/timeout handling.
  - `Agent Execution Pipeline Module`: encapsulates ordered install-step execution and adapter normalization across MSI/EXE/custom wrappers.
  - `Artifact Trust Module`: encapsulates signature/hash verification and fail-closed enforcement before execution.
  - `Identity and Enrollment Module`: encapsulates token enrollment, token consumption rules, certificate binding, and steady-state mTLS identity enforcement.
  - `Snapshot and Restore Module`: encapsulates pre-mutation config snapshot, migration path validation, restore-on-failure, and audit linkage.
  - `Policy Evaluation Module`: encapsulates retryability, idempotency mode, risk level, and approval gating decisions.
  - `Audit and Telemetry Module`: encapsulates step/job correlation events, trace linkage, and operator-consumable evidence.

- **Data and persistence decisions**
  - Phase 1 persistence baseline is SQLite for control-plane entities.
  - Canonical entities include Job, Node, AssignmentLease, and ConfigSnapshot.

- **Artifact source and ingestion policy (Phase 1)**
  - Agents install runtime/software packages from the orchestrator artifact store only.
  - Direct external runtime download from agents at execution time is not allowed.
  - Orchestrator/operator may acquire installer media from official vendor sources, then ingest into artifact store before runtime execution.
  - Upstream/vendor binaries are ingested into the orchestrator artifact store as immutable package artifacts (for example `.exe`, `.msi`, `.zip`, `.tar.gz`), with metadata and policy attached.
  - Installer media are file artifacts (single file per upload request), not folders.
  - Large media uploads use streaming `multipart/form-data` to `POST /api/artifacts`; agent retrieval uses `GET/HEAD` + `Range` for chunked download.
  - Resumable/chunked upload sessions are deferred to Phase 2 unless explicitly added as a separate endpoint.
  - Artifact storage backend for Phase 1 is local filesystem on the orchestrator host; object storage is a Phase 2 migration option.

- **Package trust, origin metadata, and attestation policy**
  - Vendor binaries are not re-signed as vendor binaries.
  - At ingest time, orchestrator verifies available vendor trust evidence (signature/checksum), computes canonical digest, and records origin metadata.
  - Manifest metadata may originate from admin-declared values, vendor metadata sources, and/or binary-derived extraction; ingest pipeline records source-confidence semantics (`declared`, `derived`, `verified`) where applicable.
  - Immutable digest means a stored content hash that cannot be changed for a given package version record.
  - Provenance means origin metadata (source URL/repository, vendor/publisher identity if available, ingest time, operator/process identity, and verification result).
  - Optional org attestation signs manifest/bundle metadata (not the vendor binary) using organization-controlled key material, so agents verify organizational approval in addition to binary integrity.

Example ingest response (API contract shape):

```json
{
    "artifactId": "a2b7d5e4-3e02-4dca-b2f2-20c630913e19",
    "packageId": "dotnet-runtime",
    "version": "8.0.4",
    "channel": "stable",
    "artifactUrl": "/api/artifacts/dotnet-runtime/8.0.4",
    "artifactType": "exe",
    "sizeBytes": 123456789,
    "digest": {
        "algorithm": "sha256",
        "value": "<immutable-content-hash>"
    },
    "signatureVerification": "pass",
    "createdAtUtc": "2026-04-16T09:30:00Z"
}
```

- **Package and job policy model**
  - Package/job manifests include integrity metadata and execution policy tags.
  - Package channel taxonomy for Phase 1 is explicit: `stable`, `canary`, `test` with immutable version identity and hash-bound metadata.
  - Downgrade is high risk by default and must require explicit approval policy.
  - Unknown package risk posture defaults to high-risk.

- **Security and trust boundaries**
  - Artifact trust checks and RBAC are mandatory runtime gates.
  - Child process invocation must run under constrained policy with auditable outcomes.
  - Trust boundaries and mitigation mapping are required artifacts for Phase 1 signoff.

- **Self-update handling**
  - Orchestrator self-update follows staged swap + supervisor/wrapper pattern (not naive in-place overwrite behavior).

- **Operator surfaces**
  - Runtime operations are exposed via API/UI/CLI.
  - Script-based orchestration is provisioning-only in PoC, not canonical runtime operation surface.

- **Execution and failure semantics**
  - Execution model is serial per node (step-by-step within a node) and parallel across nodes with bounded concurrency for PoC.
  - Retry is bounded and transient-only.
  - Non-idempotent/high-risk steps do not blind auto-retry.
  - On mid-step failure, agent emits failure status, executes local rollback/restore when rollback contract exists, and reports rollback outcome; orchestrator is source of truth for final job state and audit trail.

---

## Functional Requirements

| ID | Requirement | Priority | Rationale | Linked AC IDs |
|---|---|---|---|---|
| FR-001 | [PoC Phase 1] The system must support orchestrator-triggered installation, upgrade, rollback, cancel, and status-query workflows across multiple target nodes | Must | Core distributed installer capability | AC-001, AC-002 |
| FR-002 | [PoC Phase 1] Runtime dispatch must follow the canonical sequence: `Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose` | Must | Removes protocol ambiguity and ensures reliable ownership | AC-003 |
| FR-003 | [PoC Phase 1] Agent must execute the full per-job pipeline locally; the orchestrator must own job-level sequencing, dependencies, and policy only | Must | Resolves DAG ownership ambiguity and reduces chattiness between components | AC-004 |
| FR-004 | [PoC Phase 1] Bootstrap provisioning must support transactional rollback and cleanup on failure | Must | Prevents partial bootstrap state and enables safe recovery | AC-005 |
| FR-005 | [PoC Phase 1] The system must support heterogeneous installer targets via a typed pipeline with a legacy adapter strategy (MSI/EXE/custom wrappers) | Should | Required for DeltaV modernization coexistence path | AC-006 |
| FR-006 | [PoC Phase 1] Upgrade flow must include config snapshot, deterministic migration, and restore-on-failure behavior | Must | Upgrade credibility and rollback safety | AC-007 |

---

## Non-Functional Requirements

| ID | Requirement | Category | Target/Constraint | Linked AC IDs |
|---|---|---|---|---|
| NFR-001 | [PoC Phase 1] Delivery semantics must be at-least-once with idempotent handlers and bounded stale-job policy | Reliability | Lease TTL: 90s; heartbeat interval: 15s; stale after 3 missed heartbeats; `AssignedStale` auto-fails after 2 reassignment attempts or 15 minutes total stale duration | AC-101 |
| NFR-002 | [PoC Phase 1] Security baseline must enforce on-premises-first secrets management, package integrity checks, RBAC, and bootstrap-token-to-mTLS steady-state agent authentication | Security | No plaintext secrets in storage; DPAPI/cert store/credential manager for secrets; signature+hash validation required; one-time enrollment token then per-agent mTLS certificate identity | AC-102 |
| NFR-003 | [PoC Phase 1] Runtime orchestration and telemetry must remain deterministic and diagnosable at step level | Observability | Every step emits trace, metrics, and log correlation fields | AC-103 |
| NFR-004 | [PoC Phase 1] Automation surface must be C# plus REST/CLI plus manifests; scripts are a provisioning-only exception | Operability | No script-based runtime orchestration surface allowed | AC-104 |
| NFR-005 | [PoC Phase 1] Orchestrator packaging must be a self-contained single executable that includes the API host and embedded React UI assets; a system admin can run it on their machine without pre-installed .NET runtime or IIS | Deployment | `dotnet publish --self-contained` with single-file packaging for orchestrator distribution | AC-105 |

---

## Acceptance Criteria

| ID | Linked Req IDs | Criteria (testable statement) | Validation Method |
|---|---|---|---|
| AC-001 | FR-001 | Operator can submit install and upgrade jobs to selected nodes through API/UI and observe terminal state | Integration/E2E |
| AC-002 | FR-001 | Rollback and cancel actions are reflected with auditable state transitions | Integration |
| AC-003 | FR-002 | Protocol messages include assignment/lease/sequence fields; status updates are idempotent upserts keyed by `(jobId, stepId, sequence)`; stale or out-of-order updates are rejected; same-key payload mismatch is rejected and audited | Unit/Integration |
| AC-004 | FR-003 | Agent executes full job pipeline locally while orchestrator tracks job-level dependency and policy only | Integration |
| AC-005 | FR-004 | Bootstrap failure triggers reverse-order cleanup (service/files/config/token/audit) | Integration/Manual |
| AC-006 | FR-005 | MSI/EXE adapter steps can run through typed pipeline with normalized status and telemetry output | Integration |
| AC-007 | FR-006 | Upgrade failure restores from pre-mutation `configSnapshotId` and emits linked audit event | Integration |
| AC-101 | NFR-001 | `AssignedStale` and stale-timeout policy behave as defined under reconnect and missed-heartbeat tests | Integration/Chaos |
| AC-102 | NFR-002 | Unsigned artifact is blocked; unauthorized role cannot trigger install; post-bootstrap reconnect requires valid bound mTLS cert identity; no plaintext secrets in config or logs | Integration/Security test |
| AC-103 | NFR-003 | Every job has a root span and step-level spans with required correlation fields | Integration/Observability test |
| AC-104 | NFR-004 | All runtime actions can be performed via REST/CLI without script surface dependency | Integration/Manual |
| AC-105 | NFR-005 | From a clean Windows machine without .NET runtime, admin can launch the orchestrator executable and access the embedded dashboard and API successfully | Integration/Manual |

---

## Traceability Matrix

| Req ID | ADR(s) | Design Doc Section(s) | Planned Test Type(s) | Notes |
|---|---|---|---|---|
| FR-001 | ADR-001, ADR-008 | `03-architecture-and-design.md`, `05-orchestration-and-validation.md` | Integration, E2E | |
| FR-002 | ADR-002, ADR-007 | `04-agent-bootstrap-and-communication.md`, `05-orchestration-and-validation.md` | Unit, Integration | |
| FR-003 | ADR-009 | `03-architecture-and-design.md`, `05-orchestration-and-validation.md` | Integration | |
| FR-004 | ADR-010 | `04-agent-bootstrap-and-communication.md` | Integration, Manual | |
| FR-005 | ADR-003 | `03-architecture-and-design.md`, `05-orchestration-and-validation.md`, `10-core-contracts-pack.md` | Integration | |
| FR-006 | ADR-013 | `03-architecture-and-design.md`, `11-config-persistence-contract.md` | Integration | |
| NFR-001 | ADR-008 | `05-orchestration-and-validation.md`, `07-security-reliability-observability.md` | Integration, Chaos | |
| NFR-002 | ADR-006, ADR-012 | `07-security-reliability-observability.md`, `09-security-pack.md` | Integration, Security test | |
| NFR-003 | ADR-004 | `03-architecture-and-design.md`, `07-security-reliability-observability.md` | Integration, Observability test | |
| NFR-004 | ADR-012 | `03-architecture-and-design.md`, `12-devops-pipeline-design-pack.md` | Integration, Manual | |
| NFR-005 | ADR-005 | `03-architecture-and-design.md`, `12-devops-pipeline-design-pack.md` | Integration, Manual | |

---

## Decision Coverage

This section maps all architectural decisions (D1-D25) to their requirement coverage in this PRD.

| Decision | Coverage Status | Section / Anchor |
|---|---|---|
| D1 | Covered | `Functional Requirements` (FR-002), `Acceptance Criteria` (AC-003) |
| D2 | Covered | `Functional Requirements` (FR-003), `Acceptance Criteria` (AC-004) |
| D3 | Covered | `Functional Requirements` (FR-003), `Acceptance Criteria` (AC-004) |
| D4 | Covered | `Scope and Assumptions` (In scope / Out of scope), `Non-Functional Requirements` (NFR-004) |
| D5 | Covered | `Scope and Assumptions` (In scope), `Non-Functional Requirements` (NFR-004) |
| D6 | Covered | `Scope and Assumptions` (In scope / Out of scope), `Traceability Matrix` (NFR-004 row) |
| D7 | Covered | `Non-Functional Requirements` (NFR-002), `Acceptance Criteria` (AC-102) |
| D8 | Covered (phased/deferred) | `Scope and Assumptions` (Out of scope), `Open Items` |
| D9 | Covered | `Scope and Assumptions` (Out of scope), `Non-Functional Requirements` (NFR-004) |
| D10 | Covered | `Functional Requirements` (FR-004), `Acceptance Criteria` (AC-005) |
| D11 | Covered | `Functional Requirements` (FR-002), `Non-Functional Requirements` (NFR-001), `Acceptance Criteria` (AC-003) |
| D12 | Indirectly covered | `Traceability Matrix` (design section links) |
| D13 | Covered | `Non-Functional Requirements` (NFR-002), `Acceptance Criteria` (AC-102) |
| D14 | N/A | Hygiene/process decision; no requirement content expected |
| D15 | Indirectly covered | `Non-Functional Requirements` (NFR-002), `Traceability Matrix` (`09-security-pack.md`) |
| D16 | N/A | Pack composition decision belongs to `10-core-contracts-pack.md` |
| D17 | Covered | `Functional Requirements` (FR-002), `Acceptance Criteria` (AC-003) |
| D18 | Covered | `Non-Functional Requirements` (NFR-001), `Acceptance Criteria` (AC-101) |
| D19 | Covered | `Non-Functional Requirements` (NFR-001), `Acceptance Criteria` (AC-101) |
| D20 | Covered | `Non-Functional Requirements` (NFR-001), `Acceptance Criteria` (AC-101) |
| D21 | Covered | `Non-Functional Requirements` (NFR-001), `Acceptance Criteria` (AC-101) |
| D22 | Covered | `Functional Requirements` (FR-006), `Acceptance Criteria` (AC-007), `Traceability Matrix` |
| D23 | Covered | Entire contract format (`FR-###`, `NFR-###`, `AC-###`) and `Traceability Matrix` |
| D24 | Indirectly covered | `Non-Functional Requirements` (NFR-004), `Traceability Matrix` (`12-devops-pipeline-design-pack.md`) |
| D25 | N/A | Reconciliation execution strategy; not a runtime/system requirement |

---

## Testing Decisions

- **What makes a good test**
  - Test external behavior and contracts, not implementation internals.
  - Prefer deterministic event-driven assertions over timing luck.
  - Validate failure paths and recovery paths, not just happy paths.
  - Every production bug becomes a regression test.

- **Modules to test**
  - Runtime Protocol Module (sequence validation, idempotency, replay rejection).
  - Lease and Ownership Module (heartbeat loss, stale timeout, reassignment bounds).
  - Agent Execution Pipeline Module (ordering, adapter normalization, terminal outcomes).
  - Artifact Trust Module (signature/hash pass/fail behavior).
  - Identity and Enrollment Module (token single-use, invalid-cert reconnect rejection).
  - Snapshot and Restore Module (migration failures and deterministic restore behavior).
  - Policy Evaluation Module (retry/risk/approval branch correctness).
  - Audit and Telemetry Module (required correlation fields and evidence completeness).

- **Test layers**
  - Unit tests for policy, sequencing, and decision logic.
  - Integration tests for API + persistence + SignalR + agent contracts.
  - E2E tests for operator-critical flows from submit to terminal state.
  - Fault-injection tests for checksum mismatch, network interruption, agent disconnect, and retry exhaustion.

- **Prior art for tests in this codebase**
  - Contract and sequence behavior from requirements/core contracts packs.
  - Test strategy baselines from `06-testing-strategy.md` (unit/integration/E2E/fault-injection and quality gates).
  - Acceptance evidence mapping from `13-poc-phase1-definition-of-done.md` and tracker AC references.

---

## Out of Scope

The following are explicitly out of scope for Phase 1:

- Linux agent implementation in Phase 1.
- Multi-orchestrator high availability/disaster recovery behavior.
- Hardening Phase 2 operations (advanced key lifecycle operations, expanded incident/forensics workflows, extended telemetry retention/indexing operations, rollout-ring automation).
- Any runtime dependency on external package sources.

---

## Open Items

The following items require resolution but are not blocking Phase 1 implementation:

- Final package-specific realism target identity (deferred until Day 3 modernization map completion).
- Numeric acceptance thresholds for queue depth and max concurrent jobs in PoC environment.

---

## PoC Boundary Note

- Items marked `[PoC Phase 1]` are implementation-plan baseline requirements.
- Additional governance depth (expanded rotation runbooks, advanced key lifecycle operations, broader fleet controls) is intentionally deferred to `[Hardening Phase 2]` artifacts.

---

## Further Notes

- This PRD is canonical for Phase 1 direction and governance; if conflicts exist across docs, this PRD wins.
- Policy decisions from `17-poc-phase1-prd-v2-capability-addendum.md` are closed for Phase 1 baseline and have been propagated into this PRD, canonical storyboard, and tracker.
- Acceptance and signoff remain governed by AC IDs in this document and evidence closure in `13-poc-phase1-definition-of-done.md`.
- Implementation progress and ownership remain tracked in `poc-phase1-prd-and-implementation-tracker.md`.
- Phase 2 must use a separate PRD and separate implementation tracker.
- Phase 1 defaults approved in this PRD: local filesystem artifact store, internal-only runtime package source, file-based OTel export with rotation/retention/redaction controls, and object-storage/OTel-stack migration as Phase 2 option.
