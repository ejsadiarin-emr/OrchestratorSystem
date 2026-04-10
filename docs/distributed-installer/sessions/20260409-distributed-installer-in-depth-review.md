# Distributed Installer PoC: In-Depth Gap and Reality Review

Date: 2026-04-09  
Scope: Validation review of current PoC research/design docs against meeting notes and `learning-plan.md`

---

## 1) Scope and Review Method

This review covers:

- `learning-plan.md`
- `docs/distributed-installer/README.md`
- `docs/distributed-installer/00-meeting-summary-notes.md`
- `docs/distributed-installer/01-research-report.md`
- `docs/distributed-installer/02-market-and-ansible-comparison.md`
- `docs/distributed-installer/03-architecture-and-design.md`
- `docs/distributed-installer/04-agent-bootstrap-and-communication.md`
- `docs/distributed-installer/05-orchestration-and-validation.md`
- `docs/distributed-installer/06-testing-strategy.md`
- `docs/distributed-installer/07-security-reliability-observability.md`
- `docs/distributed-installer/adr/*.md` (ADR-001 through ADR-011)
- `docs/distributed-installer/diagrams/*.ascii.md`
- `docs/distributed-installer/mockups/dashboard-wireframes.md`
- `docs/distributed-installer/sessions/20260407-gap-analysis-meeting-notes.md`
- `docs/distributed-installer/sessions/20260407-learning-plan-gap-analysis.md`

Validation approach used:

- Cross-map meeting questions to current architecture docs and ADR decisions.
- Cross-map Day 3-10 learning-plan expected artifacts to actual deliverables.
- Run independent architecture/devops/security specialist reviews for reality checks.
- Identify contradictions, unresolved assumptions, and implementation blockers.

---

## 2) Executive Verdict

The direction is **mostly sound for a PoC**, but **not yet complete for the learning-plan artifact bar**.

### What is now solid

- Bootstrap strategy exists for PoC (`WinRM` one-time push) in `04-agent-bootstrap-and-communication.md` and `ADR-010`.
- Agent protocol decision exists (`SignalR`) in `04-agent-bootstrap-and-communication.md` and `ADR-007`.
- Queue/library decision exists (`Hangfire` orchestrator + `Channel<T>` agent) in `05-orchestration-and-validation.md` and `ADR-008`.
- Dry-run confidence model exists in `05-orchestration-and-validation.md` and `ADR-011`.
- Persistent-agent decision exists in `ADR-009` (ephemeral model explicitly rejected for PoC).

### What is still blocking full plan closure

- Critical learning-plan artifacts are still missing or incomplete (notably formal FR/NFR contract, Day 3 modernization map, security DFD/STRIDE register details, OpenAPI/data schemas, config persistence contract, and DevOps pipeline artifacts).
- Some key docs conflict with each other on orchestration semantics (push vs pull and DAG ownership).
- Security posture is directionally good but still too high-level for implementation-safe execution in enterprise environments.

Bottom line: **architecture direction is valid**, but **documentation maturity is not yet implementation-ready relative to `learning-plan.md` outcomes**.

---

## 3) Meeting-Notes Gap Status (Reality Check)

Based on `docs/distributed-installer/sessions/20260407-gap-analysis-meeting-notes.md` and current docs:

| Meeting Question / Gap | Current Status | Evidence | Validity Notes |
|---|---|---|---|
| How to install agent on remote machines | Addressed (PoC) | `04-agent-bootstrap-and-communication.md`, `adr/ADR-010-winrm-bootstrap.md` | Valid for PoC; enterprise hardening still needed |
| How agents communicate with master node | Addressed | `04-agent-bootstrap-and-communication.md`, `adr/ADR-007-signalr-protocol.md` | Protocol chosen, but wire-level contract/versioning still thin |
| In-memory C# queue equivalent and orchestration | Addressed | `05-orchestration-and-validation.md`, `adr/ADR-008-queue-libraries.md` | Good PoC choice |
| Dry-run realism / confidence | Addressed (conceptually) | `05-orchestration-and-validation.md`, `adr/ADR-011-dry-run-confidence.md` | Needs calibration data and operational policy |
| Do remote machines need same config? | Addressed | `03-architecture-and-design.md` (machine heterogeneity section) | Clear and sound |
| Ephemeral agents | Answered by decision | `adr/ADR-009-persistent-agents.md` | Rejected for PoC; rationale acceptable |
| Auto-rollback on failed *agent bootstrap* | **Open** | Not explicitly defined | Install rollback exists; bootstrap rollback does not |
| Self-hosted CI/CD runner pattern research (GitHub Actions style) | **Open** | Not explicitly documented | Requested in meeting gap notes but still absent |

---

## 4) Learning Plan Coverage (Day 3-Day 10)

This section maps expected artifacts from `learning-plan.md` to current deliverables.

### Day 3: DeltaV current-state + modernization map

Status: **Missing (Critical)**

- Missing: current-state assessment (install flow/component/tech map)
- Missing: modernization map (`current -> target -> migration strategy -> risk/effort`)
- Missing: explicit legacy constraints on new architecture

Confirmed in: `docs/distributed-installer/sessions/20260407-learning-plan-gap-analysis.md`

### Day 4: Deployment options + automation tooling

Status: **Partial (Important)**

- Partial: decision matrix exists in `01-research-report.md`, but incomplete versus Day 4 expectations.
- Missing: explicit config persistence strategy and concrete Azure DevOps boundary contract.
- Partial: no-scripting guardrails are implied but inconsistently documented due bootstrap PowerShell usage.

### Day 5: Requirements definition (FR/NFR + constraints)

Status: **Partial to Missing (Critical)**

- Missing: numbered functional requirements set.
- Missing: consolidated NFR document (currently scattered).
- Partial: deterministic/testable/modular/distributed principles exist in prose but not as formal constraints.
- Missing/weak: explicit EOL/version awareness and config-preservation requirement contract.

### Day 6: Security design and threat modeling

Status: **Partial (Critical gap remains)**

- Present: security baseline and STRIDE highlights (`07-security-reliability-observability.md`, ADR-006).
- Missing: security DFD with trust boundaries.
- Missing: full threat surface map and STRIDE register with likelihood/impact scoring.
- Missing: secure coding checklist tied to implementation points.

### Day 7: Architecture document depth

Status: **Partial (Important)**

- Present: high-level component architecture and happy-path scenarios.
- Missing: full deployment scenario sequences (upgrade/rollback/uninstall/silent/batch) at the same level as happy path.
- Missing: explicit upgrade/config lifecycle contract.
- Missing: concrete CLI and automation contract details.
- Partial: self-contained packaging decision exists, but build/deploy detail is not complete.

### Day 8: Detailed design (core)

Status: **Missing (Critical)**

- Missing: OpenAPI endpoint contract list.
- Missing: core data models and state store schema.
- Missing: explicit `IInstallStep` interface definitions and execution contracts.
- Missing: detailed test harness design for filesystem/service/registry mocking.

### Day 9: Detailed design (cross-cutting)

Status: **Mostly Missing (Critical)**

- Missing: OTel SDK setup patterns and collector pipeline detail at implementation level.
- Missing: CLI command spec (`System.CommandLine`) with options/exit codes/JSON output.
- Missing: JSON manifest schema.
- Missing: config backup schema and migration contract.
- Partial: dashboard wireframes exist, but component/state contract detail is thin.

### Day 10: DevOps pipeline design

Status: **Missing (Important)**

- Missing: pipeline YAML (`azure-pipelines.yml`) design artifact.
- Missing: stage diagram and gate policy.
- Missing: branch policy documentation and artifact versioning strategy.

---

## 5) Soundness Assessment of Core Decisions

### 5.1 Hybrid control plane (custom Orchestrator + Agent)

Verdict: **Sound for this problem domain and PoC scope**.

Why:

- Fits Windows-first, air-gapped, legacy-heavy requirements.
- Enables deterministic state-machine behavior and richer operator UX.
- Avoids over-fitting to Ansible runtime constraints.

References: `01-research-report.md`, `02-market-and-ansible-comparison.md`, `adr/ADR-001-hybrid-control-plane.md`

### 5.2 Persistent agents (not ephemeral)

Verdict: **Sound for PoC and enterprise operations**.

Why:

- Lower complexity and lower job-start latency.
- Better fit for managed endpoints.

Tradeoff:

- Requires stronger update/credential lifecycle discipline.

References: `03-architecture-and-design.md`, `adr/ADR-009-persistent-agents.md`

### 5.3 SignalR for agent comms

Verdict: **Sound for PoC, conditional for scale**.

Why:

- Native .NET integration and reconnection support.

Conditions:

- Need explicit message ordering/idempotency/ack strategy to avoid duplicate or out-of-order status side effects.

References: `04-agent-bootstrap-and-communication.md`, `adr/ADR-007-signalr-protocol.md`

### 5.4 Hangfire + Channel<T>

Verdict: **Pragmatic and valid PoC choice**.

Why:

- Durable orchestrator queue + lightweight agents is a good split.

Conditions:

- Must define lease/claim/recovery semantics to avoid crash-window ambiguity.

References: `05-orchestration-and-validation.md`, `adr/ADR-008-queue-libraries.md`

### 5.5 Dry-run confidence framework

Verdict: **Good concept, currently under-specified operationally**.

Why:

- Better than binary precheck pass/fail for high-risk deployments.

Conditions:

- Requires empirical calibration and explicit operator policy for medium-confidence gates.

References: `05-orchestration-and-validation.md`, `adr/ADR-011-dry-run-confidence.md`

---

## 6) Internal Contradictions and Ambiguities

1. **Pull-first vs push assignment ambiguity**
   - `ADR-002` says pull/claim model, while protocol docs show orchestrator pushing `AssignJob` via SignalR.
   - Needs one canonical sequence definition to prevent implementation drift.

2. **DAG ownership ambiguity**
   - `05-orchestration-and-validation.md` implies orchestrator resolves and dispatches DAG steps.
   - `04-agent-bootstrap-and-communication.md` describes agent child process executing sequential pipeline steps.
   - Decide whether agents execute whole jobs or single dispatched steps.

3. **No-scripting stance vs PowerShell bootstrap**
   - Learning plan discourages scripting as automation surface.
   - Current PoC relies on PowerShell for bootstrap.
   - This is acceptable only if explicitly scoped as provisioning exception.

4. **Air-gapped/no-cloud context vs cloud secret-store mention**
   - `07-security-reliability-observability.md` includes cloud key-vault option despite no-cloud constraints.
   - Should prioritize on-prem-first secret strategy in canonical docs.

5. **State-machine detail mismatch across docs/diagrams**
   - `03-architecture-and-design.md` has richer states than `diagrams/job-state-machine.ascii.md`.
   - Keep one canonical state model and derive diagrams from it.

6. **Session references to old file naming**
   - `sessions/20260407-gap-analysis-meeting-notes.md` references `03-poc-design-spec.md` instead of current file naming.

---

## 7) Top Risks (Impact, Probability, Mitigation)

| ID | Risk | Impact | Probability | Mitigation |
|---|---|---|---|---|
| R1 | Control model ambiguity (pull vs push) causes inconsistent implementation | High | High | Publish one canonical protocol/dispatch flow |
| R2 | Orchestrator-vs-agent DAG ownership mismatch | High | High | Define job execution ownership and update docs/ADRs consistently |
| R3 | Bootstrap failure leaves partial agent install state | High | Medium | Add bootstrap transaction/cleanup/rollback flow |
| R4 | Message replay/out-of-order status updates in reconnect scenarios | High | Medium | Add sequence IDs, idempotent upsert rules, ack contract |
| R5 | EXE/legacy adapter nondeterminism breaks idempotency claims | High | High | Strict adapter allowlists, verification hooks, exit-code mapping |
| R6 | Confidence scores miscalibrated, causing false safety or unnecessary blocks | Medium | Medium | Track prediction accuracy and recalibrate thresholds |
| R7 | Self-contained packaging without patch cadence governance | High | Medium | Define update channels, rollback bundle policy, signer governance |
| R8 | Security controls too abstract for implementation | High | Medium | Choose concrete auth/key lifecycle for PoC now |
| R9 | Missing config persistence contract blocks upgrade credibility | High | Medium | Define backup schema + migration/restore contract |
| R10 | Missing DevOps boundary and pipeline artifacts delays delivery | Medium | High | Add explicit CI/CD scope and pipeline stage contracts |

---

## 8) Required Artifacts to Meet Learning Plan (Practical Closure List)

Recommended artifact backlog to close critical gaps quickly.

### Priority 0 (must-have before implementation)

1. **Requirements Contract**
   - Numbered FRs + consolidated NFRs + explicit architecture constraints.
2. **Execution Model Clarification**
   - Canonical dispatch/execution sequence (pull/push semantics + DAG ownership).
3. **Security Threat Pack**
   - Security DFD, threat surface map, STRIDE register with likelihood/impact, secure coding checklist.
4. **Core Contracts Pack**
   - OpenAPI list, data model schema, manifest schema, `IInstallStep` interface contracts.
5. **Config Persistence Contract**
   - Backup schema, migration contract, rollback-of-config behavior.

### Priority 1 (needed for implementation planning confidence)

6. **Automation Contract**
   - CLI command spec + REST automation boundary + Azure DevOps role boundary.
7. **DevOps Pipeline Design Pack**
   - Stage diagram, quality gates, branch policy, artifact versioning strategy.
8. **Bootstrap Hardening Spec**
   - Bootstrap failure rollback, token lifecycle, artifact verification before service registration.

### Priority 2 (credibility and scale-readiness)

9. **SCCM/GPO enterprise bootstrap path** (post-PoC extension).
10. **UNC large-media staging strategy** (throughput, retry/resume, integrity verification policy).

---

## 9) Assumptions Requiring Validation

These assumptions drive many architectural choices and should be confirmed explicitly:

1. PoC environment permits WinRM and required remoting privileges.
2. Persistent agents are acceptable as the default model (ephemeral not required for PoC success criteria).
3. SQL Server is available and acceptable as queue/state backend for PoC.
4. PowerShell is acceptable for bootstrap only, while runtime automation remains C# + manifests.
5. Artifact signing and certificate trust chain can be managed fully on-prem.
6. Medium-confidence deployments can require human confirmation without breaking operator workflow.

---

## 10) Questions for Stakeholder Validation (Consultation)

Please validate these decisions so the docs and plan can be finalized without ambiguity:

1. **Dispatch semantics**: do you want strictly pull-claim by agents, or pull-connect plus orchestrator push assignment over SignalR?
2. **Execution ownership**: should the agent execute full job pipelines, or should orchestrator dispatch each pipeline step separately?
3. **Bootstrap policy**: is PowerShell/WinRM an accepted PoC exception to the no-scripting runtime principle?
4. **Auth model for PoC**: choose one primary approach now (certificate-based mutual auth vs token/HMAC path) to avoid split designs.
5. **Upgrade realism target**: which concrete package should be the reference for config backup/migration/restore (e.g., SQL Server component vs DeltaV-specific component)?
6. **DevOps boundary**: confirm that Azure DevOps will only build/test/deploy framework artifacts, while all workstation actions must flow through Orchestrator APIs/CLI.

---

## 10A) Recommended Decision Lock (Why This Over Alternatives)

The following recommendations answer Section 10 and include explicit rationale versus alternatives.

| # | Recommended Decision | Why this over the other option(s) |
|---|---|---|
| 1 | **Dispatch semantics**: keep **agent-initiated persistent connection** (pull-connect) and use **orchestrator push assignment** over SignalR with `Assign -> Ack/Claim -> Lease -> Complete/Fail`. | Better than strict polling-only pull because it reduces assignment latency and control-plane lag. Better than pure push-without-claim because reconnect windows create duplicate/ambiguous ownership; claim+lease closes that gap. |
| 2 | **Execution ownership**: agent executes the full per-job pipeline; orchestrator owns only job-level sequencing/dependencies and policy. | Better than orchestrator step-by-step remote dispatch because that creates chatty coupling and brittle failure handling. Better than fully autonomous per-agent DAG execution because cross-node coordination and centralized visibility become weaker. |
| 3 | **Bootstrap policy**: allow PowerShell/WinRM as a **PoC provisioning exception only**; runtime automation remains C# + manifests + REST/CLI. | Better than “no scripting at all” for PoC because fresh-host provisioning is otherwise impractical. Better than allowing scripts in runtime automation because it re-introduces non-deterministic shell glue and weakens typed contracts. |
| 4 | **Auth model**: use short-lived enrollment token for bootstrap, then mTLS (per-agent cert identity) for steady-state operations. | Better than long-lived API key/HMAC as primary identity because cert identity gives stronger node binding and revocation posture. Better than mTLS-only from first packet because initial trust bootstrapping still needs controlled enrollment. |
| 5 | **Upgrade realism target**: first prove config backup/migration/restore on a DeltaV-adjacent workstation component (file+service lifecycle) before SQL Server-grade packages. | Better than SQL Server-first because SQL setup complexity can obscure framework validation and slow feedback cycles. Starting with a representative but smaller package de-risks the framework contract first. |
| 6 | **DevOps boundary**: Azure DevOps builds/tests/signs/publishes framework artifacts and deploys orchestrator only; workstation actions are triggered via Orchestrator API/CLI (including automation). | Better than direct pipeline-to-workstation deployment because direct push bypasses orchestrator audit trail, policy checks, and unified execution model. This preserves single-source operational truth. |

---

## 11) Recommended Next Sequence

1. Lock answers to Section 10 questions.
2. Produce Priority 0 artifacts (Section 8).
3. Re-run a gap closure audit against `learning-plan.md` and both session gap-analysis docs.
4. Freeze architecture baseline and move into implementation planning.

---

## 12) Final Assessment

The current PoC proposal is **architecturally credible** and much stronger than the earlier gap baseline. Key design decisions (persistent agent, SignalR, Hangfire + Channel, dry-run confidence model) are broadly sound for PoC goals.

However, **critical artifact completeness is still below the learning-plan target**, and a few contradictions can create execution churn if not resolved now. Closing the listed Priority 0 and Priority 1 artifacts will make the design materially implementation-ready.
