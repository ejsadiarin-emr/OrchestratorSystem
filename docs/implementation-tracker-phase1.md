# PoC Phase 1 PRD and Implementation Tracker

Date: 2026-04-17
Status: In progress
Source of truth: `docs/prd-phase1.md`
Companion flow doc: `docs/distributed-installer/storyboard-phase1-workload-aligned.md` (legacy storyboard retained for reference)

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

| Task ID | Task                                                          | Status | AC IDs                         |
| ------- | ------------------------------------------------------------- | ------ | ------------------------------ |
| L0-01   | Shared runtime contracts library (former P0-01)               | Done   | AC-003                         |
| L0-02   | Initial PRD/tracker/storyboard alignment (former P0-02/P0-03) | Done   | AC-001..AC-007, AC-101..AC-105 |
| L1-01   | SQLite canonical persistence baseline (former P1-01)          | Done   | AC-001, AC-002, AC-007, AC-101 |
| L1-02   | Baseline runtime API alignment (former P1-02)                 | Done   | AC-001, AC-002, AC-104         |
| L1-03   | Artifact HTTP transport + range retrieval (former P1-03)      | Done   | AC-001, AC-006, AC-102         |

## Workload-first sprint board (dependency ordered)

| Task ID | Task                                                                                                             | Sprint | Depends On   | Owner                      | Status      | AC IDs                                 |
| ------- | ---------------------------------------------------------------------------------------------------------------- | ------ | ------------ | -------------------------- | ----------- | -------------------------------------- |
| W0-01   | Workload-first PRD/tracker realignment                                                                           | S0     | -            | TBD (Product/Arch)         | Done        | AC-001..AC-009, AC-101..AC-105         |
| W0-02   | Contract freeze and migration map (legacy jobs -> workload-runs)                                                 | S0     | W0-01        | TBD (Product/Arch/Backend) | Not Started | AC-001, AC-003, AC-008, AC-104         |
| W1-01   | SQLite workload domain schema (definition/revision/run/state)                                                    | S1     | W0-02        | TBD (Backend)              | Done        | AC-001, AC-002, AC-007                 |
| W1-02   | Workload API contracts (`/api/workloads*`)                                                                       | S1     | W1-01        | TBD (Backend)              | Done        | AC-001, AC-104                         |
| W1-03   | Workload run APIs (`/api/workload-runs*`)                                                                        | S1     | W1-02        | TBD (Backend)              | Done        | AC-001, AC-002                         |
| W1-04   | ~~`/api/jobs` immediate deprecation contract (`410 Gone`)~~ (skipped — fresh project, no legacy users)            | S1     | W1-03        | TBD (Backend)              | Skipped     | AC-008                                 |
| W1-05   | Artifact ingest required/optional split + schema validator                                                       | S1     | W1-02        | TBD (Backend/Frontend)     | Done        | AC-009, AC-006, AC-102                 |
| W2-01   | Runtime contract updates (`AssignRun` payload model)                                                             | S2     | W1-03        | TBD (Contracts/Backend)    | Done        | AC-003                                 |
| W2-02   | Sequence/idempotency enforcement for run timeline ingest                                                         | S2     | W2-01        | TBD (Backend)              | Not Started | AC-003, AC-101                         |
| W2-03   | Lease manager + stale policy for workload runs                                                                   | S2     | W2-01        | TBD (Backend)              | Not Started | AC-101                                 |
| W2-04a  | Policy engine - risk detection in orchestrator                                                                    | S2     | W2-02, W2-03 | TBD (Backend)              | Done        | AC-002, AC-006, AC-101                  |
| W2-04b  | Policy engine - preUpgradeActions enforcement in agent pipeline                                                    | S2     | W2-04a       | TBD (Agent)                | Not Started | AC-007, AC-006                           |
| W3-01   | Windows agent service scaffold + runtime loop hardening                                                           | S2     | W2-01        | TBD (Agent)                | Done        | AC-004                                 |
| W3-02   | Bootstrap token -> mTLS steady-state auth flow                                                                   | S2     | W3-01        | TBD (Security/Agent)       | Not Started | AC-005, AC-102                         |
| W3-02a  | Enrollment token generation + agent download endpoint                                                            | S2     | W3-01        | TBD (Backend/Frontend)      | Done        | AC-005                                 |
| W3-02b  | Agent CLI enrollment (`--enroll`, `--reset-enrollment`) + config persistence                                     | S2     | W3-02a       | TBD (Agent)                | Not Started | AC-005                                 |
| W3-03   | Agent workload pipeline (ordered package-step execution)                                                         | S2     | W3-01, W1-05 | TBD (Agent)                | Done        | AC-004, AC-006                         |
| W3-04   | Node workload state persistence/reporting                                                                        | S2     | W3-03        | TBD (Agent/Backend)        | Done        | AC-001, AC-002                         |
| W4-01   | Config snapshot/migration/restore linkage for mutation paths                                                     | S3     | W3-03        | TBD (Agent/Backend)        | Not Started | AC-007                                 |
| W5-01a  | Security baseline - RBAC + audit integrity                                                                       | S3     | W2-04a, W3-02| TBD (Security/Backend)     | Not Started | AC-002, AC-102                           |
| W5-01b  | Security baseline - trust verification + secret hygiene                                                          | S3     | W5-01a       | TBD (Security/Agent)       | Not Started | AC-102                                   |
| W5-02   | Observability stack MVP (OTel Collector + Loki + Grafana)                                                        | S3     | W2-02        | TBD (Backend/DevOps)       | Not Started | AC-103                                 |
| W6-01   | Orchestrator UI workload CRUD + run submission                                                                   | S3     | W1-03, W2-01 | TBD (Frontend)             | Done        | AC-001, AC-002                           |
| W6-01a  | **FIXED** Artifact→Package bridge (creates PackageEntity on ingest, enables revision creation)      | -      | W6-01        | Claude (2026-04-23)       | Done        | API integration                           |
| W6-01b  | Orchestrator UI run timeline + node visibility                                                                    | S3     | W6-01        | TBD (Frontend)             | Done        | AC-103, AC-105                           |
| W6-01A  | Orchestrator UI interaction refresh (centered popups, terminal-like logs, info-hint stability, terminology pass) | S3     | W6-01        | TBD (Frontend)             | Not Started | AC-107                                 |
| W6-01B  | Orchestrator local artifact-store management page with drag-drop upload and artifact/version visibility          | S3     | W1-05, W6-01 | TBD (Frontend/Backend)     | Done        | AC-009, AC-107                         |
| W6-02   | Orchestrator UI deprecation UX for `/api/jobs`                                                                   | S3     | W1-04        | TBD (Frontend)             | Not Started | AC-008                                 |
| W6-03   | CLI workload command surface                                                                                     | S3     | W1-03        | TBD (Platform)             | Not Started | AC-104, AC-001, AC-002                 |
| W7-01   | Self-contained orchestrator packaging validation                                                                 | S4     | W6-01        | TBD (Platform/DevOps)      | Not Started | AC-105                                 |
| W7-02   | CI/CD policy gates and orchestrator-only deploy boundary                                                         | S4     | W7-01, W6-03 | TBD (DevOps)               | Not Started | AC-104, AC-105                         |
| W8-01a  | Integration/E2E suite - lifecycle ACs (001-009)                                                                  | S4     | W1-01..W7-02 | TBD (QA/All)               | Not Started | AC-001..AC-009                           |
| W8-01b  | Integration/E2E/chaos suite - NFR ACs (101-105, 107)                                                            | S4     | W8-01a       | TBD (QA/All)               | Not Started | AC-101..AC-105, AC-107                   |
| W8-02a  | Testcontainers agent enrollment integration tests                                                                | S4     | W3-02b, W3-04 | TBD (QA/Backend)          | Not Started | AC-005                                 |

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

- `W2-01` -> `W2-02` -> `W2-03` -> `W2-04a` -> `W2-04b`

### Slice C - Agent execution + rollback safety

- `W3-01` -> `W3-02` -> `W3-02a` -> `W3-03` -> `W3-04` -> `W4-01`

### Slice D - Operator visibility and closure

- `W5-01a` -> `W5-01b` -> `W5-02` -> `W6-01` -> `W6-01b` -> `W6-01A` -> `W6-01B` -> `W6-02` -> `W6-03` -> `W7-01` -> `W7-02` -> `W8-01a` -> `W8-01b`

## Task details checklist

### W1-01 - SQLite workload domain schema

- Owner: `TBD (Backend)`
- Status: `Done`
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
- Status: `Done`
- Objective: expose `/api/workloads` create/list/detail/revision/publish endpoints AND workload definition import from global JSON file.
- Target modules:
    - `src/DeploymentPoC.Orchestrator/Controllers/WorkloadsController.cs` (new)
    - `src/DeploymentPoC.Orchestrator/Contracts/Api/Workloads/*` (new)
    - `src/DeploymentPoC.Orchestrator/Services/WorkloadImportService.cs` (new)
    - `src/DeploymentPoC.Orchestrator/Program.cs`
- Verification commands:
    - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter WorkloadsApi`
- Acceptance links: AC-001, AC-104
- Checklist:
    - [ ] Revision creation enforces 2-3 package entries for PoC.
    - [ ] Published revision is immutable.
    - [ ] Invalid revision payload yields deterministic validation errors.
    - [ ] **Global JSON import** accepts a file containing 2-3 workload definitions with packages referencing artifact catalog slugs.
    - [ ] Import validates that referenced package slugs exist in the artifact catalog before creating draft definitions.
    - [ ] **Demo: UI provides drag-drop or file-picker to import workload definition JSON file.**

### W1-03 - Workload run APIs

- Owner: `TBD (Backend)`
- Status: `Done`
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
- Status: `Done`
- Objective: enforce minimal required admin fields, deterministic default injection, conditional field requirements when resolution fails, and persisted resolved-manifest schema checks.
- Target modules:
    - `apps/orchestrator/backend/Controllers/ArtifactsController.cs` (existing, extended)
    - `apps/orchestrator/backend/Services/ArtifactIngestService.cs`
    - `apps/orchestrator/web/src/pages/Install.tsx`
    - `apps/orchestrator/web/src/services/api.ts`
- Verification commands:
    - `dotnet test tests/orchestrator/integration --filter ArtifactIngest`
    - `pnpm --dir apps/orchestrator/web test -- --grep ingest`
- Acceptance links: AC-009, AC-006, AC-102
- Checklist:
    - [x] Minimal required admin fields enforced (`packageId`, `version`, `channel`, and `artifactType` unless inferable).
    - [x] Deterministic resolution chain implemented (`admin -> template -> analyzer -> default`).
    - [x] Resolved manifest persists per-field source provenance (`admin|template|analyzer|default`).
    - [x] Conditional field escalation enforced when install adapter/detection cannot be resolved.
    - [x] Missing minimal/conditional required fields fail with field-level validation errors.
    - [x] Signature/hash verification `fail` blocks ingest; `warn` elevates riskLevel to `high` (status displayed in UI but update proceeds automatically).
    - [x] Stored resolved manifest validates against schema-equivalent structure.
    - [x] Optional/admin override fields are accepted without blocking ingest.
    - [x] Orchestrator `/install` (artifact store management) supports drag-drop and file picker upload paths through the same ingest endpoint.
    - [x] Artifact list/detail data required by workload revision authoring and operator drilldown are exposed to UI consumers.

### W6-01 - Orchestrator UI workload CRUD + run submission

- Owner: `TBD (Frontend)`
- Status: `Done`
- Objective: implement workload definition CRUD, revision creation/publish, and workload run submission in the orchestrator embedded UI. This is the PRIMARY demo goal surface.
- Target modules:
    - `apps/orchestrator/web/src/pages/Workloads.tsx`
    - `apps/orchestrator/web/src/pages/WorkloadRuns.tsx`
    - `apps/orchestrator/web/src/services/api.ts`
    - `apps/orchestrator/web/src/types.ts`
    - `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`
- Verification commands:
    - `pnpm --dir apps/orchestrator/web test -- --runInBand` (48 passed)
    - `pnpm --dir apps/orchestrator/web build` (passes)
- Acceptance links: AC-001, AC-002
- Evidence:
    - Backend `GET /api/workload-runs` list endpoint with optional status filter added.
    - Frontend `api.ts` fully wired to real backend APIs for workloads, revisions, and runs.
    - `WorkloadRuns.tsx` updated to use `revisionId` instead of version string for run creation.
    - All 48 frontend tests pass after fixing mocks to match real API contracts.
- Checklist:
    - [x] Workload definition list/create/detail screens are functional.
    - [x] Workload revision create and publish flow works.
    - [x] Workload run submission (install/update/cancel) works from UI.
    - [ ] Workload definition import from JSON file works via drag-drop or file picker. (deferred to W6-01B)

### W6-01b - Orchestrator UI run timeline + node visibility

- Owner: `TBD (Frontend)`
- Status: `Not Started`
- Depends on: W6-01
- Objective: add live run timeline with package-step status and node workload state visibility.
- Target modules:
    - `apps/orchestrator/web/src/pages/WorkloadRuns.tsx`
    - `apps/orchestrator/web/src/pages/Dashboard.tsx`
    - `apps/orchestrator/web/src/services/api.ts`
- Verification commands:
    - `pnpm --dir apps/orchestrator/web test -- --runInBand`
- Acceptance links: AC-103, AC-105
- Checklist:
    - [ ] Live package-step timeline is visible on run detail.
    - [ ] Node workload state shows active revision per workload.
    - [ ] Run status reflects terminal outcomes and reason codes. - Orchestrator UI interaction refresh

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
- Status: `Done`
- Objective: standardize run-centric message payload shape with workload metadata.
- Target modules:
    - `shared/contracts/Runtime/MessageEnvelope.cs`
    - `shared/contracts/Runtime/MessageTypes.cs`
    - `shared/contracts/Runtime/RunPayloads/AssignRunPayload.cs` (new)
    - `shared/contracts/Runtime/RunPayloads/PackageAssignment.cs` (new)
    - `shared/contracts/Runtime/RunPayloads/InstallAdapterConfig.cs` (new)
    - `shared/contracts/Runtime/RunPayloads/DetectionConfig.cs` (new)
    - `apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj` (added Contracts ref)
    - `apps/agent/backend/DeploymentPoC.Agent.csproj` (added Contracts ref)
- Verification commands:
    - `dotnet test tests/contracts/DeploymentPoC.Contracts.Tests.csproj`
- Acceptance links: AC-003
- Checklist:
    - [x] `AssignRun` message type added to `MessageTypes`.
    - [x] `MessageEnvelope` uses `RunId` instead of legacy `JobId`.
    - [x] `AssignRunPayload` contains run identity, workload metadata, revision info, mode, node id.
    - [x] `PackageAssignment` includes ordered package index, artifact slug, version, channel.
    - [x] `InstallAdapterConfig` carries type, command, arguments, expected exit codes, timeout.
    - [x] `DetectionConfig` carries type, path, expected version.
    - [x] `PreUpgradeActions` list supported on payload.
    - [x] Both orchestrator and agent projects reference shared contracts.
    - [x] Contract tests verify payload shape and defaults.

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

### W2-04a - Policy engine - risk detection in orchestrator

- Owner: `TBD (Backend)`
- Status: `Done`
- Objective: implement risk-level evaluation for workload runs in the orchestrator. When an update run is submitted, evaluate risk based on revision metadata and manifest riskLevel. Surface risk status in the UI. No manual approval gate — update proceeds automatically after risk display.
- Target modules:
    - `apps/orchestrator/backend/Services/PolicyEvaluationService.cs`
    - `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`
    - `apps/orchestrator/backend/Data/Entities/WorkloadRunEntity.cs`
    - `apps/orchestrator/backend/Contracts/Api/WorkloadRuns/WorkloadRunResponseModels.cs`
- Verification commands:
    - `dotnet test tests/orchestrator/integration --filter WorkloadRunsRisk`
- Acceptance links: AC-002, AC-006, AC-101
- Checklist:
    - [x] Risk level evaluation produces deterministic low/medium/high from revision metadata.
    - [x] Risk status is queryable via run detail API and visible in orchestrator UI.
    - [x] Update workflow proceeds automatically after risk display (no approval gate).

### W2-04b - Policy engine - preUpgradeActions enforcement in agent pipeline

- Owner: `TBD (Agent)`
- Status: `Not Started`
- Depends on: W2-04a
- Objective: enforce preUpgradeActions (backup, stop) per workload revision before package installation begins. Execute declarative actions in agent pipeline before the first install/upgrade step.
- Target modules:
    - `src/DeploymentPoC.Agent/Pipeline/PipelineExecutor.cs`
    - `src/DeploymentPoC.Agent/Steps/PreUpgradeAction.cs` (new)
- Verification commands:
    - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Policy`
- Acceptance links: AC-007, AC-006
- Checklist:
    - [ ] PreUpgradeActions defined in workload revision are executed before package steps.
    - [ ] PreUpgradeAction failure halts pipeline and reports reason.
    - [ ] Backup and stop action types are supported for PoC Phase 1.

### W3-01 - Windows agent service scaffold + runtime loop hardening

- Owner: `TBD (Agent)`
- Status: `Done`
- Objective: convert agent from bare web app to Windows service with SignalR runtime loop connecting to orchestrator.
- Target modules:
    - `apps/agent/backend/Program.cs`
    - `apps/agent/backend/Services/AgentRuntimeService.cs`
    - `apps/agent/backend/appsettings.json`
    - `apps/agent/backend/DeploymentPoC.Agent.csproj`
- Verification commands:
    - `dotnet test tests/agent/integration --filter AgentRuntime`
- Acceptance links: AC-004
- Checklist:
    - [x] Agent runs as Windows Service via `UseWindowsService()`.
    - [x] SignalR client connects to orchestrator `/hubs/agent` with auto-reconnect.
    - [x] `AssignRun` handler parses payload and sends `AckClaim` response.
    - [x] `appsettings.json` configures `Orchestrator:BaseUrl`.
    - [x] Integration tests verify payload parsing contract.

### W3-02a - Enrollment token generation + agent download endpoint

- Owner: `Backend/Frontend`
- Status: `Done`
- Objective: implement orchestrator-side enrollment token generation UI and API, plus browser-based agent.exe download endpoint with token binding.
- Target modules:
    - `apps/orchestrator/backend/Controllers/EnrollmentController.cs` (new)
    - `apps/orchestrator/backend/Controllers/AgentDownloadController.cs` (new)
    - `apps/orchestrator/web/src/services/api.ts` (wired to real APIs)
- Verification commands:
    - `dotnet test tests/orchestrator/integration/DeploymentPoC.Orchestrator.IntegrationTests.csproj --filter Enrollment`
- Acceptance links: AC-005
- Checklist:
    - [x] `POST /api/nodes/enroll` generates one-time enrollment token.
    - [x] Orchestrator UI shows "Enroll Node" action with token display (existing UI wired to real API).
    - [x] Agent download endpoint serves placeholder agent.exe (`GET /api/agent/download?token=`).
    - [x] Enrollment token is single-use and invalidated after agent registration (`POST /api/enrollment-tokens/{token}/consume`).

### W3-02b - Agent CLI enrollment + config persistence

- Owner: `TBD (Agent)`
- Status: `Not Started`
- Depends on: W3-02a
- Objective: implement agent-side enrollment CLI (`--enroll`, `--orchestrator-url`, `--reset-enrollment`) and cross-platform config persistence (`agent.json`).
- Target modules:
    - `apps/agent/backend/Program.cs` (CLI arg parsing, startup logic)
    - `apps/agent/backend/Services/AgentEnrollmentService.cs` (new)
    - `apps/agent/backend/Models/AgentConfig.cs` (new)
- Verification commands:
    - `dotnet test tests/agent/integration --filter Enrollment`
- Acceptance links: AC-005
- Checklist:
    - [ ] Parse `--enroll <token>`, `--orchestrator-url <url>`, `--reset-enrollment`.
    - [ ] `--reset-enrollment` wipes config file and exits.
    - [ ] `--enroll` + existing config → fail fast with error message.
    - [ ] `--enroll` + `--orchestrator-url` → consume token via HTTP, write `agent.json`, start runtime.
    - [ ] No flags + config exists → read `agent.json`, auto-reconnect to SignalR.
    - [ ] No flags + no config → exit with error.
    - [ ] Config path: `%LOCALAPPDATA%/DeploymentPoC/agent.json` (Windows), `/var/lib/deploymentpoc/agent.json` (Linux).
    - [ ] Enrollment HTTP client handles 410 (expired), 409 (consumed), 404 (missing) and exits non-zero.

### W3-03 - Agent workload pipeline (ordered packages)

- Owner: `TBD (Agent)`
- Status: `Done`
- Objective: execute workload package steps in revision order with deterministic checkpoints.
- Target modules:
    - `apps/agent/backend/Pipeline/PipelineExecutor.cs` (new)
    - `apps/agent/backend/Pipeline/PipelineContext.cs` (new)
    - `apps/agent/backend/Steps/AcquireArtifact.cs`
    - `apps/agent/backend/Steps/InstallOrUpgrade.cs` (new)
    - `apps/agent/backend/Steps/PostInstallVerify.cs` (new)
    - `apps/agent/backend/Steps/EmitFinalization.cs` (new)
    - `apps/agent/backend/Runtime/AgentRuntimeService.cs`
    - `apps/agent/backend/Program.cs`
- Verification commands:
    - `dotnet test tests/agent/integration --filter Pipeline`
- Evidence:
    - `tests/agent/integration/PipelineExecutorTests.cs` — 35 tests covering full pipeline, halt-on-failure, package ordering, install/verify/finalization steps
    - All 35 agent integration tests pass; total test suite: 116 passed, 0 failed
- Acceptance links: AC-004, AC-006
- Checklist:
    - [x] Install mode runs all packages in order (PipelineExecutor sorts by PackageIndex).
    - [x] Update mode placeholder — delta package selection is orchestrator-side (W3-04 dependency).
    - [x] Pipeline halts on first failure and emits `Fail` message with step history.
    - [x] Each step emits `StepStatus` message with current package index and step name.
    - [x] Success emits `Complete` message with full step history.
    - [x] `InstallOrUpgrade` expands `{artifactPath}` placeholder and validates exit code.
    - [x] `PostInstallVerify` checks file existence and version info (registry stub for PoC).

### W3-04 - Node workload state persistence/reporting

- Owner: `TBD (Agent/Backend)`
- Status: `Done`
- Objective: Orchestrator persists node workload state and reports it to the UI; agent messages drive state updates via SignalR.
- Target modules:
    - `apps/orchestrator/backend/Runtime/AgentConnectionTracker.cs` (new)
    - `apps/orchestrator/backend/Runtime/NodeWorkloadStateService.cs`
    - `apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs`
    - `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`
    - `apps/orchestrator/backend/Controllers/NodesController.cs`
    - `apps/orchestrator/backend/Data/Entities/WorkloadRunTimelineEntity.cs`
    - `apps/orchestrator/backend/Program.cs`
    - `apps/agent/backend/Services/AgentRuntimeService.cs`
    - `apps/orchestrator/web/src/services/api.ts`
- Verification commands:
    - `dotnet test tests/orchestrator/unit --filter AgentConnectionTracker`
    - `dotnet test tests/orchestrator/integration`
- Evidence:
    - `tests/orchestrator/unit/Runtime/AgentConnectionTrackerTests.cs` — 3 tests covering register/lookup/unregister/reregister
    - AgentRuntimeHub processes `Identify`, `SendMessage`, connection lifecycle
    - NodeWorkloadStateService processes AckClaim/StepStatus/Complete/Fail/LeaseHeartbeat with DB persistence and timeline entries
    - WorkloadRunsController sends `AssignRun` via SignalR to node group after run creation
    - Frontend `listNodeWorkloadStates()` calls real API endpoint
    - All 119 tests pass (39 orchestrator unit + 39 orchestrator integration + 35 agent integration + 6 contracts)
- Acceptance links: AC-001, AC-002
- Checklist:
    - [x] AgentRuntimeHub accepts `Identify(nodeId)` and tracks connection-to-node mapping.
    - [x] Agent calls `Identify` after SignalR connection with configured `Agent:NodeId`.
    - [x] Agent filters `AssignRun` messages by matching `payload.NodeId`.
    - [x] WorkloadRunsController sends `AssignRun` to node group after creating run.
    - [x] NodeWorkloadStateService persists workload state and timeline entries on agent messages.
    - [x] GET `/api/nodes/workload-states` returns current node workload states.
    - [x] Frontend displays node workload states from real API.

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

### W5-01a - Security baseline - RBAC + audit integrity

- Owner: `TBD (Security/Backend)`
- Status: `Not Started`
- Objective: implement role-based access control for runtime operations and ensure audit trail integrity (actor, target, sequence, workload revision, outcome).
- Target modules:
    - `src/DeploymentPoC.Orchestrator/Security/RbacService.cs` (new)
    - `src/DeploymentPoC.Orchestrator/Security/AuditService.cs` (new or extend)
    - `src/DeploymentPoC.Orchestrator/Middleware/AuthorizationMiddleware.cs` (new)
- Verification commands:
    - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Security`
- Acceptance links: AC-002, AC-102
- Checklist:
    - [ ] Unauthorized roles are denied runtime actions via RBAC.
    - [ ] Audit trail records actor, target, sequence, workload revision, and outcome.
    - [ ] Negative test: unauthorized action returns 403 with audit event.

### W5-01b - Security baseline - trust verification + secret hygiene

- Owner: `TBD (Security/Agent)`
- Status: `Not Started`
- Depends on: W5-01a
- Objective: enforce artifact signature/publisher verification and ensure no plaintext secrets in logs, config, or telemetry.
- Target modules:
    - `src/DeploymentPoC.Agent/Security/TrustVerificationService.cs` (new)
    - `src/DeploymentPoC.Orchestrator/Security/SecretHygieneService.cs` (new)
- Verification commands:
    - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Trust`
- Acceptance links: AC-102
- Checklist:
    - [ ] Unsigned artifact ingest is rejected (fail-closed).
    - [ ] Invalid certificate reconnect is denied.
    - [ ] No plaintext secrets exist in logs, config, or telemetry output.
    - [ ] Binary substitution attacks fail (signature/publisher validation on startup/update).

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

### W8-01a - Integration/E2E suite - lifecycle ACs (001-009)

- Owner: `TBD (QA/All)`
- Status: `Not Started`
- Objective: close all functional acceptance criteria (AC-001 through AC-009) with integration and E2E test evidence.
- Target modules:
    - `tests/DeploymentPoC.Orchestrator.IntegrationTests` (extend)
    - `tests/DeploymentPoC.Agent.IntegrationTests` (extend)
    - `tests/DeploymentPoC.E2E.Tests` (new or extend)
- Verification commands:
    - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests`
    - `dotnet test tests/DeploymentPoC.Agent.IntegrationTests`
    - `dotnet test tests/DeploymentPoC.E2E.Tests`
- Acceptance links: AC-001..AC-009
- Checklist:
    - [ ] AC-001: workload install/update runs observable on agent and orchestrator targets.
    - [ ] AC-002: rollback and cancel transitions are auditable and reason-coded.
    - [ ] AC-003: protocol sequence, idempotency, stale rejection verified.
    - [ ] AC-004: agent runs full local package-step pipeline.
    - [ ] AC-005: bootstrap failure triggers transactional rollback and token invalidation.
    - [ ] AC-006: MSI/EXE adapters produce normalized telemetry outcomes.
    - [ ] AC-007: mutation failure restores from snapshot with linked audit.
    - [ ] AC-008: `/api/jobs` mutations return 410 Gone.
    - [ ] AC-009: artifact ingest validates, resolves, and persists schema-valid manifests.

### W8-01b - Integration/E2E/chaos suite - NFR ACs (101-105, 107)

- Owner: `TBD (QA/All)`
- Status: `Not Started`
- Depends on: W8-01a
- Objective: close all non-functional acceptance criteria (AC-101 through AC-105, AC-107) with integration, chaos, security, observability, and UI regression evidence.
- Target modules:
    - `tests/DeploymentPoC.Orchestrator.ChaosTests` (extend)
    - `tests/DeploymentPoC.Orchestrator.IntegrationTests` (extend security/observability filters)
    - `tests/DeploymentPoC.E2E.Tests` (extend)
    - `apps/orchestrator/web/src/**/*.test.tsx` (UI regression)
- Verification commands:
    - `dotnet test tests/DeploymentPoC.Orchestrator.ChaosTests`
    - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter "Security|Otel"`
    - `pnpm --dir apps/orchestrator/web test -- --runInBand`
- Acceptance links: AC-101..AC-105, AC-107
- Checklist:
    - [ ] AC-101: stale lease policy behaves correctly under disconnect/reconnect and chaos.
    - [ ] AC-102: unsigned artifact blocked, unauthorized action denied, invalid cert rejected, no plaintext secrets.
    - [ ] AC-103: package-step telemetry queryable in Grafana/Loki by required correlation fields.
    - [ ] AC-104: REST/CLI workload commands work without script dependency.
    - [ ] AC-105: orchestrator runs on clean Windows host without preinstalled runtime.
    - [ ] AC-107: UI uses centered popups, terminal-like logs, workload terminology, drag-drop upload.

### W8-02a - Testcontainers agent enrollment integration tests

- Owner: `TBD (QA/Backend)`
- Status: `Not Started`
- Depends on: W3-02b, W3-04
- Objective: verify end-to-end agent enrollment using Testcontainers with real agent binary and orchestrator in-memory factory.
- Target modules:
    - `tests/orchestrator/integration/AgentEnrollment/AgentEnrollmentTests.cs` (new)
    - `tests/orchestrator/integration/Infrastructure/CustomWebApplicationFactory.cs` (extend with real Kestrel endpoint)
    - `tests/agent/Dockerfile` (new)
    - `tests/orchestrator/integration/AgentEnrollment/AgentEnrollmentTestFixture.cs` (new)
- Verification commands:
    - `dotnet test tests/orchestrator/integration --filter Enrollment`
- Acceptance links: AC-005
- Checklist:
    - [ ] Orchestrator factory binds real Kestrel endpoint on `0.0.0.0` for container reachability.
    - [ ] Dynamic host IP detection (`host.docker.internal` vs bridge gateway).
    - [ ] Agent Dockerfile uses pre-build + COPY into `runtime-deps:9.0`.
    - [ ] Test issues enrollment token via `POST /api/nodes/enroll`.
    - [ ] Test starts agent container with `--enroll <token> --orchestrator-url <url>`.
    - [ ] Test polls `GET /api/nodes` until node `status == "Online"` (timeout 30s).
    - [ ] **Happy path:** token consumed, config persisted, SignalR Identify called, node Online.
    - [ ] **Auto-reconnect:** restart container without `--enroll`, node returns Online.
    - [ ] **Reset + re-enroll:** `--reset-enrollment`, new token, new NodeId, Online.
    - [ ] **Expired token:** container exits non-zero, no node created.
    - [ ] **Consumed token:** container exits non-zero, no node created.
    - [ ] **Enroll with existing config:** container exits non-zero.

## MVP demo slice (April 28 deadline)

> **Deadline**: Tuesday April 28, 9-10am demo. Approximately 3 working days of implementation.
>
> **PRIMARY DEMO GOAL**: Run a workload from the Orchestrator UI on a remote agent node.
>
> **Demo flow**:
> 1. Install orchestrator (self-contained exe on clean Windows host)
> 2. Install agent (headless Windows service, browser-based bootstrap)
> 3. Ingest artifacts (upload real packages via orchestrator UI)
> 4. Ingest workload definitions (import JSON with 2-3 workloads)
> 5. **Run a workload from orchestrator UI** ← primary

### MVP-hard requirements (must work for demo)

These tasks form the minimum viable path to the demo. All other tasks are MVP-soft (nice-to-have for demo but not blocking).

| Priority | Task ID | Task | Rationale |
| -------- | ------- | ---- | --------- |
| MVP-hard | W1-01 | SQLite workload domain schema | Data foundation for everything |
| MVP-hard | W1-02 | Workload API contracts | Required to create/list workloads |
| MVP-hard | W1-03 | Workload run APIs | Required to submit/cancel/monitor runs |
| MVP-hard | W1-05 | Artifact ingest (backend) | Must upload packages to store |
| MVP-hard | W2-01 | Runtime contract updates (AssignRun) | Agent needs assignment payload |
| MVP-hard | W2-04a | Policy engine - risk detection (simplified) | Minimal: just surface risk level, no approval gate |
| MVP-hard | W3-01 | Windows agent service scaffold | Agent must connect to orchestrator |
| MVP-hard | W3-02a | Enrollment token + agent download | Must generate token + download agent.exe from browser |
| MVP-hard | W3-03 | Agent workload pipeline | Agent must execute packages in order |
| MVP-hard | W3-04 | Node workload state persistence/reporting | Orchestrator must see node state |
| MVP-hard | W6-01 | Orchestrator UI workload CRUD + run submission | PRIMARY DEMO SURFACE |
| MVP-hard | W6-01B | Local artifact-store management page | Must upload artifacts from UI |
| MVP-hard | W7-01 | Self-contained orchestrator packaging | Must run on clean Windows host |

### MVP-soft requirements (nice-to-have for demo, can stub/skip)

| Priority | Task ID | Task | Notes |
| -------- | ------- | ---- | ----- |
| MVP-soft | W0-02 | Contract freeze and migration map | Can be done in code comments/inline |
| MVP-soft | W1-04 | `/api/jobs` deprecation (`410 Gone`) | Not needed for demo; legacy only |
| MVP-soft | W2-02 | Sequence/idempotency enforcement | Simplify for demo: basic upsert, no chaos testing |
| MVP-soft | W2-03 | Lease manager + stale policy | Simplify for demo: basic timeout, no reassignment |
| MVP-soft | W2-04b | PreUpgradeActions enforcement | Can skip for demo (no actions in 2-3 package workload) |
| MVP-soft | W3-02 | Bootstrap token -> mTLS | Can use simpler auth for demo |
| MVP-soft | W4-01 | Config snapshot/migration/restore | Can skip for demo |
| MVP-soft | W5-01a | RBAC + audit integrity | Can skip RBAC for demo, add basic audit logging |
| MVP-soft | W5-01b | Trust verification + secret hygiene | Can use unsigned packages for demo |
| MVP-soft | W5-02 | Observability stack (OTel/Loki/Grafana) | Can use console logs for demo |
| MVP-soft | W6-01A | UI interaction refresh | Functional UI is enough for demo |
| MVP-soft | W6-01b | Run timeline + node visibility | Can show basic status for demo |
| MVP-soft | W6-02 | Jobs deprecation UX | Not needed for demo |
| MVP-soft | W6-03 | CLI command surface | Can use curl/Swagger for demo |
| MVP-soft | W7-02 | CI/CD policy gates | Not needed for demo |
| MVP-soft | W8-01a | Integration/E2E lifecycle tests | Manual testing for demo |
| MVP-soft | W8-01b | Chaos/NFR test suites | Not needed for demo |

### MVP demo execution order

```
Day 1: W1-01 → W1-02 → W1-03 → W1-05 (backend) ‖ W7-01 (packaging start)
Day 2: W2-01 → W2-04a (simplified) → W3-01 → W3-02a → W3-03 → W3-04 (agent + runtime)
Day 3: W6-01 → W6-01B (UI) → integration smoke test → demo
```

## Phase verification gates

| Phase     | Gate command(s)                                       | Exit criterion                                                                 | Owner | Status      |
| --------- | ----------------------------------------------------- | ------------------------------------------------------------------------------ | ----- | ----------- |
| Slice A   | `dotnet build DeploymentPoC.sln` + workload API tests | workload domain + deprecation + ingest validation pass                         | TBD   | Done        |
| Slice B   | protocol + lease + policy suites                      | deterministic run sequencing/risk detection/preUpgradeActions enforcement pass | TBD   | Done        |
| Slice C   | agent pipeline + migration tests                      | ordered package execution + restore behavior pass                              | TBD   | Done        |
| Slice D   | web + cli + observability tests                       | operator visibility and runtime operations pass                                | TBD   | Not Started |
| Packaging | self-contained publish + clean-host launch            | AC-105 satisfied                                                               | TBD   | Not Started |
| Final     | full tests + evidence review                          | AC-001..AC-009, AC-101..AC-105, AC-107 closed with evidence                    | TBD   | Not Started |

## AC evidence tracker

| AC ID  | Evidence artifact/path                                              | Owner | Status      |
| ------ | ------------------------------------------------------------------- | ----- | ----------- |
| AC-001 | `apps/orchestrator/backend/Controllers/WorkloadsController.cs`, `WorkloadRunsController.cs` | TBD   | In Progress |
| AC-002 | `apps/orchestrator/backend/Services/PolicyEvaluationService.cs`, `WorkloadRunsRiskTests.cs` | TBD   | Done        |
| AC-003 | `shared/contracts/Runtime/MessageTypes.cs`, `MessageEnvelope.cs`, `RunPayloads/AssignRunPayload.cs` | TBD   | Done        |
| AC-004 | `apps/agent/backend/Services/AgentRuntimeService.cs`, `AgentRuntimeContractTests.cs` | TBD   | Done        |
| AC-005 | `apps/agent/backend/Program.cs`, `AgentEnrollmentService.cs`, `tests/orchestrator/integration/AgentEnrollment/AgentEnrollmentTests.cs` | TBD   | Not Started |
| AC-006 | `tests/orchestrator/integration/Artifacts/ArtifactIngestApiContractTests.cs`, `tests/orchestrator/integration/WorkloadRuns/WorkloadRunsRiskTests.cs` | TBD   | Done        |
| AC-007 | TBD                                                                 | TBD   | Not Started |
| AC-008 | ~~`/api/jobs` deprecation~~ — skipped (fresh project, no legacy endpoint) | TBD   | Skipped     |
| AC-009 | `tests/orchestrator/integration/Artifacts/ArtifactIngestApiContractTests.cs` | TBD   | Done        |
| AC-101 | TBD                                                                 | TBD   | Not Started |
| AC-102 | `tests/orchestrator/integration/Artifacts/ArtifactIngestApiContractTests.cs` (signature verification) | TBD   | In Progress |
| AC-103 | Loki/Grafana query evidence                                         | TBD   | Not Started |
| AC-104 | `apps/orchestrator/backend/Controllers/WorkloadsController.cs`      | TBD   | Done        |
| AC-105 | clean-host orchestrator launch evidence                             | TBD   | Not Started |
| AC-107 | `apps/orchestrator/web/src/pages/Install.tsx` (drag-drop upload)   | TBD   | Done        |

## Change control notes

- One commit per completed task section in dependency order.
- If a task is split, use explicit suffixes and maintain AC mapping.
- Hardening Phase 2 controls can be logged as backlog but must not block Phase 1 closure.
- Storyboard updates are intentionally excluded in this pass per user instruction.
