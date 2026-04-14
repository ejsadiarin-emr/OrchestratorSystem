# PoC Phase 1 Final PRD

Date: 2026-04-14  
Status: Canonical source of truth for PoC Phase 1

## Problem Statement

Teams need a Windows-first distributed installer flow that can safely install, update, modify, rollback, and observe software deployment across remote nodes from a central orchestrator. Today, the risk is inconsistent behavior across nodes, weak traceability during failures, and unclear runtime security boundaries (artifact trust, child-process execution trust, and agent identity lifecycle). The PoC must prove that installation operations are reliable, auditable, and operationally understandable without requiring enterprise-scale hardening scope in Phase 1.

## Solution

Build a single-orchestrator, Windows-first PoC where admins submit jobs via API/UI/CLI and agents execute a local typed pipeline with deterministic runtime contracts. Use SignalR for control/status only and HTTP artifact endpoints for package transfer. Enforce package trust (signature/hash), bootstrap token to mTLS steady-state identity, policy-tagged retry/idempotency/risk handling, and step-level telemetry/audit evidence. Keep scope intentionally constrained to Phase 1 goals and defer hardening/scale extensions to Phase 2.

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

## Implementation Decisions

- **Architecture scope**
  - Phase 1 is Windows-first, single-orchestrator, and implementation-focused on proving end-to-end install/update/modify safety and observability.
  - Linux agent support and multi-orchestrator scale behavior are deferred.

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
  - Upstream/vendor binaries are ingested into the orchestrator artifact store as immutable package artifacts (for example `.exe`, `.msi`, `.zip`, `.tar.gz`), with metadata and policy attached.
  - Artifact storage backend for Phase 1 is local filesystem on the orchestrator host; object storage is a Phase 2 migration option.

- **Package trust, provenance, and attestation policy**
  - Vendor binaries are not re-signed as vendor binaries.
  - At ingest time, orchestrator verifies available vendor trust evidence (signature/checksum), computes canonical digest, and records provenance metadata.
  - Immutable digest means a stored content hash that cannot be changed for a given package version record.
  - Provenance means origin metadata (source URL/repository, vendor/publisher identity if available, ingest time, operator/process identity, and verification result).
  - Optional org attestation signs manifest/bundle metadata (not the vendor binary) using organization-controlled key material, so agents verify organizational approval in addition to binary integrity.

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

## Out of Scope

- Linux agent implementation in Phase 1.
- Multi-orchestrator high availability/disaster recovery behavior.
- Hardening Phase 2 operations (advanced key lifecycle operations, expanded incident/forensics workflows, extended telemetry retention/indexing operations, rollout-ring automation).
- Any runtime dependency on external package sources.

## Further Notes

- This PRD is canonical for Phase 1 direction and governance; if conflicts exist across docs, this PRD wins.
- Policy decisions from `17-poc-phase1-prd-v2-capability-addendum.md` are closed for Phase 1 baseline and have been propagated into this PRD, canonical storyboard, and tracker.
- Acceptance and signoff remain governed by AC IDs in `08-requirements-contract.md` and evidence closure in `13-poc-phase1-definition-of-done.md`.
- Implementation progress and ownership remain tracked in `poc-phase1-prd-and-implementation-tracker.md`.
- Phase 2 must use a separate PRD and separate implementation tracker.
- Phase 1 defaults approved in this PRD: local filesystem artifact store, internal-only runtime package source, file-based OTel export with rotation/retention/redaction controls, and object-storage/OTel-stack migration as Phase 2 option.
