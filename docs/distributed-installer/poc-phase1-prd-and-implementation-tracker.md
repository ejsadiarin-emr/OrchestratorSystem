# PoC Phase 1 PRD and Implementation Tracker

Date: 2026-04-17
Status: In progress
Source of truth: `poc-phase1-prd-final.md`
Companion flow doc: `storyboard-phase1-workload-aligned.md` (canonical storyboard)

## Purpose

This tracker is execution-only. It translates PRD requirements and acceptance criteria into dependency-ordered engineering work, verification gates, and evidence closure.

This document does not redefine policy. If policy language here conflicts with PRD language, the PRD wins.

## Scope guardrails (non-negotiable)

- PoC Phase 1 only. No Hardening Phase 2 implementation work.
- Workload-first runtime model is mandatory.
- `/api/jobs` mutation endpoints are deprecated immediately and must return `410 Gone`.
- All new runtime flows use `/api/workload-runs`.
- Orchestrator packaging is self-contained, single executable, embedded UI.
- Orchestrator launch on clean host requires no preinstalled .NET runtime or IIS.
- Runtime workstation operations remain API/UI/CLI-driven through orchestrator only.
- Direct workstation deployment from Azure DevOps pipeline is out of scope.
- Runtime artifact source is internal-only.
- Local artifact store management UI is first-class in orchestrator embedded UI.
- Artifact upload UX must support both drag-and-drop and file picker while preserving canonical `POST /api/artifacts` behavior.
- Phase 1 assumes single orchestrator.
- Workload revisions are immutable once published.
- PoC workload revision size target is 2-3 packages.
- Operator-facing UI terminology must avoid legacy "fleet" wording where equivalent node/workload terms exist.
- Canonical runtime sequence is fixed:
  `Connect -> Register/Authenticate -> AssignRun -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

## Status legend

- `Not Started`
- `In Progress`
- `Blocked`
- `Done`

## Legacy baseline (historical, completed)

These tasks are retained as historical completion and are not reopened.

| Task ID | Task | Status | AC IDs |
|---|---|---|---|
| L0-01 | Shared runtime contracts library (former P0-01) | Done | AC-003 |
| L0-02 | Initial PRD/tracker/storyboard alignment (former P0-02/P0-03) | Done | AC-001..AC-007, AC-101..AC-105 |
| L1-01 | SQLite canonical persistence baseline (former P1-01) | Done | AC-001, AC-002, AC-007, AC-101 |
| L1-02 | Baseline runtime API alignment (former P1-02) | Done | AC-001, AC-002, AC-104 |
| L1-03 | Artifact HTTP transport + range retrieval (former P1-03) | Done | AC-001, AC-006, AC-102 |

## Workload-first sprint board (dependency ordered)

| Task ID | Task | Sprint | Depends On | Owner | Status | AC IDs |
|---|---|---|---|---|---|---|
| W0-01 | Workload-first PRD/tracker realignment | S0 | - | TBD (Product/Arch) | Done | AC-001..AC-009, AC-101..AC-105 |
| W0-02 | Contract freeze and migration map (legacy jobs -> workload-runs) | S0 | W0-01 | TBD (Product/Arch/Backend) | Not Started | AC-001, AC-003, AC-008, AC-104 |
| W1-01 | SQLite workload domain schema (definition/revision/run/state) | S1 | W0-02 | TBD (Backend) | Not Started | AC-001, AC-002, AC-007 |
| W1-02 | Workload API contracts (`/api/workloads*`) | S1 | W1-01 | TBD (Backend) | Not Started | AC-001, AC-104 |
| W1-03 | Workload run APIs (`/api/workload-runs*`) | S1 | W1-02 | TBD (Backend) | Not Started | AC-001, AC-002 |
| W1-04 | `/api/jobs` immediate deprecation contract (`410 Gone`) | S1 | W1-03 | TBD (Backend) | Not Started | AC-008 |
| W1-05 | Artifact ingest required/optional split + schema validator | S1 | W1-02 | TBD (Backend/Frontend) | Not Started | AC-009, AC-006, AC-102 |
| W2-01 | Runtime contract updates (`AssignRun` payload model) | S2 | W1-03 | TBD (Contracts/Backend) | Not Started | AC-003 |
| W2-02 | Sequence/idempotency enforcement for run timeline ingest | S2 | W2-01 | TBD (Backend) | Not Started | AC-003, AC-101 |
| W2-03 | Lease manager + stale policy for workload runs | S2 | W2-01 | TBD (Backend) | Not Started | AC-101 |
| W2-04 | Policy engine (retry/idempotency/risk/approval) | S2 | W2-02, W2-03 | TBD (Backend/Agent) | Not Started | AC-002, AC-006, AC-007, AC-101 |
| W3-01 | Windows agent service scaffold + runtime loop hardening | S2 | W2-01 | TBD (Agent) | Not Started | AC-004 |
| W3-02 | Bootstrap token -> mTLS steady-state auth flow | S2 | W3-01 | TBD (Security/Agent) | Not Started | AC-005, AC-102 |
| W3-03 | Agent workload pipeline (ordered package-step execution) | S2 | W3-01, W1-05 | TBD (Agent) | Not Started | AC-004, AC-006 |
| W3-04 | Node workload state persistence/reporting | S2 | W3-03 | TBD (Agent/Backend) | Not Started | AC-001, AC-002 |
| W4-01 | Config snapshot/migration/restore linkage for mutation paths | S3 | W3-03 | TBD (Agent/Backend) | Not Started | AC-007 |
| W5-01 | Security baseline controls (RBAC/trust/audit/secrets) | S3 | W2-04, W3-02 | TBD (Security/Backend) | Not Started | AC-102, AC-002 |
| W5-02 | Observability stack MVP (OTel Collector + Loki + Grafana) | S3 | W2-02 | TBD (Backend/DevOps) | Not Started | AC-103 |
| W6-01 | Orchestrator UI workload screens + run timeline | S3 | W1-03, W2-01 | TBD (Frontend) | Not Started | AC-001, AC-002, AC-103, AC-105 |
| W6-01A | Orchestrator UI interaction refresh (centered popups, terminal-like logs, info-hint stability, terminology pass) | S3 | W6-01 | TBD (Frontend) | Not Started | AC-107 |
| W6-01B | Orchestrator local artifact-store management page with drag-drop upload and artifact/version visibility | S3 | W1-05, W6-01 | TBD (Frontend/Backend) | Not Started | AC-009, AC-107 |
| W6-02 | Orchestrator UI deprecation UX for `/api/jobs` | S3 | W1-04 | TBD (Frontend) | Not Started | AC-008 |
| W6-03 | Agent UI minimal workload status/update surface | S3 | W3-04 | TBD (Frontend/Agent) | Not Started | AC-001, AC-103 |
| W6-04 | CLI workload command surface | S3 | W1-03 | TBD (Platform) | Not Started | AC-104, AC-001, AC-002 |
| W7-01 | Self-contained orchestrator packaging validation | S4 | W6-01 | TBD (Platform/DevOps) | Not Started | AC-105 |
| W7-02 | CI/CD policy gates and orchestrator-only deploy boundary | S4 | W7-01, W6-04 | TBD (DevOps) | Not Started | AC-104, AC-105 |
| W8-01 | Integration/E2E/chaos acceptance suite and evidence closure | S4 | W1-01..W7-02 | TBD (QA/All) | Not Started | AC-001..AC-009, AC-101..AC-105 |

## MVP execution slices (do first)

### Slice A - Domain + API foundation

- `W0-02` -> `W1-01` -> `W1-02` -> `W1-03` -> `W1-04` -> `W1-05`

### W0-02 - Contract freeze and migration map (legacy jobs -> workload-runs)

- Owner: `TBD (Product/Arch/Backend)`
- Status: `Not Started`
- Objective: freeze canonical naming/payload contracts across docs and implementation before W1 changes.
- Scope:
  - runtime protocol names (`AssignRun`, `runId`, `workloadId`, `workloadRevision`) as canonical,
  - endpoint migration map (`/api/jobs*` deprecated -> `/api/workload-runs*`),
  - terminology map (`job` legacy term vs `workload-run` canonical term).
- Output artifact:
  - `docs/distributed-installer/contract-freeze-phase1.md` (new)
- Verification commands:
  - `rg "AssignJob|jobId|/api/jobs" docs/distributed-installer -n`
  - `rg "AssignRun|runId|/api/workload-runs" docs/distributed-installer -n`
- Acceptance links: AC-001, AC-003, AC-008, AC-104
- Checklist:
  - [ ] Canonical runtime sequence and message naming are explicitly frozen.
  - [ ] All active doc examples use workload-run endpoint and identifier conventions.
  - [ ] Deprecation payload contract for `/api/jobs` is cross-referenced where needed.
  - [ ] Legacy-to-canonical field map is documented for implementation and tests.

### Slice B - Deterministic runtime behavior

- `W2-01` -> `W2-02` -> `W2-03` -> `W2-04`

### Slice C - Agent execution + rollback safety

- `W3-01` -> `W3-02` -> `W3-03` -> `W3-04` -> `W4-01`

### Slice D - Operator visibility and closure

- `W5-02` -> `W6-01` -> `W6-01A` -> `W6-01B` -> `W6-02` -> `W6-03` -> `W6-04` -> `W7-01` -> `W7-02` -> `W8-01`

## Task details checklist

### W1-01 - SQLite workload domain schema

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: add first-class workload entities and indexes in SQLite.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Data/InstallerDbContext.cs`
  - `src/DeploymentPoC.Orchestrator/Data/Entities/WorkloadDefinitionEntity.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Data/Entities/WorkloadRevisionEntity.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Data/Entities/WorkloadPackageEntity.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Data/Entities/WorkloadRunEntity.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Data/Entities/NodeWorkloadStateEntity.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Migrations/*`
- Verification commands:
  - `dotnet ef migrations add WorkloadDomain --project src/DeploymentPoC.Orchestrator`
  - `dotnet ef database update --project src/DeploymentPoC.Orchestrator`
  - `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter DbContextShape`
- Acceptance links: AC-001, AC-002, AC-007
- Checklist:
  - [ ] Workload revision immutability is enforced at persistence boundary.
  - [ ] Unique active-run guard exists for `(nodeId, workloadId)`.
  - [ ] Node workload state stores current applied revision and per-package status.

### W1-02 - Workload definition/revision APIs

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: expose `/api/workloads` create/list/detail/revision/publish endpoints.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Controllers/WorkloadsController.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Contracts/Api/Workloads/*` (new)
  - `src/DeploymentPoC.Orchestrator/Program.cs`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter WorkloadsApi`
- Acceptance links: AC-001, AC-104
- Checklist:
  - [ ] Revision creation enforces 2-3 package entries for PoC.
  - [ ] Published revision is immutable.
  - [ ] Invalid revision payload yields deterministic validation errors.

### W1-03 - Workload run APIs

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: expose `/api/workload-runs` create/get/steps/cancel with idempotency.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Controllers/WorkloadRunsController.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Contracts/Api/WorkloadRuns/*` (new)
  - `src/DeploymentPoC.Orchestrator/Data/Entities/WorkloadRunEntity.cs`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter WorkloadRunsApi`
- Acceptance links: AC-001, AC-002
- Checklist:
  - [ ] Create run snapshots exact workload revision content.
  - [ ] Update mode computes changed-package execution plan deterministically.
  - [ ] Step timeline endpoint exposes package index and package id.

### W1-04 - `/api/jobs` deprecation contract

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: return `410 Gone` with mandatory payload for deprecated job mutations.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Controllers/JobsController.cs`
  - `src/DeploymentPoC.Orchestrator/Contracts/Api/DeprecatedEndpointResponse.cs` (new)
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter JobsDeprecation`
- Acceptance links: AC-008
- Checklist:
  - [ ] `POST /api/jobs` returns `410` with replacement path.
  - [ ] `POST /api/jobs/{jobId}/cancel` returns `410` with replacement path.
  - [ ] Deprecated endpoint audit event emitted.

### W1-05 - Artifact ingest required/optional split + schema validator

- Owner: `TBD (Backend/Frontend)`
- Status: `Not Started`
- Objective: enforce minimal required admin fields, deterministic default injection, conditional field requirements when resolution fails, and persisted resolved-manifest schema checks.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Controllers/ArtifactIngestController.cs` (new or extend existing)
  - `src/DeploymentPoC.Orchestrator/Services/ManifestSchemaValidator.cs` (new)
  - `src/DeploymentPoC.Orchestrator/Services/PolicyTemplateService.cs`
  - `web/src/pages/Install.tsx`
  - `web/src/services/api.ts`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ArtifactIngest`
  - `pnpm --dir web test -- --grep ingest`
- Acceptance links: AC-009, AC-006, AC-102
- Checklist:
  - [ ] Minimal required admin fields enforced (`packageId`, `version`, `channel`, and `artifactType` unless inferable).
  - [ ] Deterministic resolution chain implemented (`admin -> template -> analyzer -> default`).
  - [ ] Resolved manifest persists per-field source provenance (`admin|template|analyzer|default`).
  - [ ] Conditional field escalation enforced when install adapter/detection cannot be resolved.
  - [ ] Missing minimal/conditional required fields fail with field-level validation errors.
  - [ ] Signature/hash verification `fail` blocks ingest; `warn` elevates riskLevel to `high` (status displayed in UI but update proceeds automatically).
  - [ ] Stored resolved manifest validates against schema-equivalent structure.
  - [ ] Optional/admin override fields are accepted without blocking ingest.
  - [ ] Orchestrator `/install` (artifact store management) supports drag-drop and file picker upload paths through the same ingest endpoint.
  - [ ] Artifact list/detail data required by workload revision authoring and operator drilldown are exposed to UI consumers.

### W6-01A - Orchestrator UI interaction refresh

- Owner: `TBD (Frontend)`
- Status: `Not Started`
- Objective: apply Phase 1 interaction standards across orchestrator UI without changing runtime policy.
- Target modules:
  - `apps/orchestrator/web/src/pages/Dashboard.tsx`
  - `apps/orchestrator/web/src/pages/dashboard/InfoHint.tsx`
  - `apps/orchestrator/web/src/pages/Workloads.tsx`
  - `apps/orchestrator/web/src/pages/WorkloadRuns.tsx`
  - `apps/orchestrator/web/src/components/layout/Layout.tsx`
  - `apps/orchestrator/web/src/pages/*.test.tsx`
- Verification commands:
  - `pnpm --dir apps/orchestrator/web test -- --runInBand`
  - `pnpm --dir apps/orchestrator/web build`
- Acceptance links: AC-107
- Checklist:
  - [ ] Node/workload detail interactions use centered opaque popups instead of right-side drawer.
  - [ ] Workload and workload-run create flows launch from explicit action buttons into centered popup flows.
  - [ ] Popup mini logs use terminal-like visual treatment and preserve severity visibility.
  - [ ] Risk/reason info hint interactions no longer auto-open unexpectedly during row/detail interactions.
  - [ ] Operator-facing UI copy removes legacy "fleet" labels where node/workload wording is available.

### W6-01B - Local artifact-store management page

- Owner: `TBD (Frontend/Backend)`
- Status: `Not Started`
- Objective: make `/install` a first-class local artifact-store management page aligned with workload-first operations.
- Target modules:
  - `apps/orchestrator/web/src/pages/Install.tsx`
  - `apps/orchestrator/web/src/services/api.ts`
  - `apps/orchestrator/web/src/types.ts`
  - `apps/orchestrator/web/src/pages/Install.test.tsx`
- Verification commands:
  - `pnpm --dir apps/orchestrator/web test -- --runInBand`
  - `pnpm --dir apps/orchestrator/web build`
- Acceptance links: AC-009, AC-107
- Checklist:
  - [ ] `/install` presents artifact-store inventory with version/channel/digest metadata suitable for workload authoring.
  - [ ] Upload supports drag-drop and file picker and maps to canonical multipart ingest request.
  - [ ] Upload success/failure states are explicit and auditable in UI feedback.
  - [ ] Artifact detail visibility supports operator verification before workload revision creation.

### W2-01 - Runtime contract updates (`AssignRun`)

- Owner: `TBD (Contracts/Backend)`
- Status: `Not Started`
- Objective: standardize run-centric message payload shape with workload metadata.
- Target modules:
  - `src/DeploymentPoC.Contracts/Runtime/MessageEnvelope.cs`
  - `src/DeploymentPoC.Contracts/Runtime/MessageTypes.cs`
  - `src/DeploymentPoC.Contracts/Runtime/RunPayloads/*` (new)
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Contracts.Tests`
- Acceptance links: AC-003

### W2-02 - Sequence/idempotency enforcement

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: enforce ingest/upsert and replay rules for run step statuses.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Runtime/StepStatusIngestService.cs`
  - `src/DeploymentPoC.Orchestrator/Runtime/SequenceConflictAuditService.cs`
  - `src/DeploymentPoC.Orchestrator/Hubs/AgentRuntimeHub.cs`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter Sequence`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter SignalRProtocol`
- Acceptance links: AC-003, AC-101

### W2-03 - Lease manager + stale handling

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: preserve stale handling defaults for workload-run assignment leases.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Runtime/LeaseManager.cs`
  - `src/DeploymentPoC.Orchestrator/Runtime/LeaseTimeoutWorker.cs`
  - `src/DeploymentPoC.Orchestrator/Data/Entities/AssignmentLeaseEntity.cs`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Lease`
  - `dotnet test tests/DeploymentPoC.Orchestrator.ChaosTests --filter AssignedStale`
- Acceptance links: AC-101

### W2-04 - Policy engine

- Owner: `TBD (Backend/Agent)`
- Status: `Not Started`
- Objective: enforce retry/idempotency/risk detection and status display per package-step. Update workflow is automatic: pre-check detects risk → status reflected in orchestrator UI → proceed via pre-defined upgrade paths. No manual approval step.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Runtime/PolicyEvaluationService.cs`
  - `src/DeploymentPoC.Agent/Pipeline/PipelineExecutor.cs`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter Policy`
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Policy`
- Acceptance links: AC-002, AC-006, AC-007, AC-101

### W3-03 - Agent workload pipeline (ordered packages)

- Owner: `TBD (Agent)`
- Status: `Not Started`
- Objective: execute workload package steps in revision order with deterministic checkpoints.
- Target modules:
  - `src/DeploymentPoC.Agent/Pipeline/*`
  - `src/DeploymentPoC.Agent/Steps/AcquireArtifact.cs`
  - `src/DeploymentPoC.Agent/Steps/InstallOrUpgrade.cs`
  - `src/DeploymentPoC.Agent/Steps/PostInstallVerify.cs`
  - `src/DeploymentPoC.Agent/Steps/EmitFinalization.cs`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Pipeline`
  - `dotnet test tests/DeploymentPoC.Agent.IntegrationTests --filter Workload`
- Acceptance links: AC-004, AC-006
- Checklist:
  - [ ] Install mode runs all packages in order.
  - [ ] Update mode runs only changed packages in same order.
  - [ ] Node workload revision updates only after full package-step success.

### W4-01 - Config snapshot/migration/restore

- Owner: `TBD (Agent/Backend)`
- Status: `Not Started`
- Objective: enforce restore-on-failure with audit linkage for mutation paths.
- Target modules:
  - `src/DeploymentPoC.Agent/Config/*`
  - `src/DeploymentPoC.Orchestrator/Services/ConfigAuditService.cs`
  - `src/DeploymentPoC.Orchestrator/Data/Entities/ConfigSnapshotEntity.cs`
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Migration`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ConfigSnapshot`
- Acceptance links: AC-007

### W5-02 - Observability stack MVP

- Owner: `TBD (Backend/DevOps)`
- Status: `Not Started`
- Objective: make workload run telemetry queryable in Grafana/Loki via OTel collector.
- Target modules:
  - `src/DeploymentPoC.Orchestrator/Observability/*`
  - `deploy/observability/docker-compose.yml` (new)
  - `deploy/observability/otel-collector.yaml` (new)
  - `deploy/observability/loki-config.yaml` (new)
  - `docs/distributed-installer/diagrams/*` (optional update)
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Otel`
  - `pnpm --dir web run test:e2e -- --grep observability`
- Acceptance links: AC-103
- Checklist:
  - [ ] Queries by `workloadId` and `runId` return package-step records.
  - [ ] Queries by `nodeId` show active and failed runs.
  - [ ] File export remains available as fallback.

## Phase verification gates

| Phase | Gate command(s) | Exit criterion | Owner | Status |
|---|---|---|---|---|
| Slice A | `dotnet build DeploymentPoC.sln` + workload API tests | workload domain + deprecation + ingest validation pass | TBD | Not Started |
| Slice B | protocol + lease + policy suites | deterministic run sequencing/idempotency/stale behavior pass | TBD | Not Started |
| Slice C | agent pipeline + migration tests | ordered package execution + restore behavior pass | TBD | Not Started |
| Slice D | web + cli + observability tests | operator visibility and runtime operations pass | TBD | Not Started |
| Packaging | self-contained publish + clean-host launch | AC-105 satisfied | TBD | Not Started |
| Final | full tests + evidence review | AC-001..AC-009, AC-101..AC-107 closed with evidence | TBD | Not Started |

## AC evidence tracker

| AC ID | Evidence artifact/path | Owner | Status |
|---|---|---|---|
| AC-001 | TBD | TBD | Not Started |
| AC-002 | TBD | TBD | Not Started |
| AC-003 | `src/DeploymentPoC.Contracts/*`, SignalR suites | TBD | In Progress |
| AC-004 | TBD | TBD | Not Started |
| AC-005 | TBD | TBD | Not Started |
| AC-006 | TBD | TBD | Not Started |
| AC-007 | TBD | TBD | Not Started |
| AC-008 | `/api/jobs` deprecation integration tests | TBD | Not Started |
| AC-009 | artifact ingest schema validation tests | TBD | Not Started |
| AC-101 | TBD | TBD | Not Started |
| AC-102 | TBD | TBD | Not Started |
| AC-103 | Loki/Grafana query evidence | TBD | Not Started |
| AC-104 | REST/CLI workload command evidence | TBD | Not Started |
| AC-105 | clean-host orchestrator launch evidence | TBD | Not Started |
| AC-106 | agent local UI workflow evidence | TBD | Not Started |
| AC-107 | orchestrator UI interaction + artifact-store UX regression evidence | TBD | Not Started |

## Change control notes

- One commit per completed task section in dependency order.
- If a task is split, use explicit suffixes and maintain AC mapping.
- Hardening Phase 2 controls can be logged as backlog but must not block Phase 1 closure.
- Storyboard updates are intentionally excluded in this pass per user instruction.
