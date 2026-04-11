# PoC Phase 1 Definition of Done

Date: 2026-04-11  
Status: Draft (execution evidence tracker)

## Purpose

Provide a single signoff checklist for PoC Phase 1 closure across requirements, security, contracts, and DevOps policy constraints.

---

## 1) Requirement and acceptance closure

| ID | Description | Evidence | Status |
|---|---|---|---|
| AC-001 | Install/upgrade jobs submitted and terminal state observable | API/UI integration evidence | [ ] |
| AC-002 | Rollback/cancel actions produce auditable transitions | State transition/audit evidence | [ ] |
| AC-003 | Protocol assignment/lease/sequence and idempotent StepStatus behavior | Protocol + idempotency verification evidence | [ ] |
| AC-004 | Agent executes full job pipeline; orchestrator owns job-level sequencing/policy only | Architecture/orchestration evidence | [ ] |
| AC-005 | Bootstrap failure cleanup is reverse-order and complete | Bootstrap rollback evidence | [ ] |
| AC-006 | MSI/EXE adapter flow executes through typed pipeline | Adapter pipeline evidence | [ ] |
| AC-007 | Upgrade failure restores from `configSnapshotId` with linked audit event | Config persistence evidence | [ ] |
| AC-101 | `AssignedStale` and stale-timeout behavior meet PoC bounds | Lease/stale policy evidence | [ ] |
| AC-102 | Security baseline controls block unsigned artifacts and unauthorized operations | Security test evidence | [ ] |
| AC-103 | Root span + step-level correlation telemetry present | Observability evidence | [ ] |
| AC-104 | Runtime actions available through REST/CLI without script dependency | API/CLI policy evidence | [ ] |
| AC-105 | Self-contained orchestrator runs on clean machine without preinstalled .NET/IIS | Packaging validation evidence | [ ] |

---

## 2) Contract consistency closure

- [ ] Canonical sequence is consistent across `03/04/08/10` and install sequence diagrams.
- [ ] Lease defaults and stale timeout semantics are consistent across `03/05/08/10` and state diagrams.
- [ ] Reconnect resume contract uses `lastAcknowledgedSequence + 1` consistently.
- [ ] Config snapshot/migration/restore/audit linkage is coherent across `08/10/11`.
- [ ] Security trust boundaries and architecture diagram labels are aligned.

---

## 3) Security closure

- [ ] DFD trust boundaries (TB-01..TB-04) mapped to architecture components.
- [ ] STRIDE register entries (TH-001..TH-007B) have mitigation linkage.
- [ ] Mitigation rows (M-001..M-006) include owner, cadence, and evidence expectations.
- [ ] Hardening-only operational depth is explicitly deferred to `[Hardening Phase 2]`.

---

## 4) DevOps and deployment policy closure

- [ ] Pipeline pack enforces self-contained single-file orchestrator packaging with embedded React UI.
- [ ] Packaging validation requires clean-host launch without preinstalled .NET runtime/IIS.
- [ ] Direct workstation deployment from Azure DevOps pipeline remains explicit non-goal.
- [ ] Runtime install/upgrade/rollback actions are documented as Orchestrator API/CLI only.
- [ ] On-prem/air-gap gate constraints are explicit and testable.

---

## 5) Deferred-to-hardening confirmation

- [ ] Key/certificate rotation operational cadence details deferred.
- [ ] Extended incident/forensics workflow details deferred.
- [ ] Environment-matrix and rollout-ring automation deferred.
- [ ] Extended evidence retention/reporting operations deferred.

---

## 6) Signoff summary

| Area | Reviewer | Decision | Notes |
|---|---|---|---|
| Architecture/contracts | TBD | [ ] Pass / [ ] Block | |
| Security | TBD | [ ] Pass / [ ] Block | |
| DevOps/pipeline policy | TBD | [ ] Pass / [ ] Block | |
| Overall PoC closure | TBD | [ ] Pass / [ ] Block | |
