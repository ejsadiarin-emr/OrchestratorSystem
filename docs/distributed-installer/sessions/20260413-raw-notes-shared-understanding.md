# Shared Understanding Review: Raw Meeting Notes

Date: 2026-04-13
Inputs:
- `docs/distributed-installer/sessions/20260413-raw-meeting-notes.md`
- `docs/distributed-installer/sessions/20260413-storyboard-review-output.md`

Purpose:
- Normalize each question/point from raw notes into a shared understanding.
- Mark whether each point is already answered by storyboard review outputs and where.

## Legend
- Status:
  - `Answered by review output`
  - `Partially answered by review output`
  - `Open`

---

## 1) Required storyboard flow coverage

### Q1. How media is packaged (ISO, EXE)
- Shared understanding: PoC should primarily use signed self-contained EXE and optionally ZIP; ISO is deferred unless offline media handling is explicitly in PoC acceptance.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Section `A)` and `E)` favor PoC-focused, implementable packaging and removal of non-essential scope.

### Q2. Fresh install (main/orchestrator node)
- Shared understanding: Fresh orchestrator flow is required with explicit bootstrap config and verification gates (`/health`, UI, DB init, artifact path).
- Status: `Answered by review output`
- Evidence from review output:
  - Section `B)` (Coverage, Verification) and `E)` (keep concrete verification artifacts from A).

### Q3. Sub-node installation (remote install)
- Shared understanding: Manual bootstrap script on remote machine is acceptable for PoC; GPO/SCCM should be documented as considered alternatives, not required implementation.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `A)` notes alignment to manual bootstrap acceptance from raw notes.

### Q4. How updates are installed
- Shared understanding: Update flow must include pre-mutation snapshot/checkpoint, deterministic verification, and explicit failure outcomes.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `E)` rewrite guidance for retry/downgrade/rollback realism.

### Q5. Modify workload, including version changes and downgrade
- Shared understanding: Modify/update operations must be policy-driven (risk + approval + idempotency/retry class), with stricter controls for downgrade.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `E)` keep B policy classes and rewrite downgrade language; Section `D)` flags unsafe guarantee language.

### Q6. Orchestrator self-update handling
- Shared understanding: Self-update should not rely on naive in-place replacement while process is active; use staged swap/supervisor pattern.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `D)` self-update risk callout and Section `E)` explicit rewrite to staged swap.

---

## 2) Verification expectations

### Q7. Each step should be verified (e.g., REST API -> SQLite)
- Shared understanding: Every major flow step should have a gate with observable pass/fail evidence, not just narrative statements.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `B)` Verification score rationale and Section `E)` keep A's endpoint/command-level checks.

---

## 3) Security and transport questions

### Q8. Security on top of SignalR + mTLS
- Shared understanding: Use SignalR for control/status plane; bind steady-state identity via mTLS; enforce trust boundaries and auditable denials.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `B)` Technical correctness rationale; Section `E)` keep B transport boundary and keep A trust-boundary detail.

### Q9. Child process security (origin/trust/hardening)
- Shared understanding: Child processes must be launched by trusted agent runtime with constrained privileges, bounded resources/timeouts, argument sanitization, and audit metadata.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `B)` Security depth and Section `E)` keep child-process hardening controls from A.

### Q10. Can SignalR handle large chunks?
- Shared understanding: Do not use SignalR for large artifact payload transfer; use HTTP artifact endpoints with range/chunk download.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `B)` Technical correctness; Section `E)` explicit keep from B on transport boundary.

### Q11. How to integrate mTLS certs?
- Shared understanding: Enrollment should establish identity binding and transition to mTLS steady-state; cert trust/validation path must be explicit in docs.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Section `B)` and `E)` support boundary correctness, but detailed cert lifecycle/kms/ca ops specifics are still open.

---

## 4) Package source and artifact flow

### Q12. No external source: how agents pull packages?
- Shared understanding: Agents pull artifacts from orchestrator-owned endpoints only; no runtime dependency on external artifact source.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `A)` and `E)` remove any implied external package dependency.

### Q13. Fresh orchestrator: how do packages get in initially (drag-drop/UI upload/endpoint; zip/binaries)?
- Shared understanding: Internal ingestion path should support explicit upload mechanisms (UI/API) with signed artifact + hash metadata and versioned immutable records.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Section `A)`/`E)` align with internal-only flow, but exact operator UX baseline (UI drag-drop vs API-first) remains to finalize.

### Q14. Package trust/signing: how signed, where keys stored?
- Shared understanding: Must define signing authority and key custody minimum for PoC, including verification chain and trust root handling.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `C)` A missing/weak and Section `D)`/`E)` explicit rewrite requirement for key-management process.

### Q15. Package versions and channels (canary/test/SHA-based)
- Shared understanding: Versioning policy is required; channel semantics can be phased, but immutable version identity and policy guardrails should be explicit in PoC docs.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Section `E)` keeps manifest/policy rigor, but channel taxonomy is not fully specified.

---

## 5) Job pipeline semantics and complexity

### Q16. Serial vs parallel package/job execution distinction
- Shared understanding: Execution mode should be explicit per package/job with observable step progression and safe concurrency controls.
- Status: `Open`
- Notes:
  - Storyboard review did not explicitly settle serial/parallel scheduling policy.

### Q17. Indicator of current running job/step
- Shared understanding: UI/API timeline should show ordered step status with correlation keys (`jobId`, `nodeId`, `step`, reason codes).
- Status: `Answered by review output`
- Evidence from review output:
  - Section `B)` Verification quality and `E)` keep concrete verification artifacts.

### Q18. Handling long multi-step and brittle jobs; deciding retryability
- Shared understanding: Retry must be policy-based and bounded, using structured error classification and idempotency/risk tags.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `D)` retry contradiction risk; Section `E)` keep B policy classes and rewrite retry semantics.

### Q19. Self-healing and idempotency tags for risky/non-idempotent jobs
- Shared understanding: Tagging model is required (`retryabilityClass`, `idempotencyMode`, `riskLevel`, `approvalRequired`) and should gate execution branch logic.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `E)` explicit keep from B and rewrite guidance.

---

## 6) Data, observability, and persistence

### Q20. Use SQLite instead of SQL Server for PoC
- Shared understanding: SQLite is acceptable and preferred for PoC control-plane simplicity.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `A)` PoC-first positioning and Section `B)` feasibility rationale favor constrained scope.

### Q21. SQLite for logs/OTel? PostgreSQL?
- Shared understanding: Do not overbuild DB-backed observability in PoC; prefer simpler file/OTLP with retention/rotation and minimal sensitive data exposure.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Section `A)` and `E)` prefer implementable PoC constraints, but exact default logging backend policy is still not explicitly locked here.

### Q22. OTel security and log data exposure
- Shared understanding: Telemetry should include least-sensitive fields by default, with explicit policy for redaction and access control.
- Status: `Open`
- Notes:
  - Review output implies security depth needs but does not define concrete redaction/access policy.

---

## 7) Operational boundaries and scope

### Q23. Distinction between Ping (orch -> agent) vs LeaseHeartbeat (agent -> orch)
- Shared understanding:
  - `Ping`: orchestrator-driven liveness probe and connectivity signal.
  - `LeaseHeartbeat`: agent-driven lease renewal for assignment ownership/staleness/reassignment logic.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Covered indirectly through correctness/security criteria, but not explicitly resolved in the final review narrative.

### Q24. Windows-first use case; optional Windows-to-Linux showcase
- Shared understanding: Windows-first is primary PoC target; Linux agent showcase is optional/non-blocking.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Section `E)` remove non-essential expansion from core PoC and keep future items in appendix.

### Q25. Trust boundary annotations in architecture diagram
- Shared understanding: Trust boundaries must be explicit with principal, direction, controls, and threat assumptions.
- Status: `Answered by review output`
- Evidence from review output:
  - Section `E)` keep A trust-boundary structure/detail.

### Q26. Scale assumptions (no need for multi-orchestrator now)
- Shared understanding: Single-orchestrator architecture is acceptable for PoC; avoid introducing multi-orchestrator complexity in baseline flows.
- Status: `Partially answered by review output`
- Evidence from review output:
  - Section `A)` and `E)` emphasize PoC-feasible constraints and trimming non-essential complexity.

---

## 8) Consolidated answered-by-review mapping (quick index)

Fully/mostly answered by review output:
- Q2, Q3, Q4, Q5, Q6, Q7, Q8, Q9, Q10, Q12, Q14, Q17, Q18, Q19, Q20, Q25

Partially answered by review output:
- Q1, Q11, Q13, Q15, Q21, Q23, Q24, Q26

Still open (not explicitly settled by review output):
- Q16, Q22

---

## 9) Recommended next alignment pass

1. Lock explicit serial vs parallel execution policy and where it is configurable (Q16).
2. Lock OTel data classification/redaction + retention default for PoC (Q22).
3. Finalize operator baseline for package ingestion UX (UI drag-drop vs API-first), preserving internal-only source constraint (Q13).
4. Write one short normative section for Ping vs LeaseHeartbeat behavior and failure semantics (Q23).
5. Add a concise signing/key-custody PoC minimum standard to storyboard merge output (Q14 carry-through).
