# PoC Phase 1 Final PRD

Date: 2026-04-17
Status: Canonical source of truth for PoC Phase 1
Owner: Product + Architecture + Security

## Document governance

This PRD is the canonical policy and requirements document for PoC Phase 1. If there is any conflict across documentation, this PRD wins.

Active documentation set for PoC Phase 1:

1. `docs/prd-phase1.md` (this document)
2. `docs/implementation-tracker-phase1.md`
3. `docs/orchestrator-control-plane/CONTEXT.md`
4. `docs/agent-runtime/CONTEXT.md`
5. `docs/artifact-workload-catalog/CONTEXT.md`
6. `docs/CONTEXT-MAP.md`
7. `docs/distributed-installer/` (legacy historical snapshot; read-only)

Content consolidated into this PRD from prior packs/reports:

- meeting/research docs
- architecture, communication, orchestration, testing docs
- security/contracts/config/devops packs
- decision addendum and phase-1 definition of done
- ADR decisions including artifact metadata enrichment (ADR-014)

Implementation phasing tags:

- `[PoC Phase 1]` required for implementation baseline and signoff
- `[Hardening Phase 2]` explicitly deferred and must not block Phase 1

ID conventions:

- `FR-###` functional requirement
- `NFR-###` non-functional requirement
- `AC-###` acceptance criteria linked to FR/NFR
- `TB-##` trust boundary
- `TH-###` threat register row
- `M-###` mitigation mapping row

---

## Problem statement

Teams need a Windows-first distributed installer that can safely install, update, rollback, cancel, and observe deployment across remote nodes from one central orchestrator.

The prior package-centric model caused ambiguous update behavior and weak operator ergonomics. Product direction now requires first-class workloads so that deployment behavior is simple, deterministic, and auditable.

The PoC must prove three things:

1. runtime operations are deterministic and auditable,
2. trust boundaries are explicit and testable,
3. operators can understand and recover from failures quickly.

---

## Solution summary

Build a single-orchestrator, Windows-first PoC where system admins define workload revisions and execute workload runs through API/UI/CLI. Persistent agents execute a local typed pipeline for package steps in strict order.

Core posture:

- first-class `workloads` made of predefined package steps,
- immutable workload revisions,
- install and update lifecycle on both orchestrator node and agent nodes,
- runtime lifecycle APIs are workload-run only (`/api/workload-runs`); `/api/jobs` is non-runtime historical context,
- SignalR for control/status only and HTTP for artifact bytes,
- policy-governed risk decisions (riskLevel only; per-artifact retryabilityClass/idempotencyMode removed for Phase 1),
- step-level telemetry with required workload correlation fields,
- self-contained orchestrator executable with embedded React UI.
- agent runtime is headless; all operator visibility and control flows through the orchestrator API/UI.
- **running a workload from the Orchestrator UI is the primary Phase 1 demo goal** — all other features support this outcome.
- orchestrator embedded UI includes first-class local artifact store management with drag-and-drop upload while preserving canonical artifact ingest contracts.

---

## Scope and assumptions

### In scope [PoC Phase 1]

- Windows-first distributed workload orchestration across multiple nodes on LAN
- first-class workload definitions and immutable revisions
- workload lifecycle: install, update, rollback, cancel
- persistent agent runtime channel, lease ownership, and canonical sequencing
- full local per-run agent pipeline with orchestrator-owned plan/policy decisions
- typed adapter support for MSI/EXE execution paths
- config snapshot/migration/restore contract for mutation safety
- self-contained orchestrator packaging with embedded UI
- orchestrator embedded UI includes workload lifecycle and local artifact-store management surfaces
- agent packaging as headless Windows service (no local web UI)
- workload execution target may be an agent node or the orchestrator node

### Out of scope [PoC Phase 1]

- direct workstation deployment from Azure DevOps pipeline
- Linux agent implementation
- multi-orchestrator HA/DR commitments
- full rollout-ring automation and fleet hardening workflows
- runtime dependency on external package sources
- automatic dependency graph solver for workload packages
- automatic package removal during update (explicitly deferred)

### Assumptions

- PoC bootstrap is browser-based: operator downloads signed `agent.exe` via orchestrator web UI and uses a single-use enrollment token to authorize agent join
- internal signing/trust material can be managed on-premises
- medium-confidence operations may require explicit operator confirmation
- phase-1 persistence baseline is SQLite for canonical runtime entities
- each workload revision in PoC contains 2-3 packages
- workload revision content is immutable once published
- update workflow is fully automatic: pre-check → risk detection → status display in orchestrator → proceed via pre-defined upgrade paths
- operator views risk status in UI before update runs and can cancel, but no manual approval step is required

---

## User stories

### Workload definition and revision

1. As a system administrator, I can create a workload definition draft with a name, slug, and ordered package list, so that I can model the software I need to deploy.
2. As a system administrator, I can publish a workload definition as an immutable revision, so that deployed workloads have a fixed reference that cannot silently change.
3. As a system administrator, I can import workload definitions from a global JSON file containing multiple workloads, so that I can onboard 2-3 workloads in a single batch.
4. As a system administrator, I can view a list of all workload definitions and their revisions, so that I can understand what is available for deployment.
5. As a system administrator, I can see which package manifests a workload revision references, so that I can verify the composition of a workload before running it.
6. As a system administrator, I cannot modify a published workload revision, so that deployed workloads remain deterministic and reproducible.

### Workload run lifecycle

7. As a system administrator, I can submit a workload install run against selected target nodes, so that all packages in the revision are applied in order.
8. As a system administrator, I can submit a workload update run against selected nodes, so that only changed packages are executed in canonical order.
9. As a system administrator, I can submit a workload rollback run, so that a previous known state is restored using snapshot/restore contracts.
10. As a system administrator, I can cancel an active workload run, so that execution stops at a safe boundary with a persisted reason code.
11. As a system administrator, I can target both orchestrator node and agent nodes with workload runs, so that the orchestrator itself can be a deployment target.
12. As an operator, I can view live package-step timelines with terminal outcomes and reason codes, so that I can diagnose progress or failure in real time.
13. As an operator, I can see which workload revision is currently active on each node, so that I know the deployed state of the fleet.

### Artifact and manifest management

14. As an operator, I can upload artifact binaries with manifests via drag-and-drop or file picker, so that I can populate the local artifact store from the Orchestrator UI.
15. As an operator, I can view a list of ingested artifacts with version, channel, digest, and risk-level metadata, so that I can verify what is available before authoring workloads.
16. As an operator, I can view the detail of a single artifact including resolved manifest fields and their source provenance, so that I can audit which fields came from admin input vs. defaults.
17. As an operator, I receive clear field-level validation errors when artifact ingest is missing required fields, so that I can correct my submission before retrying.
18. As a system administrator, I can rely on deterministic default resolution (admin → template → analyzer → default) for artifact manifest fields, so that minimal input produces a complete manifest.
19. As a security reviewer, I can verify that signature verification `fail` blocks artifact ingest (fail-closed), so that tampered or unsigned artifacts never enter the catalog.
20. As a security reviewer, I can verify that signature verification `warn` elevates the manifest risk level to `high`, so that risk is surfaced even when ingest proceeds.

### Agent enrollment and bootstrap

21. As an operator, I can generate a one-time enrollment token from the Orchestrator UI, so that I can authorize a new agent node to join.
22. As an operator, I can open a browser-based download URL on a target node to download a signed agent.exe, so that I can install the agent without push scripts or WinRM.
23. As a security reviewer, I can verify that enrollment tokens are single-use and bound to mTLS certificate identity, so that a compromised token cannot be reused.
24. As a reliability engineer, I can verify that bootstrap failure triggers transactional rollback and token invalidation, so that a partially installed agent does not leave orphaned state.

### Runtime protocol and determinism

25. As a reliability engineer, I can verify that the runtime dispatch sequence (Connect → Register → AssignRun → AckClaim → LeaseHeartbeat → StepStatus → Complete/Fail → LeaseClose) is enforced, so that operations are deterministic.
26. As a reliability engineer, I can verify that step status ingest is idempotent with upsert key `(runId, nodeId, packageId, stepId, sequence)`, so that reconnects do not corrupt state.
27. As a reliability engineer, I can verify that stale/out-of-order updates are rejected and same-key payload mismatches are audited as `sequence_payload_conflict`, so that the timeline is tamper-evident.
28. As a reliability engineer, I can verify that lease stale policy (TTL 90s, heartbeat 15s, 3 missed stale threshold) behaves correctly under disconnect/reconnect, so that runs do not hang indefinitely.
29. As a reliability engineer, I can verify that stale assignments auto-fail after 2 reassignment attempts or 15 minutes, so that stuck runs have bounded recovery.

### Policy and risk

30. As a system administrator, I can define pre-upgrade actions (backup, stop) on workload revisions, so that critical preparation steps run before packages are upgraded.
31. As an operator, I can view risk-level status in the Orchestrator UI before an update proceeds, so that I am informed of elevated risk.
32. As a system administrator, I can rely on the update workflow proceeding automatically after pre-check and risk detection, so that operational velocity is not blocked by manual approval gates.
33. As a system administrator, I can cancel an update run before dispatch if I observe elevated risk, so that I retain operator control.

### Agent execution pipeline

34. As a reliability engineer, I can verify that the agent executes the full local package-step pipeline (PreConditionCheck → AcquireArtifact → ValidateSignatureAndHash → DetectCurrentState → InstallOrUpgrade → PostInstallVerify → EmitFinalization) in strict order, so that execution is deterministic.
35. As a reliability engineer, I can verify that MSI and EXE adapters produce normalized telemetry outcomes, so that different installer types report consistent status.
36. As a reliability engineer, I can verify that a failed pipeline step halts execution and reports the failure reason, so that partial runs are visible and diagnosable.

### Configuration mutation safety

37. As a reliability engineer, I can verify that any mutation path creates a pre-mutation config snapshot, so that state is recoverable.
38. As a reliability engineer, I can verify that mutation failure restores from the pre-mutation snapshot and emits linked audit events, so that config drift is prevented.

### Orchestrator UI and operator ergonomics

39. As an operator, I can open large centered detail popups for node and workload information, so that I can view diagnostics without losing dashboard context.
40. As an operator, I can see terminal-like mini logs inside detail popups, so that I can read real-time step output in a familiar format.
41. As an operator, I can access a dedicated local artifact-store management page with drag-drop and file-picker upload, so that artifact management is a first-class workflow.
42. As an operator, I can verify artifact version metadata and risk level before creating workload revisions, so that I make informed authoring decisions.
43. As an operator, I can see workload revision and package visibility throughout the UI, so that I always know what I am deploying.
44. As an operator, I can interact with the UI using terminology that says "nodes" and "workloads" instead of "fleet", so that the language matches the domain model.

### Security and audit

45. As an auditor, I can reconstruct actor, target, sequence, workload revision, and outcome from linked audit/telemetry evidence, so that operations are fully traceable.
46. As a security reviewer, I can verify that unauthorized roles are denied runtime actions via RBAC, so that privilege boundaries are enforced.
47. As a security reviewer, I can verify that no plaintext secrets exist in logs, config, or telemetry, so that secret hygiene is maintained.
48. As a security reviewer, I can verify that binary substitution attacks fail (signature/publisher validation on startup and update), so that executable integrity is protected.
49. As a security reviewer, I can verify that downgrade to a vulnerable signed build is prevented (version floor enforcement), so that anti-downgrade controls work.

### Observability

50. As an operator, I can query package-step telemetry by workloadId, workloadRevision, runId, nodeId, and reasonCode in Grafana/Loki, so that I can diagnose issues across the fleet.
51. As a reliability engineer, I can rely on OTel Collector → Loki → Grafana as the default observability stack, so that operational queries work without custom tooling.

### Packaging and deployment

52. As a release/platform owner, I can run the Orchestrator as a self-contained executable on a clean Windows host, so that no preinstalled .NET runtime or IIS is required.
53. As a release/platform owner, I can rely on CI/CD pipeline deploying the Orchestrator only, so that workstation deployment stays API/UI/CLI-driven through the Orchestrator.
54. As a release/platform owner, I can verify that the Orchestrator runs on-premises without public internet dependency, so that air-gapped environments are supported.

### CLI operations

55. As an operator, I can submit workload runs via CLI (`di workload-runs create`), so that I can script operational workflows.
56. As an operator, I can check run status via CLI (`di workload-runs status`), so that I can monitor runs from a terminal.
57. As an operator, I can cancel runs via CLI (`di workload-runs cancel`), so that I can intervene without opening the UI.
58. As an operator, I can list workloads and nodes via CLI, so that I can inspect the fleet from a terminal.
59. As an operator, I can upload artifacts via CLI (`di artifacts upload`), so that I can automate artifact ingestion.

### Deprecation and migration

60. As a developer, I can verify that `/api/jobs` mutation endpoints return `410 Gone` with replacement path information, so that no runtime path depends on legacy job APIs.
61. As a developer, I can verify that all runtime lifecycle operations use `/api/workload-runs` exclusively, so that the API surface is clean and deterministic.

---

## Workload model and lifecycle semantics

### Core model

| Term | Meaning |
|---|---|
| WorkloadDefinition | Logical unit with stable identity and display metadata |
| WorkloadRevision | Immutable published version of ordered package steps |
| WorkloadRun | Execution request for one workload revision against selected targets |
| NodeWorkloadState | Last known workload revision and package states on a node |
| PreUpgradeAction | Declarative requirement that must be satisfied before an upgrade proceeds (e.g., "backup database") |

### Lifecycle modes

- `install`: apply all packages in workload revision order.
- `update`: compute changed packages from node state to target revision, then execute only changed packages in canonical order.
- `rollback`: execute approved rollback path using snapshot/restore contract.
- `cancel`: stop at safe interruption boundaries and persist explicit terminal reason.

### Determinism rules [PoC Phase 1]

1. A workload run snapshots exact revision content at creation time.
2. Package execution order is fixed by revision order.
3. Update does not remove packages in Phase 1.
4. Node revision is promoted only after all required package steps succeed.
5. At most one active run per `(nodeId, workloadId)`.

---

## Implementation decisions

### Modules and boundaries

**Orchestrator Control Plane** — owns REST API, embedded React UI, SignalR runtime hub, workload registry, run planner, policy/lease logic, persistence, and artifact store management. Deep module: run planner and policy engine are isolated behind testable interfaces so sequencing and risk decisions can be unit-tested without infrastructure.

**Agent Runtime** — headless persistent Windows service owning enrollment handshake, local typed execution pipeline, adapter execution (MSI/EXE), config snapshot/migration, and telemetry emission. Deep module: execution pipeline uses a `IInstallStep` interface so each step (PreConditionCheck, AcquireArtifact, ValidateSignatureAndHash, DetectCurrentState, InstallOrUpgrade, PostInstallVerify, EmitFinalization) is testable in isolation.

**Artifact & Workload Catalog** — owns artifact storage, manifest ingest and validation, workload definition import, and versioning/immutability. Deep module: manifest resolution chain (admin → template → analyzer → default) and schema validation are isolated services testable without HTTP endpoints.

**Shared Runtime Contracts Library** — owns the canonical message envelope, protocol version, message types, and typed payloads. This library is the single source of truth for both Orchestrator and Agent, ensuring wire compatibility.

**CLI (`di`)** — thin CLI surface providing REST-parity commands for workload runs, workloads, nodes, and artifacts. No runtime logic; maps directly to REST API calls.

### Architectural decisions

1. **Single-orchestrator model**: one Orchestrator instance per PoC deployment; no multi-orchestrator HA/DR in Phase 1.
2. **Agent-initiated connection**: agents connect to the Orchestrator (pull-first via SignalR), not the reverse. Orchestrator never initiates connections to agents.
3. **Push API surface on `/api/workload-runs` only**: all runtime lifecycle operations (create, get, steps, cancel) use this endpoint family. `/api/jobs` mutations return `410 Gone`.
4. **Dual transport**: SignalR for control/status messages; HTTP for artifact payload transport (GET/HEAD + Range). No artifact bytes over SignalR.
5. **SQLite baseline persistence**: Phase 1 canonical runtime entities persist in SQLite. No external database dependency.
6. **Self-contained packaging**: Orchestrator ships as a single executable with embedded React UI. No preinstalled .NET runtime or IIS required on target hosts.
7. **Immutable workload revisions**: once published, revision content cannot change. Updates create new revisions.
8. **At-most-one active run per `(nodeId, workloadId)`**: concurrency guard enforced at persistence boundary.
9. **Update workflow is fully automatic**: pre-check → risk detection → status display → proceed. No manual approval gate. Operator can cancel before dispatch.
10. **Policy tag simplification**: Phase 1 manifest-level policy uses only `riskLevel` (`low|medium|high`). `retryabilityClass` and `idempotencyMode` are explicitly deferred to Hardening Phase 2.
11. **Update does not remove packages**: changed packages are upgraded in place; no package removal in Phase 1.
12. **Pre-upgrade actions replace upgrade paths**: declarative requirements (backup, stop) are mapped per workload revision, not per artifact.
13. **Global workload JSON import**: workload definitions are imported as a single JSON file containing 2-3 workloads per PoC file, not authored inline.
14. **Artifact ingest is a single multipart call**: `POST /api/artifacts` with `file` (binary), `manifest` (JSON, required), and `detachedSignature` (optional). Both drag-and-drop and file picker UI paths map to this same canonical contract.
15. **Config mutation safety**: any mutation path must create a pre-mutation snapshot, execute deterministic migration, restore on failure, and emit linked audit events.
16. **Observability defaults**: OTel Collector → Loki → Grafana. Required correlation fields: workloadId, workloadRevision, runId, nodeId, packageId, stepId, sequence, reasonCode.

### Key interfaces

- **Runtime message envelope**: canonical JSON envelope with protocolVersion, messageType, assignmentId, leaseId, runId, workloadId, workloadRevision, nodeId, sequence, and typed payload. All runtime messages use this shape.
- **Step status ingest upsert key**: `(runId, nodeId, packageId, stepId, sequence)`. Idempotent on repeat; rejected on stale/out-of-order or payload mismatch.
- **Artifact ingest contract**: multipart/form-data with required `file` and `manifest` parts; optional `detachedSignature`. Manifest must include `packageId`, `version`, `channel`, and `artifactType` (unless inferable). Resolved manifest retains per-field source provenance.
- **Agent pipeline step interface**: `IInstallStep` with `Name` and `ExecuteAsync(context, ct)`. Each step (PreConditionCheck through EmitFinalization) implements this interface.
- **Config migration interface**: `IConfigMigration` with `FromVersion`, `ToVersion`, and `ExecuteAsync(context, ct)`. Migrations are strict `vN → vN+1`, reversible by snapshot restore.
- **Lease manager**: TTL 90s, heartbeat 15s, stale after 3 missed heartbeats, auto-fail after 2 reassignment attempts or 15 minutes stale duration.

### Schema changes

- New entities: `WorkloadDefinitionEntity`, `WorkloadRevisionEntity`, `WorkloadPackageEntity`, `WorkloadRunEntity`, `NodeWorkloadStateEntity`, `AssignmentLeaseEntity`, `ConfigSnapshotEntity`.
- `WorkloadRevisionEntity` enforces immutability at persistence boundary; published revisions cannot be modified.
- Unique constraint on `(nodeId, workloadId)` for at-most-one active run guard.
- Artifact manifest resolves to full schema with per-field provenance (`admin|template|analyzer|default`).
- `/api/jobs` and `/api/jobs/{id}/cancel` return `410 Gone` with replacement path payload.

### Specific interactions

- Orchestrator assigns a run to an agent via `AssignRun` message over SignalR; agent acknowledges with `AckClaim`. Lease established with heartbeat cadence.
- Agent downloads artifact bytes via HTTP range requests to the Orchestrator's artifact endpoint (not over SignalR).
- Agent reports step status via `StepStatus` messages. Orchestrator ingests with idempotent upsert; stale/out-of-order messages are rejected.
- On disconnect, agent reconnects and resumes from `lastAcknowledgedSequence + 1`. Orchestrator does not re-queue completed steps.
- On lease stale, Orchestrator attempts reassignment (up to 2 attempts). After bounded stale timeout, run fails with explicit reason.
- Operator cancels via `POST /api/workload-runs/{runId}/cancel`. Agent receives cancel signal and stops at safe boundary.

## Architecture and operating model

### Core components

- **Orchestrator:** REST API, SignalR runtime hub, workload registry, run planner, policy/lease logic, persistence, embedded UI host.
- **Agent:** headless persistent Windows service, runtime client, local typed execution pipeline, adapter execution, telemetry emission.
- **Artifact store:** orchestrator-managed internal artifact source.
- **Operator surfaces:** API/UI/CLI for runtime actions; scripts are provisioning-only. Embedded orchestrator UI includes node/workload visibility, workload CRUD and run actions, workload-run timeline visibility, and local artifact-store management.

### Runtime transport boundaries

- SignalR: control/status only.
- HTTP: artifact payload transport only (`GET/HEAD + Range`).

### Canonical runtime sequence

`Connect -> Register/Authenticate -> AssignRun -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

### Lease and stale defaults

- lease TTL: `90s`
- heartbeat interval: `15s`
- stale threshold: `3` missed heartbeats
- stale timeout bound: auto-fail after 2 reassignment attempts or 15 minutes stale duration

### Trust boundaries

| ID | Boundary | Data crossing | Primary concern |
|---|---|---|---|
| TB-01 | Admin caller -> Orchestrator API | commands, auth context | spoofing, privilege abuse, repudiation |
| TB-02 | Agent -> Runtime hub | assignment/lease/status traffic | spoofing, replay, DoS |
| TB-03 | Orchestrator/Agent -> Artifact source | artifact retrieval and metadata | tamper, substitution |
| TB-04 | Orchestrator -> Audit/observability stores | security/operation events | repudiation, tamper |

---

## Core contracts (normative)

### API surface (Phase 1)

| Endpoint ID | Method | Path | Purpose |
|---|---|---|---|
| API-001 | POST | `/api/workloads` | create workload definition draft |
| API-002 | POST | `/api/workloads/{workloadId}/revisions` | create immutable workload revision |
| API-003 | POST | `/api/workloads/{workloadId}/publish` | publish workload revision |
| API-004 | GET | `/api/workloads` | list workload definitions/revisions |
| API-005 | GET | `/api/workloads/{workloadId}` | fetch workload detail |
| API-006 | POST | `/api/workload-runs` | submit install/update/rollback workload run |
| API-007 | GET | `/api/workload-runs/{runId}` | fetch run summary/state |
| API-008 | GET | `/api/workload-runs/{runId}/steps` | fetch package-step timeline |
| API-009 | POST | `/api/workload-runs/{runId}/cancel` | cancel active workload run |
| API-010 | GET | `/api/nodes` | list node health/registration |
| API-011 | POST | `/api/nodes/enroll` | issue one-time enrollment token |
| API-012 | POST | `/api/artifacts` | ingest artifact media + manifest |
| API-015 | GET | `/api/artifacts` | list artifact records for local artifact-store management |
| API-016 | GET | `/api/artifacts/{artifactId}` | fetch artifact detail/metadata for operator drilldown |
| API-013 | POST | `/api/jobs` | removed from runtime API surface (historical migration reference only) |
| API-014 | POST | `/api/jobs/{jobId}/cancel` | removed from runtime API surface (historical migration reference only) |

### Legacy endpoint history

`/api/jobs` mutation endpoints are not part of the runtime API surface for current Phase 1 implementation.

Historical migration context:

- Earlier migration guidance referenced `410 Gone` responses and replacement path `/api/workload-runs`.
- Current implementation guidance is unambiguous: use `/api/workload-runs` lifecycle APIs only.

### Runtime message envelope

```json
{
  "messageType": "AssignRun|AckClaim|LeaseHeartbeat|StepStatus|Complete|Fail|LeaseClose",
  "protocolVersion": "1.0",
  "messageId": "uuid",
  "timestampUtc": "2026-04-17T12:00:00Z",
  "assignmentId": "string",
  "leaseId": "string",
  "runId": "string",
  "workloadId": "string",
  "workloadRevision": "1.1.0",
  "nodeId": "string",
  "sequence": 1,
  "payload": {
    "packageId": "pkg-voice",
    "packageIndex": 2,
    "stepId": "install-or-upgrade"
  }
}
```

Status ingest/idempotency rules:

- upsert key is `(runId, nodeId, packageId, stepId, sequence)`
- stale/out-of-order updates are rejected
- same-key payload mismatch is rejected and audited as `sequence_payload_conflict`
- reconnect resumes from `lastAcknowledgedSequence + 1`

### Artifact ingest and manifest schema

- single streaming `multipart/form-data` call to `POST /api/artifacts`
- required parts: `file` (binary), `manifest` (json)
- optional part: `detachedSignature`
- manifest channel is strictly `stable|canary|test`
- agents retrieve bytes through HTTP artifact endpoints only
- orchestrator UI may use file picker or drag-and-drop input, but both map to the same canonical multipart ingest contract

Minimal required admin fields (required at request validation):

- `manifest.packageId`
- `manifest.version`
- `manifest.channel`
- `manifest.artifactType` (required unless type is confidently inferred from uploaded media)

System-prefilled defaults and resolution rules [PoC Phase 1]:

For fields outside the minimal required set, orchestrator resolves values using this deterministic chain:

1. explicit admin-provided value,
2. `PolicyTemplateService` profile match,
3. binary analyzer/vendor metadata,
4. platform hard defaults.

Persisted resolved manifests must retain per-field source provenance:

- `admin|template|analyzer|default`

Default values when a field is not provided and no higher-precedence source resolves it:

- `installAdapter.type`:
  - `msi` for MSI media,
  - `exe` for EXE media,
  - `archive` for ZIP/TAR.GZ media.
- `installAdapter.command`:
  - `artifact.bin` (or runtime-resolved extracted entrypoint for archive media).
- `installAdapter.arguments`:
  - MSI: `/qn /norestart`
  - EXE: `/quiet /norestart`
  - archive: empty string
- `installAdapter.expectedExitCodes`: `[0, 3010]`
- `installAdapter.timeoutSeconds`: `1800`
- `detection.type`: `version_manifest`
- `detection.path`: `<manifest.packageId>`
- `detection.expectedVersion`: `==<manifest.version>`
- `originMetadata.source`: `internal-upload`
- `originMetadata.publisher`: `unknown`
- `originMetadata.ingestedBy`: authenticated actor id
- `originMetadata.ingestedAtUtc`: server UTC timestamp
- `policyTags.riskLevel`: `medium`

Security overrides:

- verification result `fail` blocks ingest (fail-closed),
- verification result `warn` forces `policyTags.riskLevel = high` (risk status is displayed in UI but update proceeds automatically).

Conditional requirements:

- If template/analyzer resolution cannot produce install adapter or detection fields, admin must provide:
  - `manifest.installAdapter.command`
  - `manifest.installAdapter.arguments`
  - `manifest.installAdapter.expectedExitCodes`
  - `manifest.installAdapter.timeoutSeconds`
  - `manifest.detection.type`
  - `manifest.detection.path`
  - `manifest.detection.expectedVersion`
- If media type is not inferable, admin must provide `manifest.artifactType`.

Optional admin fields:

- any additional manifest metadata not in the required set
- explicit overrides for any prefillable field
- fields auto-filled by enrichment services, if available

Post-ingest stored manifest record must validate against JSON schema equivalent to:

```json
{
  "artifact": {
    "source": "/api/artifacts/nodejs/24.0.0",
    "type": "zip",
    "sizeBytes": 34567890,
    "digest": {
      "algorithm": "sha256",
      "value": "<immutable-content-hash>"
    },
    "signature": {
      "type": "authenticode-or-detached",
      "publisher": "CN=VendorOrInternalSigning",
      "verification": "pass|warn|fail"
    }
  },
  "originMetadata": {
    "source": "vendor-repo-or-internal-mirror",
    "publisher": "vendor-if-known",
    "ingestedBy": "operator-or-process-id",
    "ingestedAtUtc": "timestamp",
    "verificationResult": "pass|warn|fail"
  },
  "policyTags": {
    "riskLevel": "medium"
  }
}
```

### Config persistence contract

### Workload definition import schema

Workload definitions are imported as a global JSON file containing one or more workload definitions. PoC targets 2–3 workloads per file.

Sample workload definition file (`20260421-001-workloads.json`):

```json
{
  "workloads": [
    {
      "name": "DeltaV Observability Stack",
      "version": "2",
      "slug": "deltav-observability-stack",
      "packages": [
        "grafana-13.0.1-24542347077-windows-amd64",
        "loki-windows-amd64"
      ],
      "preUpgradeActions": [
        {
          "type": "backup",
          "description": "Backup Loki data directory before upgrade",
          "path": "C:\\ProgramData\\loki\\data"
        }
      ]
    },
    {
      "name": "DeltaV Core Runtime",
      "version": "1",
      "slug": "deltav-core-runtime",
      "packages": [
        "ej-installer-1.12.0"
      ]
    }
  ]
}
```

Field semantics:
- `packages`: list of manifest slugs (must exist in artifact catalog before publish)
- `preUpgradeActions`: optional declarative steps executed by the Agent before package installation/upgrade. Each action has a `type` and free-form metadata. PoC Phase 1 supports `backup` and `stop` types.
- `version`: simple integer referencing the workload revision (not semver).

### Config persistence contract (mutation safety)

Mutation paths requiring config changes must:

1. create pre-mutation snapshot (`configSnapshotId`),
2. execute deterministic migration chain only,
3. restore from snapshot on failure,
4. emit linked audit events for snapshot/migration/restore outcomes.

### Observability contract

Phase 1 default operator-visible stack:

- OTel Collector (ingest)
- Loki (log store)
- Grafana (query/view)

File-based export may remain as fallback, but required operational queries must work in Grafana/Loki for these fields:

- `workloadId`, `workloadRevision`, `runId`, `nodeId`, `packageId`, `stepId`, `sequence`, `reasonCode`.

---

## ADR consolidation matrix

All ADR content is represented in this PRD and linked tracker work.

| ADR | Decision | Phase 1 posture |
|---|---|---|
| ADR-001 | hybrid control plane (custom orchestrator + agent) | adopted |
| ADR-002 | agent-initiated connection with orchestrator assignment | adopted |
| ADR-003 | adapter strategy (MSI + EXE first) | adopted |
| ADR-004 | OpenTelemetry as baseline observability | adopted |
| ADR-005 | self-contained orchestrator/agent packaging strategy | adopted (orchestrator required in Phase 1) |
| ADR-006 | security baseline (artifact trust, RBAC, least privilege, audit) | adopted |
| ADR-007 | SignalR protocol canonicalization and sequencing | adopted |
| ADR-008 | orchestrator queue and agent buffering pattern | adopted |
| ADR-009 | persistent agent service model | adopted |
| ADR-010 | Browser-based bootstrap with signed agent.exe and enrollment-token authorization | adopted |
| ADR-011 | dry-run confidence framework | adopted |
| ADR-012 | enterprise bootstrap vs runtime orchestration boundary | adopted |
| ADR-013 | upgrade realism target phasing | adopted |
| ADR-014 | artifact metadata enrichment pipeline | adopted as Phase 1 capability with fallback chain |

---

## Functional requirements

| ID | Requirement | Priority | Linked AC IDs |
|---|---|---|---|
| FR-001 | [PoC Phase 1] Support workload definition/revision and orchestrator-triggered install/update/rollback/cancel/status workflows across target nodes | Must | AC-001, AC-002 |
| FR-002 | [PoC Phase 1] Enforce canonical runtime dispatch sequence and explicit lease ownership semantics | Must | AC-003 |
| FR-003 | [PoC Phase 1] Agent executes full local package-step pipeline; orchestrator owns run-level plan/policy/state only | Must | AC-004 |
| FR-004 | [PoC Phase 1] Bootstrap provisioning supports transactional rollback/cleanup on failure | Must | AC-005 |
| FR-005 | [PoC Phase 1] Typed installer adapter strategy supports MSI/EXE with normalized outcomes | Should | AC-006 |
| FR-006 | [PoC Phase 1] Mutation flow enforces config snapshot/migration/restore contract | Must | AC-007 |
| FR-007 | [PoC Phase 1] Runtime lifecycle APIs use `/api/workload-runs` only; `/api/jobs` mutation endpoints are non-runtime historical context | Must | AC-008 |
| FR-008 | [PoC Phase 1] Artifact ingest validates minimal required fields, injects deterministic defaults for prefillable fields, enforces conditional requirements when resolution fails, and persists schema-valid resolved manifests | Must | AC-009 |
| FR-009 | [PoC Phase 1] Orchestrator embedded UI provides clear operator ergonomics: centered opaque detail popups, terminal-style mini logs, workload/version/package visibility, no legacy fleet terminology in operator-facing labels, and local artifact-store management with drag-and-drop upload | Should | AC-107 |

---

## Non-functional requirements

| ID | Requirement | Category | Target/constraint | Linked AC IDs |
|---|---|---|---|---|
| NFR-001 | [PoC Phase 1] Delivery semantics are at-least-once with idempotent handlers and bounded stale policy | Reliability | TTL 90s, heartbeat 15s, stale after 3 misses, bounded stale timeout | AC-101 |
| NFR-002 | [PoC Phase 1] Security baseline enforces on-prem secret hygiene, artifact integrity, RBAC, token->mTLS identity | Security | no plaintext secrets, fail-closed trust checks, one-time token then cert identity | AC-102 |
| NFR-003 | [PoC Phase 1] Runtime telemetry is deterministic, queryable, and diagnosable at package-step level | Observability | required workload/run correlation fields queryable in Grafana/Loki | AC-103 |
| NFR-004 | [PoC Phase 1] Runtime automation surface is C# plus REST/CLI/manifests; scripts are provisioning-only | Operability | no script-driven runtime orchestration surface | AC-104 |
| NFR-005 | [PoC Phase 1] Orchestrator distribution is self-contained single executable with embedded UI | Deployment | no preinstalled .NET runtime or IIS required on clean host | AC-105 |

---

## Acceptance criteria

| ID | Linked req IDs | Testable statement | Validation method |
|---|---|---|---|
| AC-001 | FR-001 | Operator submits workload install/update runs through API/UI and observes terminal state on agent and orchestrator targets | Integration/E2E |
| AC-002 | FR-001 | Rollback and cancel transitions are auditable and reason-coded | Integration |
| AC-003 | FR-002 | Protocol includes assignment/lease/sequence; status ingest is idempotent; stale/out-of-order and same-key mismatch behavior enforced | Unit/Integration |
| AC-004 | FR-003 | Agent runs full local package-step pipeline; orchestrator remains run-level authority | Integration |
| AC-005 | FR-004 | Bootstrap failure executes reverse-order cleanup and token invalidation | Integration/Manual |
| AC-006 | FR-005 | MSI/EXE adapters execute through typed pipeline with normalized telemetry | Integration |
| AC-007 | FR-006 | Mutation failure restores from snapshot and emits linked audit evidence | Integration |
| AC-008 | FR-007 | Runtime lifecycle coverage uses `/api/workload-runs`; no runtime path depends on `/api/jobs` mutations | Integration |
| AC-009 | FR-008 | Artifact ingest injects deterministic defaults for prefillable fields, rejects missing minimal required or unresolved conditional fields, and persists schema-valid resolved manifests with field-source provenance | Integration |
| AC-101 | NFR-001 | Stale lease policy behaves correctly under disconnect/reconnect and chaos scenarios | Integration/Chaos |
| AC-102 | NFR-002 | Unsigned artifact blocked, unauthorized action denied, invalid cert reconnect denied, no plaintext secrets | Integration/Security |
| AC-103 | NFR-003 | Package-step telemetry and required workload correlation attributes are queryable in Grafana/Loki | Integration/Observability |
| AC-104 | NFR-004 | Runtime actions available through REST/CLI workload surfaces without script dependency | Integration/Manual |
| AC-105 | NFR-005 | Orchestrator executable runs on clean Windows host with API and UI available | Integration/Manual |
| AC-107 | FR-009 | Orchestrator UI uses centered popups for node/workload details and create flows, includes terminal-like mini logs in details, preserves workload revision/package visibility, removes legacy fleet wording from operator-facing labels, and exposes local artifact-store management with drag-drop + picker upload | Integration/Manual/UI regression |

---

## Security risk register (STRIDE summary)

| Threat ID | Component | Category | Description | Baseline mitigation |
|---|---|---|---|---|
| TH-001 | runtime hub | Spoofing | rogue node impersonation | one-time token + bound mTLS cert identity |
| TH-002 | artifact path | Tampering | artifact substitution/tamper | signature/hash verification fail-closed |
| TH-003 | API/audit | Repudiation | actor denies runtime action | actor+role+correlation IDs with tamper-evident audit chain |
| TH-004 | logs/telemetry | Info disclosure | secret/token leakage | redaction denylist + secret hygiene policy |
| TH-005 | queue/runtime channel | DoS | heartbeat/retry flood | rate limits, bounded retries, queue/connections bounds |
| TH-006 | adapter execution | Elevation of privilege | unsafe process invocation | allowlisted adapters/args + constrained spawn policy |
| TH-007A | binaries | Tampering | executable substitution attack | signature/publisher validation on startup/update |
| TH-007B | binaries | Elevation of privilege | downgrade to vulnerable signed build | version floor enforcement |

Mitigation mapping IDs retained for implementation tracking:

- `M-001` token->mTLS identity
- `M-002` artifact trust verification
- `M-003` RBAC plus audit integrity
- `M-004` sequence/idempotency enforcement
- `M-005` on-prem secret handling and no plaintext policy
- `M-006` binary trust and anti-downgrade controls

---

## Testing and verification policy

Testing principles:

- contract-first and behavior-focused,
- deterministic assertions over timing luck,
- failure-path coverage equal to happy-path coverage,
- production bugs become regression tests.

Required test layers:

- unit: run planning, sequencing, policy decisions, idempotency, adapter normalization,
- integration: workload API/persistence/runtime channel contracts,
- e2e: operator-critical submit-monitor-cancel/rollback flows,
- fault-injection: checksum mismatch, disconnect/reconnect, retry exhaustion, lease stale paths,
- compatibility: legacy migration behavior is documented and remains non-runtime.

Evidence policy:

- each AC must map to executable test evidence,
- each evidence artifact links to commit/build/run metadata,
- signoff is blocked until all required AC rows are closed.

## Testing decisions

### Testing philosophy

Tests assert external behavior only, not implementation details. A test is good when:

- It exercises a module through its public interface (REST endpoint, SignalR message, pipeline step interface, CLI command), not its internal state.
- It remains valid when the implementation is refactored, renamed, or reorganized.
- It produces the same result when run in any order or in isolation.
- Failure-path assertions are as specific as happy-path assertions (exact reason codes, exact state transitions, exact audit events).

Tests that reach into private fields, depend on test execution order, or assert on internal logging/output rather than observable contracts are considered fragile and should be rewritten.

### Modules under test

| Module | Test level | Rationale |
|---|---|---|
| Workload API contracts (`/api/workloads`, `/api/workload-runs`) | Integration | External REST behavior; Create, Get, Steps, Cancel must be verifiable end-to-end |
| Artifact ingest (`POST /api/artifacts`) | Integration | Validates multipart parsing, required/optional field resolution, schema validation, provenance persistence |
| Runtime message protocol (SignalR envelope) | Unit + Integration | Contract fidelity on wire; idempotent upsert and sequence conflict detection |
| Lease manager and stale policy | Unit + Chaos | Deterministic lease TTL/heartbeat/reassignment logic; chaos for disconnect/reconnect |
| Policy engine (risk evaluation, pre-upgrade actions) | Unit | Pure decision logic; risk-level elevation rules and pre-upgrade action enforcement |
| Agent execution pipeline (IInstallStep chain) | Unit + Integration | Each step tested via `IInstallStep` interface; pipeline ordering tested via integration |
| Manifest resolution chain (admin → template → analyzer → default) | Unit | Pure deterministic resolution; provenance tagging validated without HTTP |
| Config snapshot/migration/restore | Integration | Snapshot creation, deterministic migration, failure restore, and audit linkage |
| CLI command surface | Integration | REST-parity commands verified against real API |
| Artifact deprecation (`/api/jobs` → `410 Gone`) | Integration | Negative contract tests; no runtime path depends on legacy endpoints |
| Observability query contract | Integration | Loki/Grafana queryability for required correlation fields |
| Orchestrator UI (workload lifecycle, artifact store) | E2E/UI regression | Operator-critical flows: create workload, submit run, view timeline, upload artifact, verify metadata |
| Security baseline (RBAC, trust, secrets) | Integration + Security | Negative tests: unsigned artifact rejection, unauthorized action denial, invalid cert rejection, secret hygiene audit |
| Self-contained packaging | Integration/Manual | Clean-host launch without preinstalled runtime/IIS |

### Prior art in this codebase

- SignalR protocol contract tests exist in `tests/DeploymentPoC.Orchestrator.IntegrationTests` filtered by `SignalRProtocol`.
- Database shape tests exist via `DbContextShape` filter pattern.
- Agent pipeline tests exist in `tests/DeploymentPoC.Agent.Tests` filtered by `Pipeline`.
- Integration tests for the orchestrator exist in `tests/DeploymentPoC.Orchestrator.IntegrationTests` with named filter groups (`WorkloadsApi`, `WorkloadRunsApi`, `JobsDeprecation`, `ArtifactIngest`, `Lease`, `ConfigSnapshot`, `Otel`).
- Chaos tests exist in `tests/DeploymentPoC.Orchestrator.ChaosTests` filtered by `AssignedStale`.
- Frontend tests exist in `apps/orchestrator/web/src/**/*.test.tsx` with pnpm test runner.

---

## DevOps and deployment policy

Pipeline responsibilities [PoC Phase 1]:

`CI -> Publish -> PackagingValidation -> DeployOrchestrator -> Integration -> E2E`

Hard constraints:

- pipeline deploys orchestrator only,
- workstation runtime actions are triggered via orchestrator API/CLI,
- no direct workstation deployment from pipeline jobs,
- packaging validation must include clean-host launch (no .NET runtime or IIS preinstall).

On-prem/air-gap realism requirements:

- no public internet dependency for required release gates,
- internal mirrors/preloaded fixtures for artifacts,
- local or on-prem telemetry collectors and stores for observability checks.

---

## Traceability matrix

| Requirement | ADR linkage | Primary validation |
|---|---|---|
| FR-001 | ADR-001, ADR-008 | integration/e2e workload lifecycle suite |
| FR-002 | ADR-002, ADR-007 | sequence/idempotency unit+integration suite |
| FR-003 | ADR-009 | agent package-step integration suite |
| FR-004 | ADR-010 | bootstrap rollback integration/manual suite |
| FR-005 | ADR-003 | adapter normalization integration suite |
| FR-006 | ADR-013 | snapshot/migration/restore integration suite |
| FR-007 | ADR-007, ADR-012 | deprecation contract integration checks |
| FR-008 | ADR-014 | artifact ingest schema validation suite |
| FR-009 | ADR-001, ADR-012 | orchestrator embedded UI workflow and terminology regression suite |
| NFR-001 | ADR-008 | lease stale + chaos suite |
| NFR-002 | ADR-006, ADR-012 | security integration + negative tests |
| NFR-003 | ADR-004 | observability query contract integration suite |
| NFR-004 | ADR-012 | REST/CLI operability integration/manual checks |
| NFR-005 | ADR-005 | packaging validation + clean-host launch test |

---

## Definition of done (Phase 1)

PoC Phase 1 is complete only when all conditions are true:

1. `AC-001..AC-009`, `AC-101..AC-105`, `AC-107` each have linked evidence.
2. Security baseline controls are proven with negative tests.
3. Contract consistency across PRD, tracker, and storyboard is verified.
4. Packaging and deployment policy constraints are proven in CI evidence.
5. Deferred `[Hardening Phase 2]` items are documented but not treated as blockers.

---

## Deferred items [Hardening Phase 2]

- advanced key and certificate lifecycle operations
- expanded incident/forensics runbooks
- broader telemetry retention/indexing/governance
- rollout-ring automation and fleet-scale controls
- multi-orchestrator HA/DR behavior
- Linux agent implementation
- automatic package dependency solver and package removal policies

## Phase 2 expansions and considerations

### UI-based agent enrollment (device code flow)

Phase 1 enrollment is CLI-only: the operator downloads a signed `agent.exe` and runs it with `--enroll <token> --orchestrator-url <url>`. This supports headless containers and server environments.

Phase 2 may add a browser-driven enrollment flow that keeps the agent headless (consistent with ADR-0001) while removing the need to type tokens into a terminal:

1. Operator runs `agent.exe` with no enrollment flags.
2. Agent detects no config file, generates a random `sessionId`, and opens the system browser to `https://<orchestrator>/enroll?session=<sessionId>`.
3. Agent begins polling `GET /api/enrollment-sessions/{sessionId}/status` every 5 seconds.
4. On the orchestrator page, the user pastes the enrollment token and clicks authorize.
5. The orchestrator consumes the token, creates the node, and stores `nodeId` against the session.
6. The agent's next poll receives `{ status: "completed", nodeId: "...", orchestratorUrl: "..." }`.
7. Agent writes `agent.json` and starts the runtime automatically.

This follows the OAuth2 device-code pattern used by GitHub CLI, Azure CLI, and Tailscale. Required orchestrator endpoints:
- `POST /api/enrollment-sessions` — agent creates a session
- `GET /api/enrollment-sessions/{id}/status` — agent polls for completion
- `POST /api/enrollment-sessions/{id}/complete` — UI submits token

Additional Phase 2 concerns: session TTL/cleanup (e.g., 10 minutes), polling timeout, graceful fallback to `--enroll` CLI on Windows Server Core or air-gapped nodes where no browser is available.

---

## Further notes

### Phase 1 prerequisites

- A clean Windows host (no .NET runtime, no IIS) must be available for packaging validation.
- Internal signing material and enrollment token infrastructure must be configured before agent bootstrap can be tested.
- OTel Collector, Loki, and Grafana must be deployable locally or on-premises for observability validation.
- SQLite is the persistence baseline; no external database server dependency for Phase 1.

### Well-known risks

- **SQLite concurrency limits**: single-writer constraint may surface under high-frequency status ingest. Phase 1 scope assumes PoC-scale traffic (2-3 workloads, <10 agents). Hardening Phase 2 may require migration to a more capable store.
- **SignalR reconnection robustness**: agent reconnect must resume from `lastAcknowledgedSequence + 1` without state corruption. Extensive chaos testing is planned but reconnect coverage depends on realistic network interruption simulation.
- **MSI/EXE adapter edge cases**: installer return codes (e.g., 3010 reboot-pending) must map to normalized outcomes. Edge cases in real-world MSI/EXE behavior are known to be varied and may surface during integration testing.
- **Workload import validation**: unresolved manifest slug references must block publication. The 2-3 workload PoC size means the import path is tested at minimal scale.
- **Browser-based bootstrap air-gap**: the agent download flow assumes the target node can reach the Orchestrator URL. Air-gap scenarios where this is impossible are out of Phase 1 scope.

### Terminology enforcement

- "Workload Run" is the canonical runtime term. "Job" appears only in deprecation context (`/api/jobs` → `410 Gone`).
- "Node" or "Agent" is the canonical term for a registered target. "Fleet" must not appear in operator-facing UI labels.
- "Pre-Upgrade Action" is the canonical term for declarative pre-upgrade requirements. "Upgrade path" and "pre-condition" are avoided.
- "Risk Level" (`low|medium|high`) is the only manifest-level policy metadata in Phase 1. "Severity" and "priority" are avoided.

### Documentation set

Active documentation for Phase 1:
1. `docs/prd-phase1.md` — this document (canonical)
2. `docs/implementation-tracker-phase1.md` — execution tracker
3. `docs/orchestrator-control-plane/CONTEXT.md` — orchestrator bounded context
4. `docs/agent-runtime/CONTEXT.md` — agent bounded context
5. `docs/artifact-workload-catalog/CONTEXT.md` — artifact/workload bounded context
6. `docs/CONTEXT-MAP.md` — cross-context relationship map
7. `docs/distributed-installer/` — legacy historical snapshot (read-only)
8. `docs/adr/` — architecture decision records

### Known open questions

- Orchestrator self-update (staged-swap pattern) is documented in context files but not yet a Phase 1 implementation task. It may be added if packaging validation surfaces dependency on it.
- Vendor metadata fetch for artifact manifest enrichment is a Phase 2 capability. Phase 1 relies on admin input, policy template matching, and binary analysis.
- CLI command surface (`di`) is specified in context files but not yet broken into implementation tracker tasks beyond W6-03.

## Change control

- Any update that changes FR/NFR/AC semantics must update this PRD first.
- Tracker execution details may evolve, but cannot violate PRD constraints.
- Storyboard flow updates are expected in a separate user-led pass.
