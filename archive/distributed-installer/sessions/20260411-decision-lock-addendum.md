# Distributed Installer PoC: Decision Lock Addendum

Date: 2026-04-11  
Scope: Canonical decision lock for contradictions, ambiguities, and Priority 0/1 closure sequencing

---

## 1) Purpose

This addendum records stakeholder-approved answers to unresolved questions and contradictions identified in:

- `docs/distributed-installer/sessions/20260409-distributed-installer-in-depth-review.md`

It is the single checkpoint artifact before applying broad reconciliation edits across architecture docs, ADRs, and diagrams.

---

## 2) Locked Decisions

### D1. Dispatch semantics

Lock: Use **agent-initiated persistent connection** with **orchestrator push assignment** over SignalR.

Canonical sequence foundation: `Assign -> Ack/Claim -> Lease -> Complete/Fail`.

### D2. Execution ownership

Lock: Orchestrator owns job-level sequencing/dependencies/policy. Agent executes the full per-job pipeline.

### D3. Scheduling granularity

Lock: Runtime dispatch is job-level, not orchestrator step-by-step remote dispatch. Step progress is reported as events.

### D4. Bootstrap scripting boundary

Lock: PowerShell/WinRM scripting is allowed for **bootstrap provisioning only**. Runtime orchestration remains C# + manifests + REST/CLI.

### D5. Bootstrap channel policy

Lock:

- PoC: WinRM allowed
- Enterprise: GPO/SCCM preferred
- All bootstrap channels may install/register/update agent bootstrap artifacts only
- Runtime package jobs must run via Orchestrator API/CLI

### D6. Product boundary justification

Lock: GPO/SCCM are bootstrap and distribution/lifecycle channels for agents; distributed installer is the runtime orchestration control plane.

Recorded in ADR:

- `docs/distributed-installer/adr/ADR-012-enterprise-bootstrap-vs-runtime-orchestration.md`

### D7. Auth model (PoC)

Lock: Short-lived enrollment token for bootstrap, then mTLS per-agent certificate identity for steady state.

### D8. Upgrade realism target phasing

Lock:

- Phase 1 proof target: DeltaV-adjacent workstation component (file + service lifecycle)
- Phase 2: SQL Server-grade installer class
- Exact package identity deferred pending Day 3 outputs

Recorded in ADR:

- `docs/distributed-installer/adr/ADR-013-upgrade-realism-target-phasing.md`

### D9. DevOps boundary

Lock: Azure DevOps builds/tests/signs/publishes framework artifacts and deploys orchestrator only. Workstation install actions are triggered through Orchestrator API/CLI.

### D10. Bootstrap rollback semantics

Lock: Bootstrap is transactional with compensation on failure, scoped to provisioning only.

Failure cleanup includes reverse-order teardown (service stop/remove, file/config cleanup, token invalidation, audit event).

### D11. Message ordering and idempotency

Lock:

- `AssignJob` includes `assignmentId`, `leaseId`, monotonic `sequence`
- Status updates are idempotent upserts keyed by `(jobId, stepId, sequence)`
- Orchestrator ignores stale/out-of-order updates
- Reconnect requires resume handshake using last acknowledged sequence

### D12. Canonical state-model source

Lock: `docs/distributed-installer/03-architecture-and-design.md` is canonical source for job states. Diagrams and dependent docs must derive from it.

### D13. On-prem/no-cloud secret strategy

Lock: PoC secret strategy is on-prem first (DPAPI/cert store/credential manager). Cloud vault references are future optional extension only.

### D14. Legacy reference hygiene

Lock: Active specs/ADRs must reference current file names only. Historical references can remain in session docs if explicitly marked historical.

### D15. Security pack minimum

Lock: Single security pack must contain:

- DFD with trust boundaries
- STRIDE register with likelihood/impact scoring
- Mitigation-to-component mapping table
- Secure coding checklist mapped to touchpoints (`Orchestrator API`, `SignalR hub`, `Agent executor`, `Legacy adapters`)

### D16. Core contracts pack

Lock: One consolidated Core Contracts Pack will include:

- API endpoint inventory (pre-OpenAPI)
- Canonical data model/entity list
- Manifest schema draft
- `IInstallStep` + `IPreCheck` interfaces
- Agent/orchestrator message contracts

### D17. Canonical runtime protocol sequence

Lock:

`Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

Includes explicit timeout and reassignment rules.

### D18. Lease defaults (PoC)

Lock:

- Lease TTL: `90s`
- Heartbeat interval: `15s`
- Stale threshold: `3` missed heartbeats
- Stale action: mark `AssignedStale`, then safe reassignment after idempotency guard

### D19. State extension for stale leases

Lock: Add `AssignedStale` as explicit canonical state.

### D20. Safe reassignment gate

Lock: Reassignment requires replay-safe checkpoint + no active prior lease heartbeat in grace window + mutation checkpoint consistency.

### D21. Stale timeout bounds

Lock: `AssignedStale` auto-fails with `lease_timeout_exhausted` after either:

- 2 reassignment attempts, or
- 15 minutes total stale duration

### D22. Config persistence minimum contract

Lock:

- Pre-mutation snapshot (`configSnapshotId`)
- Schema version on snapshot and target package
- Deterministic migration path (`vN -> vN+1`)
- Rollback restore from snapshot on failure
- Audit linkage (`jobId`, `nodeId`, `snapshotId`, migration result)

### D23. Requirements contract format

Lock: One requirements artifact with IDs:

- `FR-###`
- `NFR-###`
- `AC-###`

Plus traceability map from requirements to ADRs and planned test types.

### D24. DevOps artifact minimum

Lock: DevOps closure requires:

- Stage diagram
- `azure-pipelines.yml` skeleton (CI, deploy-orchestrator, integration, E2E)
- Gate policy table
- Artifact version rule (`semver + build metadata`)
- Explicit no-direct-workstation-deploy statement

### D25. Reconciliation strategy

Lock: Apply **Option A**.

1. Record this decision-lock addendum
2. Perform second-pass reconciliation edits across core docs and ADRs

---

## 3) Immediate Reconciliation Backlog (Second Pass)

1. Resolve push/pull wording and runtime sequence consistency across:
    - `docs/distributed-installer/03-architecture-and-design.md`
    - `docs/distributed-installer/04-agent-bootstrap-and-communication.md`
    - `docs/distributed-installer/05-orchestration-and-validation.md`
    - `docs/distributed-installer/adr/ADR-002-agent-pull-first.md`
    - `docs/distributed-installer/adr/ADR-007-signalr-protocol.md`

2. Resolve DAG ownership wording (job-level orchestration vs per-step dispatch) in:
    - `docs/distributed-installer/05-orchestration-and-validation.md`

3. Add bootstrap transaction/rollback semantics in:
    - `docs/distributed-installer/04-agent-bootstrap-and-communication.md`
    - `docs/distributed-installer/adr/ADR-010-winrm-bootstrap.md`

4. Align secret strategy with no-cloud PoC scope in:
    - `docs/distributed-installer/07-security-reliability-observability.md`

5. Align state machine artifacts:
    - canonical states in `docs/distributed-installer/03-architecture-and-design.md`
    - derived diagram in `docs/distributed-installer/diagrams/job-state-machine.ascii.md`

6. Fix legacy file-name references in active docs and annotate historical session references where needed:
    - `docs/distributed-installer/sessions/20260407-gap-analysis-meeting-notes.md`

7. Create missing closure artifacts (Priority 0/1 packs):
    - Requirements Contract
    - Security Pack
    - Core Contracts Pack
    - Config Persistence Contract
    - DevOps Pipeline Design Pack

---

## 4) Notes on Day 3 Dependency

Exact phase-1 package identity remains intentionally deferred until Day 3 outputs are complete:

- current-state install flow/component map
- legacy technology map
- modernization map with risk/effort

This does not block phase-1 framework contract definition.
