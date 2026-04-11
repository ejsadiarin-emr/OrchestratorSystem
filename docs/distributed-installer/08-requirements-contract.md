# Requirements Contract

Date: 2026-04-11  
Status: Draft (prefilled from locked decisions)

## Purpose

This artifact consolidates functional and non-functional requirements into testable, traceable IDs.

Implementation phasing tags used in this document:

- `[PoC Phase 1]` required before implementation-plan signoff
- `[Hardening Phase 2]` explicitly deferred to post-PoC hardening

ID conventions:

- `FR-###` Functional requirement
- `NFR-###` Non-functional requirement
- `AC-###` Acceptance criteria linked to FR/NFR

---

## 1) Scope and assumptions

### In scope (PoC)

- Distributed install orchestration across multiple Windows nodes on LAN.
- Agent bootstrap provisioning via WinRM (PoC), with enterprise channels (GPO/SCCM) as supported alternatives.
- Agent runtime communication over SignalR with claimed assignment + lease semantics.
- Full per-job pipeline execution on agent with orchestrator-owned job-level policy/dependency sequencing.
- Dry-run confidence gating, idempotent handlers, and rollback/compensation paths.
- Self-contained orchestrator executable with embedded React UI.

### Out of scope (PoC)

- Direct workstation package deployment from Azure DevOps pipelines.
- Linux agent implementation (design-ready only).
- SQL Server-grade package as first realism target (deferred to phase 2).
- Full fleet-scale HA/DR and advanced rollout policy rings.

### Assumptions

- PoC environment permits bootstrap remoting privileges on target machines.
- SQL Server is available for orchestrator state and queue persistence.
- Artifact signing and trust chain can be managed on-prem.
- Medium-confidence deployments may require operator confirmation.

---

## 2) Functional requirements

| ID | Requirement | Priority (Must/Should/Could) | Rationale | Linked AC IDs |
|---|---|---|---|---|
| FR-001 | [PoC Phase 1] System must support orchestrator-triggered installation, upgrade, rollback, cancel, and status query workflows across multiple target nodes | Must | Core distributed installer capability | AC-001, AC-002 |
| FR-002 | [PoC Phase 1] Runtime dispatch must follow canonical sequence: `Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose` | Must | Removes protocol ambiguity and ensures reliable ownership | AC-003 |
| FR-003 | [PoC Phase 1] Agent must execute full per-job pipeline locally; orchestrator must own job-level sequencing, dependencies, and policy only | Must | Resolves DAG ownership contradiction and reduces chatty coupling | AC-004 |
| FR-004 | [PoC Phase 1] Bootstrap provisioning must support transactional rollback/cleanup on failure | Must | Prevents partial bootstrap state and enables safe recovery | AC-005 |
| FR-005 | [PoC Phase 1] System must support heterogenous installer targets via typed pipeline + legacy adapter strategy (MSI/EXE/custom wrappers) | Should | Required for DeltaV modernization coexistence path | AC-006 |
| FR-006 | [PoC Phase 1] Upgrade flow must include config snapshot, deterministic migration, and restore-on-failure behavior | Must | Upgrade credibility and rollback safety | AC-007 |

---

## 3) Non-functional requirements

| ID | Requirement | Category | Target/Constraint | Linked AC IDs |
|---|---|---|---|---|
| NFR-001 | [PoC Phase 1] Delivery semantics must be at-least-once with idempotent handlers and bounded stale-job policy | Reliability | Lease TTL 90s; heartbeat 15s; stale after 3 missed heartbeats; `AssignedStale` auto-fails after 2 reassignment attempts or 15 minutes total stale duration | AC-101 |
| NFR-002 | [PoC Phase 1] Security baseline must enforce on-prem-first secrets, package integrity checks, RBAC, and bootstrap-token-to-mTLS steady-state agent auth | Security | No plaintext secrets; DPAPI/cert store/credential manager; signature+hash validation; one-time enrollment token then per-agent mTLS certificate identity | AC-102 |
| NFR-003 | [PoC Phase 1] Runtime orchestration and telemetry must remain deterministic and diagnosable at step level | Observability | Each step emits trace/metrics/log correlation fields | AC-103 |
| NFR-004 | [PoC Phase 1] Automation surface must be C# + REST/CLI + manifests; scripts are provisioning-only exception | Operability | No script-based runtime orchestration surface | AC-104 |
| NFR-005 | [PoC Phase 1] Orchestrator packaging must be self-contained single executable that includes API host and embedded React UI assets; system admin can run it on their machine without pre-installed .NET runtime or IIS | Deployment | `dotnet publish --self-contained` with single-file packaging for orchestrator distribution | AC-105 |

---

## 4) Acceptance criteria

| ID | Linked Req IDs | Criteria (testable statement) | Validation method |
|---|---|---|---|
| AC-001 | FR-001 | Operator can submit install and upgrade jobs to selected nodes through API/UI and observe terminal state | Integration/E2E |
| AC-002 | FR-001 | Rollback and cancel actions are reflected with auditable state transitions | Integration |
| AC-003 | FR-002 | Protocol messages include assignment/lease/sequence fields; status updates are idempotent upserts keyed by `(jobId, stepId, sequence)`; stale/out-of-order updates are rejected; same-key payload mismatch is rejected and audited | Unit/Integration |
| AC-004 | FR-003 | Agent executes full job pipeline locally while orchestrator tracks job-level dependency/policy only | Integration |
| AC-005 | FR-004 | Bootstrap failure triggers reverse-order cleanup (service/files/config/token/audit) | Integration/Manual |
| AC-006 | FR-005 | MSI/EXE adapter steps can run through typed pipeline with normalized status/telemetry output | Integration |
| AC-007 | FR-006 | Upgrade failure restores from pre-mutation `configSnapshotId` and emits linked audit event | Integration |
| AC-101 | NFR-001 | `AssignedStale` and stale-timeout policy behave as defined under reconnect and missed-heartbeat tests | Integration/Chaos |
| AC-102 | NFR-002 | Unsigned artifact is blocked; unauthorized role cannot trigger install; post-bootstrap reconnect requires valid bound mTLS cert identity; no plaintext secrets in config/logs | Integration/Security test |
| AC-103 | NFR-003 | Every job has root span and step-level spans with required correlation fields | Integration/Observability test |
| AC-104 | NFR-004 | All runtime actions can be performed via REST/CLI without script surface dependency | Integration/Manual |
| AC-105 | NFR-005 | From a clean Windows machine without .NET runtime, admin can launch the orchestrator executable and access the embedded dashboard + API successfully | Integration/Manual |

---

## 5) Traceability matrix

| Req ID | ADR(s) | Design doc section(s) | Planned test type(s) | Notes |
|---|---|---|---|---|
| FR-001 | ADR-001, ADR-008 | `03-architecture-and-design.md`, `05-orchestration-and-validation.md` | Integration, E2E |
| FR-002 | ADR-002, ADR-007 | `04-agent-bootstrap-and-communication.md`, `05-orchestration-and-validation.md` | Unit, Integration |
| FR-003 | ADR-009 | `03-architecture-and-design.md`, `05-orchestration-and-validation.md` | Integration |
| FR-004 | ADR-010 | `04-agent-bootstrap-and-communication.md` | Integration, Manual |
| FR-005 | ADR-003 | `03-architecture-and-design.md`, `05-orchestration-and-validation.md`, `10-core-contracts-pack.md` | Integration |
| FR-006 | ADR-013 | `03-architecture-and-design.md`, `11-config-persistence-contract.md` | Integration |
| NFR-001 | ADR-008 | `05-orchestration-and-validation.md`, `07-security-reliability-observability.md` | Integration, Chaos |
| NFR-002 | ADR-006, ADR-012 | `07-security-reliability-observability.md`, `09-security-pack.md` | Integration, Security test |
| NFR-003 | ADR-004 | `03-architecture-and-design.md`, `07-security-reliability-observability.md` | Integration, Observability test |
| NFR-004 | ADR-012 | `03-architecture-and-design.md`, `12-devops-pipeline-design-pack.md` | Integration, Manual |
| NFR-005 | ADR-005 | `03-architecture-and-design.md`, `12-devops-pipeline-design-pack.md` | Integration, Manual |

---

## 6) Decision coverage (D1-D25)

| Decision | Representation in this contract | Section / anchor |
|---|---|---|
| D1 | Covered | `2) Functional requirements` (FR-002), `4) Acceptance criteria` (AC-003) |
| D2 | Covered | `2) Functional requirements` (FR-003), `4) Acceptance criteria` (AC-004) |
| D3 | Covered | `2) Functional requirements` (FR-003), `4) Acceptance criteria` (AC-004) |
| D4 | Covered | `1) Scope and assumptions` (In scope / Out of scope), `3) Non-functional requirements` (NFR-004) |
| D5 | Covered | `1) Scope and assumptions` (In scope), `3) Non-functional requirements` (NFR-004) |
| D6 | Covered | `1) Scope and assumptions` (In scope / Out of scope), `5) Traceability matrix` (NFR-004 row) |
| D7 | Covered | `3) Non-functional requirements` (NFR-002), `4) Acceptance criteria` (AC-102) |
| D8 | Covered (phased/deferred) | `1) Scope and assumptions` (Out of scope), `7) Open items` |
| D9 | Covered | `1) Scope and assumptions` (Out of scope), `3) Non-functional requirements` (NFR-004) |
| D10 | Covered | `2) Functional requirements` (FR-004), `4) Acceptance criteria` (AC-005) |
| D11 | Covered | `2) Functional requirements` (FR-002), `3) Non-functional requirements` (NFR-001), `4) Acceptance criteria` (AC-003) |
| D12 | Indirectly covered | `5) Traceability matrix` (design section links) |
| D13 | Covered | `3) Non-functional requirements` (NFR-002), `4) Acceptance criteria` (AC-102) |
| D14 | N/A | Hygiene/process decision; no requirement content expected in this contract |
| D15 | Indirectly covered | `3) Non-functional requirements` (NFR-002), `5) Traceability matrix` (`09-security-pack.md`) |
| D16 | N/A | Pack composition decision belongs to `10-core-contracts-pack.md` |
| D17 | Covered | `2) Functional requirements` (FR-002), `4) Acceptance criteria` (AC-003) |
| D18 | Covered | `3) Non-functional requirements` (NFR-001), `4) Acceptance criteria` (AC-101) |
| D19 | Covered | `3) Non-functional requirements` (NFR-001), `4) Acceptance criteria` (AC-101) |
| D20 | Covered | `3) Non-functional requirements` (NFR-001), `4) Acceptance criteria` (AC-101) |
| D21 | Covered | `3) Non-functional requirements` (NFR-001), `4) Acceptance criteria` (AC-101) |
| D22 | Covered | `2) Functional requirements` (FR-006), `4) Acceptance criteria` (AC-007), `5) Traceability matrix` |
| D23 | Covered | Entire contract format (`FR-###`, `NFR-###`, `AC-###`) and `5) Traceability matrix` |
| D24 | Indirectly covered | `3) Non-functional requirements` (NFR-004), `5) Traceability matrix` (`12-devops-pipeline-design-pack.md`) |
| D25 | N/A | Reconciliation execution strategy; not a runtime/system requirement |

---

## 7) Open items

- Final package-specific realism target identity (deferred until Day 3 modernization map completion).
- Numeric acceptance thresholds for queue depth and max concurrent jobs in PoC environment.

## 8) PoC boundary note

- Items marked `[PoC Phase 1]` are implementation-plan baseline requirements.
- Additional governance depth (expanded rotation runbooks, advanced key lifecycle operations, broader fleet controls) is intentionally deferred to `[Hardening Phase 2]` artifacts.
