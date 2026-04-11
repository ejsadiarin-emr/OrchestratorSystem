# Distributed Installer PoC Phase 1 Spec Reconciliation Plan Tasklist

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a docs-only, implementation-ready PoC Phase 1 specification pack with full traceability, zero contract contradictions, and explicit Hardening Phase 2 deferrals.

**Architecture:** Keep `08/09/10/11/12` as the contract pack and align all dependent baseline docs (`03/04/05/07`) and diagrams (`architecture`, `install-sequence`, `job-state-machine`) to the decision lock addendum. Use `08-requirements-contract.md` as the canonical requirement ID source and keep runtime protocol/state semantics consistent across prose and diagrams.

**Tech Stack:** Markdown docs, Mermaid diagrams (`*.mmd`), ASCII diagrams (`*.ascii.md`), docs verification via grep/manual review.

---

## 1) Source-of-Truth Inputs

- `docs/distributed-installer/08-requirements-contract.md`
- `docs/distributed-installer/09-security-pack.md`
- `docs/distributed-installer/10-core-contracts-pack.md`
- `docs/distributed-installer/11-config-persistence-contract.md`
- `docs/distributed-installer/12-devops-pipeline-design-pack.md`
- `docs/distributed-installer/03-architecture-and-design.md`
- `docs/distributed-installer/04-agent-bootstrap-and-communication.md`
- `docs/distributed-installer/05-orchestration-and-validation.md`
- `docs/distributed-installer/07-security-reliability-observability.md`
- `docs/distributed-installer/diagrams/architecture.ascii.md`
- `docs/distributed-installer/diagrams/architecture.mmd`
- `docs/distributed-installer/diagrams/install-sequence.ascii.md`
- `docs/distributed-installer/diagrams/install-sequence.mmd`
- `docs/distributed-installer/diagrams/job-state-machine.ascii.md`
- `docs/distributed-installer/diagrams/job-state-machine.mmd`
- `docs/distributed-installer/sessions/20260411-decision-lock-addendum.md`

---

## 2) PoC Implementation Scope

### IN scope (PoC Phase 1 only)

1. Requirements and acceptance contract closure for `FR-001..FR-006`, `NFR-001..NFR-005`, `AC-001..AC-105`.
2. Canonical runtime sequence and ownership semantics consistency:
   - `Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`
3. Lease/idempotency/reconnect contract consistency:
   - TTL `90s`, heartbeat `15s`, stale threshold `3`, `AssignedStale` handling, stale timeout bounds, replay-safe reassignment.
4. Config snapshot/migration/restore and audit linkage coherence.
5. Security pack closure (DFD, STRIDE, mitigation mapping, secure coding checklist).
6. DevOps pipeline contract closure with non-negotiable packaging and policy boundaries:
   - self-contained single orchestrator executable with embedded React UI
   - no preinstalled .NET runtime/IIS required
   - no direct workstation deployment from Azure DevOps pipeline
7. Diagram parity (ASCII and Mermaid) with canonical contracts.

### OUT of scope (defer to Hardening Phase 2)

- Expanded key/certificate lifecycle operational runbooks and cadence.
- Broader incident/forensics workflows and long-horizon retention operations.
- Environment matrix expansion, rollout rings, and advanced release governance automation.
- SQL Server-grade installer execution work (phase 2 realism target).
- Linux agent implementation details.
- Product/runtime code changes.

---

## 3) Non-Negotiable Constraints

- Docs-first only; no product code changes.
- Preserve current architecture direction and decision lock.
- Resolve ambiguity from docs/ADR/decision lock first.
- Keep all tasks PoC-lean; do not introduce production hardening work as active implementation.
- Keep both diagram formats (`*.ascii.md` and `*.mmd`) synchronized when touched.

---

## 4) Dependency-Ordered Spec Tasklist

## Phase 1 - Scope and Traceability Lock

### Task 1.1: Create PoC traceability baseline

**Objective:** Build a single trace map from PoC requirements/security/devops controls to concrete doc sections.

**Dependencies:** None.

**Files:**
- Modify: `docs/distributed-installer/08-requirements-contract.md`
- Modify: `docs/distributed-installer/sessions/20260411-poc-phase1-spec-plan-tasklist.md` (trace table updates)

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: manual trace check pass sheet.
- Docs: explicit task-to-requirement trace table in this session plan.

**Verification criteria:**
- Every task in this plan maps to at least one `[PoC Phase 1]` requirement/control.
- `08` remains canonical source for FR/NFR/AC wording.

**Suggested commit boundary:** `docs(traceability): establish phase-1 requirement-to-task mapping`

- [x] Capture current requirement/control IDs used by this plan.
- [x] Add/confirm traceability references without redefining requirement text outside `08`.
- [x] Record verification status in this session plan.

### Task 1.2: Enforce explicit PoC vs Hardening boundaries

**Objective:** Remove mixed-phase ambiguity from contract docs.

**Dependencies:** Task 1.1.

**Files:**
- Modify: `docs/distributed-installer/08-requirements-contract.md`
- Modify: `docs/distributed-installer/09-security-pack.md`
- Modify: `docs/distributed-installer/12-devops-pipeline-design-pack.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: manual scan for boundary language placement.
- Docs: clear IN/OUT wording and explicit deferred sections.

**Verification criteria:**
- Active implementation tasks mention only `[PoC Phase 1]` content.
- `[Hardening Phase 2]` appears only in deferred/non-goal sections.

**Suggested commit boundary:** `docs(scope): lock phase boundaries across contracts`

- [x] Normalize boundary phrasing in `08/09/12`.
- [x] Ensure no hardening item is written as a required PoC task.
- [x] Update this tasklist status with pass/fail notes.

## Phase 2 - Runtime Protocol and State Contract Convergence

### Task 2.1: Align canonical runtime sequence across prose docs

**Objective:** Ensure one exact runtime protocol string in all normative references.

**Dependencies:** Phase 1 complete.

**Files:**
- Modify: `docs/distributed-installer/03-architecture-and-design.md`
- Modify: `docs/distributed-installer/04-agent-bootstrap-and-communication.md`
- Modify: `docs/distributed-installer/05-orchestration-and-validation.md`
- Modify: `docs/distributed-installer/10-core-contracts-pack.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: grep validation for canonical string.
- Docs: normalized sequence wording and message naming.

**Verification criteria:**
- Exact canonical sequence appears where runtime flow is normative.
- No contradictory short-form sequence is used as canonical in active docs.

**Suggested commit boundary:** `docs(protocol): normalize canonical runtime sequence`

- [x] Replace divergent runtime sequence text with canonical full sequence.
- [x] Ensure ownership semantics remain consistent (job-level orchestration; agent executes full pipeline).
- [x] Re-run grep and record results.

Task 2.1 execution note (2026-04-11):

- Updated `install-sequence.ascii.md` labels from generic wording (`SignalR job`, `SignalR update`) to canonical terms (`AssignJob`, `StepStatus`).
- Updated `install-sequence.mmd` to include explicit `AssignJob(assignmentId, leaseId, sequence)`, `AckClaim(...)`, `StepStatus(...)`, `LeaseHeartbeat(...)`, `Complete/Fail(...)`, and `LeaseClose(...)` flow labels.
- Updated reconnect wording in `04-agent-bootstrap-and-communication.md` to canonical `lastAcknowledgedSequence + 1` expression.
- Verification: grep checks confirm canonical message names present in diagrams and reconnect-resume expression present in runtime comms doc.

### Task 2.2: Align lease defaults and stale timeout semantics

**Objective:** Standardize lease constants and stale failure behavior.

**Dependencies:** Task 2.1.

**Files:**
- Modify: `docs/distributed-installer/03-architecture-and-design.md`
- Modify: `docs/distributed-installer/05-orchestration-and-validation.md`
- Modify: `docs/distributed-installer/08-requirements-contract.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: manual value consistency checklist.
- Docs: aligned lease defaults + stale policy text.

**Verification criteria:**
- TTL `90s`, heartbeat `15s`, stale threshold `3` are identical across docs.
- `AssignedStale` auto-fail bound (`2` reassignment attempts or `15 minutes`) is consistent.

**Suggested commit boundary:** `docs(lease): align ttl heartbeat stale timeout defaults`

- [x] Confirm constants in `03/05/08`.
- [x] Confirm stale timeout and reason code wording consistency.
- [x] Record mismatches and resolutions in this plan.

Task 2.2 execution note (2026-04-11):

- Added explicit lease/stale defaults in `03-architecture-and-design.md` communication section for direct parity with `05/08/10`.
- Confirmed stale timeout reason code and bounds remain consistent (`lease_timeout_exhausted`, 2 attempts or 15 minutes) across normative docs.
- Verification: grep checks across docs confirm aligned constants and stale semantics.

### Task 2.3: Align idempotency and reconnect-resume semantics

**Objective:** Ensure replay/out-of-order handling and reconnect behavior are contractually identical.

**Dependencies:** Task 2.2.

**Files:**
- Modify: `docs/distributed-installer/04-agent-bootstrap-and-communication.md`
- Modify: `docs/distributed-installer/10-core-contracts-pack.md`
- Modify: `docs/distributed-installer/05-orchestration-and-validation.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: manual matrix for `(jobId, stepId, sequence)` behavior.
- Docs: synchronized idempotency/reconnect wording.

**Verification criteria:**
- Same-key payload conflict behavior and audit expectation are consistent.
- Reconnect resume references `lastAcknowledgedSequence + 1` semantics without contradiction.

**Suggested commit boundary:** `docs(idempotency): align replay guards and reconnect resume contract`

- [x] Normalize idempotent upsert and conflict rejection wording.
- [x] Normalize stale/out-of-order rejection wording.
- [x] Confirm reconnect resume phrase consistency.

Task 2.3 execution note (2026-04-11):

- Added explicit status update handling contract to `05-orchestration-and-validation.md` including idempotent key, conflict audit event, stale/out-of-order rejection, and reconnect resume semantics.
- Confirmed reconnect wording remains canonical (`lastAcknowledgedSequence + 1`) in `04` and `10`.
- Verification: grep checks confirm idempotency/reconnect contract statements are present and consistent.

### Task 2.4: Update install sequence diagrams to canonical message flow

**Objective:** Bring diagram flows to parity with canonical protocol terms and ownership model.

**Dependencies:** Task 2.3.

**Files:**
- Modify: `docs/distributed-installer/diagrams/install-sequence.ascii.md`
- Modify: `docs/distributed-installer/diagrams/install-sequence.mmd`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: manual diagram-text parity check.
- Docs: synchronized diagram labels and notes.

**Verification criteria:**
- Diagrams explicitly show `AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose` semantics.
- No conflicting message naming remains (`JobStatusUpdate` vs `StepStatus`) where canonical naming is required.

**Suggested commit boundary:** `docs(diagrams): align install sequence with canonical protocol`

- [x] Update message labels and terminal flow notes in ASCII diagram.
- [x] Update Mermaid sequence to match canonical naming.
- [x] Validate parity between ASCII and Mermaid.

### Task 2.5: Update state machine diagrams to canonical state model

**Objective:** Ensure state diagrams match canonical states in architecture doc, including stale-lease behavior.

**Dependencies:** Task 2.3.

**Files:**
- Modify: `docs/distributed-installer/03-architecture-and-design.md`
- Modify: `docs/distributed-installer/diagrams/job-state-machine.ascii.md`
- Modify: `docs/distributed-installer/diagrams/job-state-machine.mmd`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: state-by-state parity checklist.
- Docs: consistent state names and transitions.

**Verification criteria:**
- `AssignedStale` and stale-timeout path are represented in diagrams.
- Intermediate states (`Prechecking`, `PrecheckPassed`, `Downloading`, `Verifying`, `VerifyFailed`, `RollbackInProgress`, `RolledBack`) remain consistent.

**Suggested commit boundary:** `docs(state-machine): align canonical states and transitions`

- [x] Reconcile canonical state list and transitions.
- [x] Ensure cancellation paths align with safe-checkpoint notes.
- [x] Confirm ASCII and Mermaid parity.

Task 2.5 execution note (2026-04-11):

- Updated `job-state-machine.mmd` to include `AssignedStale` transitions, stale timeout terminal path (`lease_timeout_exhausted`), expanded precheck/download/verify path, and cancellation paths for intermediate states.
- Verification: grep checks confirm `AssignedStale` and `lease_timeout_exhausted` are present in Mermaid state machine.

### Task 2.6: Align architecture diagrams with trust and runtime boundaries

**Objective:** Ensure architecture visuals and labels align with runtime/control boundaries and security trust model.

**Dependencies:** Tasks 2.4 and 2.5.

**Files:**
- Modify: `docs/distributed-installer/diagrams/architecture.ascii.md`
- Modify: `docs/distributed-installer/diagrams/architecture.mmd`
- Modify: `docs/distributed-installer/09-security-pack.md` (if boundary labels need alignment)

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: trust-boundary and runtime-label checklist.
- Docs: synchronized architecture diagram terminology.

**Verification criteria:**
- Runtime assignment/lease semantics labeling is consistent with contract pack.
- Security pack trust-boundary references map cleanly to architecture components.

**Suggested commit boundary:** `docs(architecture): synchronize boundary and runtime labels`

- [x] Confirm trust boundary wording across diagram and security pack.
- [x] Confirm runtime channel labels are unambiguous.
- [x] Record any deferred visual simplifications.

Task 2.6 execution note (2026-04-11):

- Added explicit trust-boundary mapping text in `09-security-pack.md` for TB-01..TB-04.
- Added TB annotations to `architecture.ascii.md` and edge labels in `architecture.mmd` for TB-01..TB-04 mapping.
- Verification: manual diagram/security-pack review confirms trust-boundary mapping closure.

## Phase 3 - Config Persistence Contract Closure

### Task 3.1: Cross-link config snapshot/migration/restore contracts

**Objective:** Guarantee FR-006/AC-007/D22 behavior is discoverable and contradiction-free across docs.

**Dependencies:** Phase 2 complete.

**Files:**
- Modify: `docs/distributed-installer/11-config-persistence-contract.md`
- Modify: `docs/distributed-installer/03-architecture-and-design.md`
- Modify: `docs/distributed-installer/08-requirements-contract.md`
- Modify: `docs/distributed-installer/10-core-contracts-pack.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: cross-reference checklist.
- Docs: explicit links and consistent terms.

**Verification criteria:**
- Pre-mutation snapshot, deterministic migration path, restore-on-failure, and audit linkage are all explicitly tied.
- No conflicting event naming or sequence of operations.

**Suggested commit boundary:** `docs(config): close snapshot migration restore cross-links`

- [x] Add/normalize references from `03/08/10` to `11`.
- [x] Confirm migration-safe checkpoint wording is stable.
- [x] Record evidence in this tasklist.

Task 3.1 execution note (2026-04-11):

- Added `03` section cross-linking upgrade config persistence contract back to `11`.
- Added `11` cross-reference anchors for `FR-006`, `AC-007`, `D22`, and `10` entity linkage.
- Verification: manual cross-reference check confirms discoverable linkage from architecture/contracts to config persistence contract.

### Task 3.2: Normalize config audit event naming

**Objective:** Keep config migration/restore events consistent where referenced.

**Dependencies:** Task 3.1.

**Files:**
- Modify: `docs/distributed-installer/11-config-persistence-contract.md`
- Modify: `docs/distributed-installer/07-security-reliability-observability.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: manual event-name comparison pass.
- Docs: aligned event names and required fields.

**Verification criteria:**
- Config event names are not aliased inconsistently in dependent docs.
- Audit linkage expectations remain compatible with security baseline.

**Suggested commit boundary:** `docs(audit): normalize config migration restore event names`

- [x] Compare event labels and required fields across files.
- [x] Resolve naming drift and update references.
- [x] Log final canonical names in this plan.

Task 3.2 execution note (2026-04-11):

- Verified canonical config audit events remain:
  - `ConfigSnapshotCreated`
  - `ConfigMigrationApplied`
  - `ConfigRestoreApplied`
  - `ConfigMigrationFailed`
  - `ConfigRestoreFailed`
- Verification: grep checks confirm event names and `configSnapshotId` linkage remain stable without alias drift.

## Phase 4 - Security Pack PoC Closure

### Task 4.1: Align DFD trust boundaries with architecture

**Objective:** Ensure TB-01..TB-04 map directly to architecture components and flows.

**Dependencies:** Task 2.6.

**Files:**
- Modify: `docs/distributed-installer/09-security-pack.md`
- Modify: `docs/distributed-installer/07-security-reliability-observability.md`
- Modify: `docs/distributed-installer/diagrams/architecture.ascii.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: boundary mapping checklist.
- Docs: direct mapping notes and consistent labels.

**Verification criteria:**
- No orphan trust boundaries.
- Security and architecture docs use matching component names.

**Suggested commit boundary:** `docs(security): align dfd trust boundaries with architecture map`

- [x] Validate boundary rows vs components and data flows.
- [x] Resolve naming mismatches.
- [x] Mark task complete with evidence note.

Task 4.1 execution note (2026-04-11):

- Completed TB mapping closure by aligning security pack boundaries with architecture diagram annotations.
- Verification: manual row-to-component mapping review confirms TB-01..TB-04 map directly to architecture channels/components.

### Task 4.2: Complete STRIDE-to-mitigation-to-evidence linkage

**Objective:** Ensure every PoC threat has mitigation, owner, cadence, and evidence path.

**Dependencies:** Task 4.1.

**Files:**
- Modify: `docs/distributed-installer/09-security-pack.md`
- Modify: `docs/distributed-installer/08-requirements-contract.md` (trace links only)

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: threat-control evidence matrix check.
- Docs: full linkage closure for TH-001..TH-007B and M-001..M-006.

**Verification criteria:**
- All `[PoC Phase 1]` threats map to mitigation rows with evidence expectations.
- Security controls remain testable against AC-102.

**Suggested commit boundary:** `docs(security): finalize stride mitigation evidence linkage`

- [x] Check each threat row has enforceable mitigation/evidence.
- [x] Add missing owner/cadence/evidence details where needed.
- [x] Update trace references if gaps are found.

Task 4.2 execution note (2026-04-11):

- Verified TH-001..TH-007B maintain mitigation coverage and M-001..M-006 include owner/cadence/evidence columns in `09-security-pack.md`.
- Verification: table completeness check confirms PoC threat-control-evidence linkage remains intact.

### Task 4.3: Isolate hardening-only security operations

**Objective:** Prevent scope creep by confining non-PoC operations to deferred sections.

**Dependencies:** Task 4.2.

**Files:**
- Modify: `docs/distributed-installer/09-security-pack.md`
- Modify: `docs/distributed-installer/07-security-reliability-observability.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: deferred-only language scan.
- Docs: explicit hardening defer blocks.

**Verification criteria:**
- Expanded rotation/runbook/retention operations are marked `[Hardening Phase 2]` only.
- PoC baseline security controls remain complete without hardening dependencies.

**Suggested commit boundary:** `docs(security): isolate hardening operations from poc baseline`

- [x] Move/label hardening operations under deferred sections.
- [x] Confirm baseline controls are still sufficient for PoC signoff.
- [x] Record explicit deferred list.

Task 4.3 execution note (2026-04-11):

- Added explicit `[Hardening Phase 2]` defer statement in `07-security-reliability-observability.md` production backlog section.
- Expanded deferred open items in `09-security-pack.md` to include incident/forensics and retention governance operations.
- Verification: manual boundary scan confirms hardening ops are isolated from PoC baseline controls.

## Phase 5 - DevOps Pack PoC Closure

### Task 5.1: Lock self-contained orchestrator packaging gate

**Objective:** Make non-negotiable packaging requirement explicit and testable.

**Dependencies:** Phase 2 complete.

**Files:**
- Modify: `docs/distributed-installer/12-devops-pipeline-design-pack.md`
- Modify: `docs/distributed-installer/08-requirements-contract.md` (cross-reference only)

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: gate wording checklist.
- Docs: explicit required packaging gate language.

**Verification criteria:**
- Clean-host launch (no preinstalled .NET runtime/IIS) is mandatory gate wording.
- Embedded React UI in orchestrator executable is explicitly stated.

**Suggested commit boundary:** `docs(devops): enforce self-contained clean-host packaging gate`

- [x] Confirm packaging command and gate language are mandatory.
- [x] Confirm orchestrator executable scope is unambiguous.
- [x] Record verification evidence line.

Task 5.1 execution note (2026-04-11):

- Verified `12-devops-pipeline-design-pack.md` contains mandatory self-contained publish posture and clean-host gate language.
- Verification: manual review confirms no optional phrasing for AC-105-critical packaging gate.

### Task 5.2: Lock orchestrator-only runtime deployment policy boundary

**Objective:** Ensure no direct workstation deployment from pipeline appears in active workflow.

**Dependencies:** Task 5.1.

**Files:**
- Modify: `docs/distributed-installer/12-devops-pipeline-design-pack.md`
- Modify: `docs/distributed-installer/08-requirements-contract.md`
- Modify: `docs/distributed-installer/adr/ADR-012-enterprise-bootstrap-vs-runtime-orchestration.md` (if wording drift found)

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: policy boundary scan.
- Docs: consistent D9 boundary statements.

**Verification criteria:**
- Runtime package operations are always documented as Orchestrator API/CLI actions.
- Pipeline scope is limited to build/test/sign/publish and orchestrator deployment.

**Suggested commit boundary:** `docs(policy): enforce orchestrator-only runtime deployment boundary`

- [x] Normalize policy statements across docs.
- [x] Remove or qualify any ambiguous deployment wording.
- [x] Add note to this plan confirming D9 lock compliance.

Task 5.2 execution note (2026-04-11):

- Strengthened policy section in `12` to explicitly state runtime install/upgrade/rollback operations are never executed directly from pipeline jobs.
- Added explicit pipeline responsibility boundary language (build/test/sign/publish + orchestrator deploy only).
- Verification: policy boundary scan confirms D9 alignment.

### Task 5.3: Finalize on-prem/air-gap gate realism and rollback wording

**Objective:** Keep PoC pipeline gates practical in constrained environments.

**Dependencies:** Task 5.2.

**Files:**
- Modify: `docs/distributed-installer/12-devops-pipeline-design-pack.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: gate realism checklist.
- Docs: explicit on-prem fixture assumptions and rollback trigger wording.

**Verification criteria:**
- Required release gates do not assume public internet.
- Rollback trigger text for failed health checks is explicit and unambiguous.

**Suggested commit boundary:** `docs(devops): finalize air-gap gate realism and rollback criteria`

- [x] Verify gate constraints reference internal mirrors/preloaded fixtures.
- [x] Verify health-check rollback trigger wording is deterministic.
- [x] Log closure evidence.

Task 5.3 execution note (2026-04-11):

- Verified on-prem/air-gap constraints and deterministic rollback trigger wording in `12` remain explicit and testable.
- Verification: manual gate review confirms no public-internet assumptions in required release gates.

## Phase 6 - Final PoC Readiness Documentation

### Task 6.1: Publish PoC Definition of Done checklist artifact

**Objective:** Produce signoff-ready checklist mapped to requirements and controls.

**Dependencies:** Phases 1-5 complete.

**Files:**
- Create: `docs/distributed-installer/13-poc-phase1-definition-of-done.md`
- Modify: `docs/distributed-installer/README.md`

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: checklist completeness review.
- Docs: DoD checklist and README entry.

**Verification criteria:**
- Checklist covers AC-001..AC-105 and relevant security/devops controls.
- README links to DoD artifact.

**Suggested commit boundary:** `docs(signoff): add poc phase-1 definition-of-done artifact`

- [x] Draft DoD checklist with evidence slots.
- [x] Link DoD in README reading order.
- [x] Validate all required IDs are represented.

Task 6.1 execution note (2026-04-11):

- Created `docs/distributed-installer/13-poc-phase1-definition-of-done.md` with AC coverage table, contract/security/devops closure checklists, deferred hardening confirmation, and signoff table.
- Updated `README.md` recommended reading order and session notes to include new DoD and this tasklist.
- Verification: manual check confirms AC-001..AC-105 presence in DoD checklist table.

### Task 6.2: Run final consistency sweep and document closure evidence

**Objective:** Record final closure and prevent future drift.

**Dependencies:** Task 6.1.

**Files:**
- Modify: `docs/distributed-installer/sessions/20260411-poc-phase1-spec-plan-tasklist.md`
- Modify: `docs/distributed-installer/sessions/20260411-decision-lock-addendum.md` (append link to closure artifacts only if needed)

**Deliverables (code/tests/docs):**
- Code: none.
- Tests: final grep/manual consistency sweep.
- Docs: completed tasklist status + evidence summary.

**Verification criteria:**
- No unresolved contradictions against decision lock D1-D25.
- Final plan status table is complete and auditable.

**Suggested commit boundary:** `docs(hygiene): complete phase-1 consistency sweep and closure log`

- [x] Perform final grep/manual contradiction scan.
- [x] Update each task status in this file.
- [x] Add closure note with artifact links.

Task 6.2 execution note (2026-04-11):

- Completed final consistency sweep with grep/manual checks over protocol, stale/lease, config events, trust boundaries, and devops policy constraints.
- Updated this tasklist with execution evidence notes for completed tasks and execution log entries.
- Closure artifact links: `08`, `09`, `10`, `11`, `12`, `13`, diagrams (`architecture`, `install-sequence`, `job-state-machine`), and `04/05/07` aligned sections.

---

## 5) Task-to-Requirement Traceability

| Task ID | Primary Requirement/Control Links | Decision Lock Links |
|---|---|---|
| 1.1 | FR-001..FR-006, NFR-001..NFR-005, AC-001..AC-105 | D23 |
| 1.2 | Scope boundary notes in `08/09/12` | D8, D24 |
| 2.1 | FR-002, AC-003 | D1, D11, D17 |
| 2.2 | NFR-001, AC-101 | D18, D19, D20, D21 |
| 2.3 | FR-002, NFR-001, AC-003, AC-101 | D11, D17 |
| 2.4 | FR-002, AC-003 | D1, D17 |
| 2.5 | NFR-001, AC-101 | D12, D19, D21 |
| 2.6 | NFR-002, AC-102 | D7, D15 |
| 3.1 | FR-006, AC-007 | D22 |
| 3.2 | NFR-002, AC-102 | D13, D22 |
| 4.1 | NFR-002, AC-102 | D15 |
| 4.2 | NFR-002, AC-102 | D7, D13, D15 |
| 4.3 | PoC boundary notes in `09/07` | D15 |
| 5.1 | NFR-005, AC-105 | D24 |
| 5.2 | NFR-004, AC-104 | D4, D5, D6, D9, D24 |
| 5.3 | NFR-005, AC-105 | D24 |
| 6.1 | AC-001..AC-105 closure evidence | D23, D24 |
| 6.2 | Full pack consistency closure | D25 |

---

## 6) Risk and De-scope Options

| Risk | Level | Trigger | De-scope Option |
|---|---|---|---|
| Cross-doc contradictions during parallel edits | High | Same contract updated in multiple files with diverging wording | Keep normative detail in `08` and `10`, convert secondary docs to references-only phrasing |
| Scope creep into Hardening Phase 2 | High | Operational/security enhancements entering PoC required sections | Move to explicit deferred sections; keep only PoC-tagged controls in active tasks |
| Diagram drift from prose contracts | Medium | ASCII and Mermaid disagree on protocol/state semantics | Make diagram pair updates atomic in same commit; if needed, keep one concise and reference canonical prose |
| Over-specification without testable criteria | Medium | Broad statements with no validation path | Add verification bullets tied to AC IDs and observable grep/manual checks |

---

## 7) Definition of Done (PoC) Checklist

- [x] All completed tasks map to `[PoC Phase 1]` requirements/controls only.
- [x] Canonical runtime sequence is identical in `03/04/10` and install sequence diagrams.
- [x] Lease defaults and `AssignedStale` semantics match `08/05/03/10` and state diagrams.
- [x] Idempotency and reconnect-resume behavior is consistent across runtime docs.
- [x] Config snapshot/migration/restore contract is coherent and traceable to FR-006/AC-007.
- [x] Security pack has complete DFD, STRIDE, mitigation, and secure coding traceability.
- [x] DevOps pack enforces self-contained orchestrator packaging and no-preinstalled-runtime/IIS gate.
- [x] Policy boundary is explicit: no direct workstation deployment from Azure DevOps pipeline.
- [x] Hardening Phase 2 items are explicitly deferred and not required for PoC signoff.
- [x] Final closure notes and evidence links are captured in this session artifact.

---

## 8) Execution Log (fill during implementation)

| Date | Task ID | Change summary | Verification run | Commit |
|---|---|---|---|---|
| 2026-04-11 | - | Plan created | Initial document validation | - |
| 2026-04-11 | 2.1, 2.5 | Canonical protocol/state diagram alignment pass (ASCII + Mermaid + reconnect wording in 04) | grep checks for protocol/state keywords and reconnect expression | - |
| 2026-04-11 | 2.2, 2.3, 2.6 | Lease/idempotency convergence and trust-boundary architecture mapping | grep/manual checks for constants, idempotency/reconnect wording, TB mapping | - |
| 2026-04-11 | 3.1, 3.2 | Config persistence cross-link closure and config event naming consistency verification | manual cross-reference review + grep event checks | - |
| 2026-04-11 | 4.1, 4.2, 4.3 | Security boundary/mapping closure and hardening defer isolation | manual boundary mapping + deferred-scope scan | - |
| 2026-04-11 | 5.1, 5.2, 5.3 | DevOps packaging/policy boundary hardening and gate realism verification | manual policy/gate review | - |
| 2026-04-11 | 6.1, 6.2 | Added PoC DoD artifact and completed final consistency closure documentation | grep/manual final sweep + README/link checks | - |
