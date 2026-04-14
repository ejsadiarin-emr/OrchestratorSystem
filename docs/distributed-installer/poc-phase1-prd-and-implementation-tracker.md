# PoC Phase 1 PRD and Implementation Tracker

Date: 2026-04-14
Status: In Progress (3/20 tasks complete)
Source of truth: `poc-phase1-prd-final.md`, `18-installation-and-operational-storyboards-canonical.md`, `08-requirements-contract.md`, `09-security-pack.md`, `10-core-contracts-pack.md`, `11-config-persistence-contract.md`, `12-devops-pipeline-design-pack.md`, and `13-poc-phase1-definition-of-done.md`.

## Scope guardrails (non-negotiable)

- PoC Phase 1 only. Do not execute Hardening Phase 2 implementation work.
- Orchestrator must be a self-contained single executable with embedded React UI assets.
- Orchestrator launch must not require preinstalled .NET runtime or IIS.
- Runtime workstation install/upgrade/rollback actions must be Orchestrator API/CLI driven only.
- Direct workstation deployment from Azure DevOps pipeline is out of scope.
- Runtime package/artifact source must be internal-only (no external package source dependency).
- Phase 1 execution target is single orchestrator (no HA/multi-orchestrator commitments).
- Canonical runtime sequence is fixed:
  `Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

## Status legend

- `Not Started`
- `In Progress`
- `Blocked`
- `Done`

## Sprint board (dependency ordered)

| Task ID | Task | Sprint | Depends On | Owner | Status | AC IDs |
|---|---|---|---|---|---|---|
| P0-01 | Shared runtime contracts library | S1 | - | TBD (Backend) | Done | AC-003 |
| P0-02 | Phase 1 decision closure from PRD addendum | S1 | P0-01 | TBD (Product/Arch/Security) | Done | AC-001..AC-007, AC-101..AC-105 |
| P0-03 | Canonical storyboard merge and contradiction fixes | S1 | P0-02 | TBD (Architecture) | Done | AC-001, AC-002, AC-003, AC-102 |
| P1-01 | SQLite persistence for canonical entities | S1 | P0-03 | TBD (Backend) | Not Started | AC-001, AC-002, AC-007, AC-101 |
| P1-02 | API contract alignment (`/api/jobs`, `/steps`, `/cancel`, `/nodes`) | S1 | P1-01 | TBD (Backend) | Not Started | AC-001, AC-002, AC-104 |
| P1-03 | Artifact HTTP transport + range/chunk retrieval | S1 | P1-02 | TBD (Backend/Agent) | Not Started | AC-001, AC-006, AC-102 |
| P2-01 | SignalR protocol + sequence/idempotency enforcement | S1 | P1-02 | TBD (Backend) | Not Started | AC-003, AC-101 |
| P2-02 | Lease manager + `AssignedStale` timeout policy | S1 | P2-01 | TBD (Backend) | Not Started | AC-101 |
| P2-03 | Policy evaluation engine (retry/idempotency/risk/approval) | S2 | P2-01, P2-02, P1-03 | TBD (Backend/Agent) | Not Started | AC-002, AC-006, AC-007, AC-101 |
| P3-01 | Windows agent service scaffold + runtime channel loop | S2 | P0-01 | TBD (Agent) | Not Started | AC-004 |
| P3-02 | Bootstrap token to mTLS steady-state auth flow | S2 | P3-01, P2-01 | TBD (Security/Agent) | Not Started | AC-005, AC-102 |
| P4-01 | Agent local typed pipeline + MSI/EXE adapters | S2 | P3-01 | TBD (Agent) | Not Started | AC-004, AC-006 |
| P4-02 | Config snapshot/migration/restore + audit linkage | S2 | P4-01, P1-01 | TBD (Agent/Backend) | Not Started | AC-007 |
| P5-01 | RBAC, artifact trust, audit hash chain, secret hygiene | S3 | P2-01, P3-02, P4-01 | TBD (Security/Backend) | Not Started | AC-102, AC-002 |
| P5-02 | OTel file export policy + redaction controls | S3 | P5-01 | TBD (Security/Backend) | Not Started | AC-102, AC-103 |
| P6-01 | UI runtime screens + live step timeline | S3 | P1-02, P2-01 | TBD (Frontend) | Not Started | AC-001, AC-002, AC-103, AC-105 |
| P6-02 | CLI runtime command surface | S3 | P1-02 | TBD (Platform) | Not Started | AC-104, AC-001, AC-002 |
| P7-01 | Self-contained single-file orchestrator packaging | S4 | P6-01 | TBD (Platform/DevOps) | Not Started | AC-105 |
| P7-02 | CI/CD policy gates and orchestrator-only deploy boundary | S4 | P7-01, P6-02 | TBD (DevOps) | Not Started | AC-104, AC-105 |
| P8-01 | Integration/E2E/chaos acceptance suite and evidence | S4 | P1-01..P7-02 | TBD (QA/All) | Not Started | AC-001..AC-007, AC-101..AC-105 |

## Task details checklist

### P0-01 - Shared runtime contracts library

- Owner: `TBD (Backend)`
- Status: `Done`
- Objective: Centralize message envelope, canonical message types, state/reason enums for orchestrator and agent.
- Target modules:
  - Create `src/DeploymentPoC.Contracts/DeploymentPoC.Contracts.csproj`
  - Create `src/DeploymentPoC.Contracts/Runtime/MessageEnvelope.cs`
  - Create `src/DeploymentPoC.Contracts/Runtime/MessageTypes.cs`
  - Create `src/DeploymentPoC.Contracts/Jobs/JobState.cs`
  - Create `src/DeploymentPoC.Contracts/Jobs/ReasonCodes.cs`
  - Modify `DeploymentPoC.sln`
- Interfaces/contracts impacted: FR-002, core message envelope in `10-core-contracts-pack.md`.
- Test requirements: unit tests for required envelope fields and enum compatibility.
- Verification commands:
  - `dotnet build DeploymentPoC.sln`
  - `dotnet test tests/DeploymentPoC.Contracts.Tests`
- Acceptance links: AC-003
- Suggested commit boundary: `feat(contracts): add shared runtime envelope and state contracts`
- Checklist:
  - [x] Contracts project compiles and is referenced by orchestrator and agent projects.
  - [x] Envelope includes `assignmentId`, `leaseId`, `jobId`, `agentId`, `sequence` fields.

### P0-02 - Phase 1 decision closure from PRD addendum

- Owner: `TBD (Product/Arch/Security)`
- Status: `Done`
- Objective: Close all open/partial policy decisions listed in `17-poc-phase1-prd-v2-capability-addendum.md`.
- Target modules:
  - Modify `docs/distributed-installer/17-poc-phase1-prd-v2-capability-addendum.md`
  - Modify `docs/distributed-installer/poc-phase1-prd-final.md`
  - Modify `docs/distributed-installer/18-installation-and-operational-storyboards-canonical.md`
- Interfaces/contracts impacted: FR-001..FR-006, NFR-001..NFR-005.
- Test requirements: decision closure review and explicit signoff comments.
- Verification commands:
  - `dotnet build DeploymentPoC.sln`
  - Manual doc review against Q11/Q13/Q15/Q16/Q21/Q22/Q23/Q24/Q26
- Acceptance links: AC-001..AC-007, AC-101..AC-105
- Suggested commit boundary: `docs(prd): close phase-1 policy decisions for execution`
- Checklist:
  - [x] Q11, Q13, Q15, Q16, Q21, Q22, Q23, Q24, Q26 marked closed with explicit decisions.
  - [x] Final PRD and canonical storyboard reflect closed decisions.

### P0-03 - Canonical storyboard merge and contradiction fixes

- Owner: `TBD (Architecture)`
- Status: `Done`
- Objective: Merge strengths from storyboard drafts and remove contradictory/risky guidance.
- Target modules:
  - Modify `docs/distributed-installer/18-installation-and-operational-storyboards-canonical.md`
  - Modify `docs/distributed-installer/poc-phase1-prd-final.md`
- Interfaces/contracts impacted: FR-001, FR-002, NFR-001, NFR-002.
- Test requirements: architecture/security review pass.
- Verification commands:
  - Manual checklist from `docs/distributed-installer/sessions/20260413-storyboard-review-output.md` section E
- Acceptance links: AC-001, AC-002, AC-003, AC-102
- Suggested commit boundary: `docs(storyboard): publish canonical merged phase-1 flows`
- Checklist:
  - [x] Retry contradiction is removed and policy-driven classification is explicit.
  - [x] Self-update uses staged swap/supervisor pattern as normative.
  - [x] Signing authority and key-custody minimum standard is documented.
  - [x] SignalR control-plane and HTTP artifact-plane boundary is explicit.
  - [x] Package channel taxonomy (`stable`, `canary`, `test`) is documented with immutable/hash-bound identity.

### P1-01 - SQLite persistence for canonical entities

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: Replace in-memory state with SQLite-backed canonical entities.
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Data/InstallerDbContext.cs`
  - Create `src/DeploymentPoC.Orchestrator/Data/Entities/JobEntity.cs`
  - Create `src/DeploymentPoC.Orchestrator/Data/Entities/NodeEntity.cs`
  - Create `src/DeploymentPoC.Orchestrator/Data/Entities/AssignmentLeaseEntity.cs`
  - Create `src/DeploymentPoC.Orchestrator/Data/Entities/ConfigSnapshotEntity.cs`
  - Create `src/DeploymentPoC.Orchestrator/Data/Migrations/*`
  - Modify `src/DeploymentPoC.Orchestrator/Program.cs`
- Interfaces/contracts impacted: FR-001, FR-006, NFR-001.
- Test requirements: unit mapping tests and integration persistence tests.
- Verification commands:
  - `dotnet ef database update --project src/DeploymentPoC.Orchestrator`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Persistence`
- Acceptance links: AC-001, AC-002, AC-007, AC-101
- Suggested commit boundary: `feat(orchestrator): add sqlite persistence for canonical entities`
- Checklist:
  - [ ] `Job`, `Node`, `AssignmentLease`, and `ConfigSnapshot` persisted in SQLite.
  - [ ] In-memory-only paths no longer used for runtime state.

### P1-02 - API contract alignment

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: Align API behavior and payloads with API-001..API-005.
- Target modules:
  - Modify `src/DeploymentPoC.Orchestrator/Controllers/JobsController.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Controllers/NodesController.cs`
  - Create `src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobRequest.cs`
  - Create `src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobResponse.cs`
  - Create `src/DeploymentPoC.Orchestrator/Contracts/Api/JobDetailResponse.cs`
  - Create `src/DeploymentPoC.Orchestrator/Contracts/Api/JobStepListResponse.cs`
  - Create `src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobRequest.cs`
  - Create `src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobResponse.cs`
- Interfaces/contracts impacted: FR-001, NFR-004, API-001..API-005.
- Test requirements: API contract integration tests and endpoint auth tests.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter Controllers`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ApiContract`
- Acceptance links: AC-001, AC-002, AC-104
- Suggested commit boundary: `feat(api): align job and node endpoints with core contract pack`
- Checklist:
  - [ ] `/api/jobs/{jobId}/steps` endpoint implemented.
  - [ ] Cancel endpoint uses `POST /api/jobs/{jobId}/cancel` contract shape.

### P1-03 - Artifact HTTP transport + range/chunk retrieval

- Owner: `TBD (Backend/Agent)`
- Status: `Not Started`
- Objective: Implement artifact transfer over HTTP endpoints (including range/chunk retrieval) and explicitly avoid artifact payload transfer over SignalR.
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Controllers/ArtifactsController.cs`
  - Create `src/DeploymentPoC.Orchestrator/Services/ArtifactStoreService.cs`
  - Modify `src/DeploymentPoC.Agent/Steps/AcquireArtifact.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Program.cs`
- Interfaces/contracts impacted: FR-001, FR-005, NFR-002.
- Test requirements: integration tests for artifact upload/download, range requests, and no-SignalR-payload contract.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Artifact`
  - `dotnet test tests/DeploymentPoC.Agent.IntegrationTests --filter AcquireArtifact`
- Acceptance links: AC-001, AC-006, AC-102
- Suggested commit boundary: `feat(artifact): add http artifact transport with range chunk retrieval`
- Checklist:
  - [ ] Agent retrieves artifacts through HTTP endpoints only.
  - [ ] Large artifacts support range/chunk retrieval.
  - [ ] SignalR messages do not carry artifact payloads.

### P2-01 - SignalR protocol + idempotency enforcement

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: Enforce canonical runtime message sequence and strict idempotency/replay guards for control/status plane only (no artifact payload transfer over SignalR).
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Hubs/AgentRuntimeHub.cs`
  - Create `src/DeploymentPoC.Orchestrator/Runtime/StepStatusIngestService.cs`
  - Create `src/DeploymentPoC.Orchestrator/Runtime/SequenceConflictAuditService.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Program.cs`
- Interfaces/contracts impacted: FR-002, NFR-001.
- Test requirements: unit + integration tests for stale/out-of-order and payload conflict behavior.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter Sequence`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter SignalRProtocol`
- Acceptance links: AC-003, AC-101
- Suggested commit boundary: `feat(runtime): implement signalr protocol sequencing and idempotent status ingest`
- Checklist:
  - [ ] Upsert key is `(jobId, stepId, sequence)`.
  - [ ] Same-key payload mismatch rejects and emits `sequence_payload_conflict`.
  - [ ] Reconnect resumes from `lastAcknowledgedSequence + 1`.

### P2-03 - Policy evaluation engine (retry/idempotency/risk/approval)

- Owner: `TBD (Backend/Agent)`
- Status: `Not Started`
- Objective: Implement unified policy evaluation for retryability, idempotency mode, risk level, and approval requirements across install/update/modify/downgrade flows.
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Runtime/PolicyEvaluationService.cs`
  - Create `src/DeploymentPoC.Orchestrator/Runtime/ApprovalGateService.cs`
  - Modify `src/DeploymentPoC.Agent/Pipeline/PipelineExecutor.cs`
  - Modify `src/DeploymentPoC.Agent/Steps/InstallOrUpgrade.cs`
- Interfaces/contracts impacted: FR-001, FR-005, FR-006, NFR-001.
- Test requirements: unit + integration tests for policy branch selection, bounded retry, non-idempotent/high-risk behavior, and downgrade approvals.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter Policy`
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Policy`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Approval`
- Acceptance links: AC-002, AC-006, AC-007, AC-101
- Suggested commit boundary: `feat(policy): add unified retry idempotency risk and approval evaluation`
- Checklist:
  - [ ] Retry is bounded and policy-driven.
  - [ ] High-risk/non-idempotent steps never blind auto-retry.
  - [ ] Downgrade path enforces explicit approval when enabled.
  - [ ] Channel policy (`stable`, `canary`, `test`) is validated by API/model rules.

### P2-02 - Lease manager + stale handling

- Owner: `TBD (Backend)`
- Status: `Not Started`
- Objective: Enforce lease TTL, heartbeat interval, stale threshold, and stale timeout bounds.
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Runtime/LeaseManager.cs`
  - Create `src/DeploymentPoC.Orchestrator/Runtime/LeaseTimeoutWorker.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Data/Entities/AssignmentLeaseEntity.cs`
- Interfaces/contracts impacted: FR-002, NFR-001.
- Test requirements: integration + chaos tests for heartbeat loss and stale timeout behavior.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Lease`
  - `dotnet test tests/DeploymentPoC.Orchestrator.ChaosTests --filter AssignedStale`
- Acceptance links: AC-101
- Suggested commit boundary: `feat(reliability): add lease ttl heartbeat and assignedstale timeout policy`
- Checklist:
  - [ ] TTL is `90s`, heartbeat interval `15s`, stale threshold `3` missed.
  - [ ] Auto-fail bound is 2 reassignment attempts or 15 minutes stale duration.

### P3-01 - Windows agent service scaffold

- Owner: `TBD (Agent)`
- Status: `Not Started`
- Objective: Create persistent Windows service agent with SignalR client and channel-based execution loop.
- Target modules:
  - Create `src/DeploymentPoC.Agent/DeploymentPoC.Agent.csproj`
  - Create `src/DeploymentPoC.Agent/Program.cs`
  - Create `src/DeploymentPoC.Agent/Services/AgentWorker.cs`
  - Create `src/DeploymentPoC.Agent/Services/RuntimeClient.cs`
  - Create `src/DeploymentPoC.Agent/Services/JobChannelService.cs`
  - Modify `DeploymentPoC.sln`
- Interfaces/contracts impacted: FR-003.
- Test requirements: unit tests for queue processing and cancellation behavior.
- Verification commands:
  - `dotnet build src/DeploymentPoC.Agent/DeploymentPoC.Agent.csproj`
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Worker`
- Acceptance links: AC-004
- Suggested commit boundary: `feat(agent): scaffold windows service runtime and signalr client`
- Checklist:
  - [ ] Agent starts and connects to orchestrator hub.
  - [ ] Job messages can be buffered and executed from channel loop.

### P3-02 - Bootstrap token to mTLS auth flow

- Owner: `TBD (Security/Agent)`
- Status: `Not Started`
- Objective: Support one-time enrollment token and enforce per-agent mTLS identity for steady-state reconnect.
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Security/EnrollmentService.cs`
  - Create `src/DeploymentPoC.Orchestrator/Security/AgentCertificateValidator.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Hubs/AgentRuntimeHub.cs`
  - Create `src/DeploymentPoC.Agent/Security/CertificateBindingService.cs`
- Interfaces/contracts impacted: FR-004, NFR-002.
- Test requirements: integration tests for token single-use and invalid-cert reconnect rejection.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Auth`
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Enrollment`
- Acceptance links: AC-005, AC-102
- Suggested commit boundary: `feat(security): enforce enrollment token flow and mTLS steady-state identity`
- Checklist:
  - [ ] Token is consumed once and invalidated.
  - [ ] Reconnect without valid bound cert is rejected.

### P4-01 - Agent local typed pipeline + adapters

- Owner: `TBD (Agent)`
- Status: `Not Started`
- Objective: Execute full per-job step pipeline locally and support MSI/EXE adapters with normalized outcomes.
- Target modules:
  - Create `src/DeploymentPoC.Agent/Pipeline/IInstallStep.cs`
  - Create `src/DeploymentPoC.Agent/Pipeline/IPreCheck.cs`
  - Create `src/DeploymentPoC.Agent/Pipeline/PipelineExecutor.cs`
  - Create `src/DeploymentPoC.Agent/Steps/PreConditionCheck.cs`
  - Create `src/DeploymentPoC.Agent/Steps/AcquireArtifact.cs`
  - Create `src/DeploymentPoC.Agent/Steps/ValidateSignatureAndHash.cs`
  - Create `src/DeploymentPoC.Agent/Steps/DetectCurrentState.cs`
  - Create `src/DeploymentPoC.Agent/Steps/InstallOrUpgrade.cs`
  - Create `src/DeploymentPoC.Agent/Steps/PostInstallVerify.cs`
  - Create `src/DeploymentPoC.Agent/Steps/EmitFinalization.cs`
  - Create `src/DeploymentPoC.Agent/Adapters/MsiAdapter.cs`
  - Create `src/DeploymentPoC.Agent/Adapters/ExeAdapter.cs`
- Interfaces/contracts impacted: FR-003, FR-005.
- Test requirements: unit tests for step ordering and adapter normalization; integration tests for MSI/EXE flow.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Pipeline`
  - `dotnet test tests/DeploymentPoC.Agent.IntegrationTests --filter Adapter`
- Acceptance links: AC-004, AC-006
- Suggested commit boundary: `feat(agent): implement local typed pipeline with msi exe adapters`
- Checklist:
  - [ ] Agent executes full job pipeline without orchestrator step-by-step dispatch.
  - [ ] MSI and EXE paths emit normalized status/telemetry.

### P4-02 - Config snapshot/migration/restore contract implementation

- Owner: `TBD (Agent/Backend)`
- Status: `Not Started`
- Objective: Implement upgrade config persistence contract with deterministic migration and restore-on-failure.
- Target modules:
  - Create `src/DeploymentPoC.Agent/Config/IConfigMigration.cs`
  - Create `src/DeploymentPoC.Agent/Config/MigrationChainResolver.cs`
  - Create `src/DeploymentPoC.Agent/Config/SnapshotService.cs`
  - Create `src/DeploymentPoC.Agent/Config/RestoreService.cs`
  - Create `src/DeploymentPoC.Orchestrator/Services/ConfigAuditService.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Data/Entities/ConfigSnapshotEntity.cs`
- Interfaces/contracts impacted: FR-006, `11-config-persistence-contract.md`.
- Test requirements: unit tests for missing migration hop; integration tests for restore and audit event emission.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Agent.Tests --filter Migration`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ConfigSnapshot`
- Acceptance links: AC-007
- Suggested commit boundary: `feat(upgrade): implement snapshot migration restore contract and audit linkage`
- Checklist:
  - [ ] Missing hop results in `migration_path_missing`.
  - [ ] Failed migration triggers restore and linked audit event.

### P5-01 - Security controls baseline implementation

- Owner: `TBD (Security/Backend)`
- Status: `Not Started`
- Objective: Enforce RBAC, artifact trust checks, audit tamper evidence, and no plaintext secret handling.
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Security/RbacPolicies.cs`
  - Create `src/DeploymentPoC.Orchestrator/Security/AuditHashChainService.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Controllers/*.cs`
  - Create `src/DeploymentPoC.Agent/Security/ArtifactValidationService.cs`
  - Create `src/DeploymentPoC.Agent/Security/SecretProvider.cs`
- Interfaces/contracts impacted: NFR-002, mitigation mappings M-001..M-006.
- Test requirements: security integration tests for unsigned artifact block, unauthorized role denial, and secret redaction.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.SecurityTests`
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Authorization`
- Acceptance links: AC-102, AC-002
- Suggested commit boundary: `feat(security): add artifact trust rbac audit chain and secret protection`
- Checklist:
  - [ ] Unsigned or tampered artifacts fail closed.
  - [ ] Unauthorized role cannot trigger runtime install actions.
  - [ ] Sensitive values are not written to logs or config in plaintext.

### P6-01 - UI runtime screens and live timeline

- Owner: `TBD (Frontend)`
- Status: `Not Started`
- Objective: Provide actionable PoC dashboard and job views with live status/step timeline.
- Target modules:
  - Modify `web/src/pages/Dashboard.tsx`
  - Modify `web/src/pages/Jobs.tsx`
  - Modify `web/src/pages/Nodes.tsx`
  - Modify `web/src/pages/Install.tsx`
  - Create `web/src/services/api.ts`
  - Create `web/src/services/realtime.ts`
  - Modify `src/DeploymentPoC.Orchestrator/DeploymentPoC.Orchestrator.csproj`
- Interfaces/contracts impacted: FR-001, NFR-003, NFR-005.
- Test requirements: web integration tests and E2E tests for job submit/cancel/status.
- Verification commands:
  - `pnpm --dir web test`
  - `pnpm --dir web run build`
  - `pnpm --dir web run test:e2e`
- Acceptance links: AC-001, AC-002, AC-103, AC-105
- Suggested commit boundary: `feat(ui): add poc runtime dashboards jobs nodes and live step timeline`
- Checklist:
  - [ ] UI shows terminal job state and step-level updates.
  - [ ] Embedded UI artifacts can be served by orchestrator executable.

### P6-02 - CLI runtime command surface

- Owner: `TBD (Platform)`
- Status: `Not Started`
- Objective: Implement runtime command path via CLI (not script orchestration).
- Target modules:
  - Create `src/DeploymentPoC.Cli/DeploymentPoC.Cli.csproj`
  - Create `src/DeploymentPoC.Cli/Commands/JobsCommand.cs`
  - Create `src/DeploymentPoC.Cli/Commands/NodesCommand.cs`
  - Modify `DeploymentPoC.sln`
- Interfaces/contracts impacted: NFR-004, FR-001.
- Test requirements: integration tests for create/cancel/status commands.
- Verification commands:
  - `dotnet run --project src/DeploymentPoC.Cli -- jobs create --help`
  - `dotnet test tests/DeploymentPoC.Cli.IntegrationTests`
- Acceptance links: AC-104, AC-001, AC-002
- Suggested commit boundary: `feat(cli): add runtime api cli surface for jobs and nodes`
- Checklist:
  - [ ] Install/upgrade/rollback/cancel/status are available through CLI calls to orchestrator API.

### P7-01 - Self-contained orchestrator packaging

- Owner: `TBD (Platform/DevOps)`
- Status: `Not Started`
- Objective: Produce self-contained single-file orchestrator executable with embedded React UI for clean-host run.
- Target modules:
  - Modify `src/DeploymentPoC.Orchestrator/DeploymentPoC.Orchestrator.csproj`
  - Create `scripts/package-orchestrator.ps1`
  - Modify `src/DeploymentPoC.Orchestrator/Program.cs`
- Interfaces/contracts impacted: NFR-005.
- Test requirements: packaging validation on clean Windows machine without .NET/IIS.
- Verification commands:
  - `dotnet publish src/DeploymentPoC.Orchestrator/DeploymentPoC.Orchestrator.csproj --self-contained --runtime win-x64 -p:PublishSingleFile=true`
  - Run published executable on clean host and verify dashboard + API.
- Acceptance links: AC-105
- Suggested commit boundary: `feat(packaging): publish self-contained single-file orchestrator with embedded ui`
- Checklist:
  - [ ] Orchestrator launches on clean host with no runtime prereq installs.
  - [ ] Embedded dashboard assets load from executable.

### P7-02 - CI/CD gates and policy enforcement

- Owner: `TBD (DevOps)`
- Status: `Not Started`
- Objective: Implement pipeline gates and enforce orchestrator-only deployment boundary.
- Target modules:
  - Create or modify `azure-pipelines.yml`
  - Create `scripts/sign-artifacts.ps1`
  - Create `scripts/validate-clean-host-launch.ps1`
- Interfaces/contracts impacted: NFR-004, NFR-005, DevOps policy in `12-devops-pipeline-design-pack.md`.
- Test requirements: CI gate tests for packaging and policy boundaries.
- Verification commands:
  - CI run for stages: `CI -> Publish -> PackagingValidation -> DeployOrchestrator -> Integration -> E2E`
  - Validate no direct workstation deployment tasks exist in runtime path.
- Acceptance links: AC-104, AC-105
- Suggested commit boundary: `chore(ci): add poc pipeline gates and orchestrator-only deploy policy`
- Checklist:
  - [ ] Pipeline deploys orchestrator only.
  - [ ] Runtime workstation actions are triggered only through Orchestrator API/CLI.

### P8-01 - End-to-end acceptance and evidence suite

- Owner: `TBD (QA/All)`
- Status: `Not Started`
- Objective: Build acceptance test suite and evidence pack covering all PoC AC IDs.
- Target modules:
  - Create `tests/DeploymentPoC.Orchestrator.IntegrationTests/*`
  - Create `tests/DeploymentPoC.Agent.IntegrationTests/*`
  - Create `tests/DeploymentPoC.E2E/*`
  - Create `tests/DeploymentPoC.ChaosTests/*`
- Interfaces/contracts impacted: All FR/NFR acceptance criteria.
- Test requirements: unit, integration, e2e, and chaos scenarios.
- Verification commands:
  - `dotnet test DeploymentPoC.sln`
  - `pnpm --dir web run test:e2e`
  - `dotnet test tests/DeploymentPoC.ChaosTests`
- Acceptance links: AC-001..AC-007, AC-101..AC-105
- Suggested commit boundary: `test(poc): add full acceptance matrix for phase-1 contracts`
- Checklist:
  - [ ] Evidence artifact is captured for each AC.
  - [ ] No Hardening Phase 2-only checks are required for PoC completion.

## Phase verification gates

| Phase | Gate command(s) | Exit criterion | Owner | Status |
|---|---|---|---|---|
| Phase 0-1 | `dotnet build DeploymentPoC.sln` + persistence/API tests | Contracts + SQLite + API compile and pass tests | TBD | In Progress |
| Phase 2 | runtime protocol and lease test suites | Sequence, idempotency, stale policy pass | TBD | Not Started |
| Phase 3-4 | agent unit/integration + migration tests | Agent executes full pipeline; config restore verified | TBD | Not Started |
| Phase 5 | security test suite | Unsigned block, RBAC deny, secret hygiene pass | TBD | Not Started |
| Phase 6 | web test + e2e + CLI integration tests | UI and CLI runtime operations pass | TBD | Not Started |
| Phase 7 | `dotnet publish` self-contained + clean-host launch validation + CI gate run | AC-104/AC-105 policy and packaging satisfied | TBD | Not Started |
| Phase 8 | full solution tests + acceptance evidence review | AC-001..AC-105 marked complete with evidence | TBD | Not Started |

## AC evidence tracker

| AC ID | Evidence artifact/path | Owner | Status |
|---|---|---|---|
| AC-001 | TBD | TBD | Not Started |
| AC-002 | TBD | TBD | Not Started |
| AC-003 | src/DeploymentPoC.Contracts/* | TBD (Backend) | Done |
| AC-004 | TBD | TBD | Not Started |
| AC-005 | TBD | TBD | Not Started |
| AC-006 | TBD | TBD | Not Started |
| AC-007 | TBD | TBD | Not Started |
| AC-101 | TBD | TBD | Not Started |
| AC-102 | TBD | TBD | Not Started |
| AC-103 | TBD | TBD | Not Started |
| AC-104 | TBD | TBD | Not Started |
| AC-105 | TBD | TBD | Not Started |

## Change control notes

- Use one commit per task completion in dependency order.
- If a task must be split, append suffixes (`P4-01a`, `P4-01b`) and keep AC mapping explicit.
- Any request that adds Hardening Phase 2 controls must be recorded separately and not block PoC completion.
### P5-02 - OTel file export policy + redaction controls

- Owner: `TBD (Security/Backend)`
- Status: `Not Started`
- Objective: Implement and verify Phase 1 OTel defaults (file-based export with rotation/retention) and denylist-based sensitive data redaction/access controls.
- Target modules:
  - Create `src/DeploymentPoC.Orchestrator/Observability/OtelExportPolicy.cs`
  - Create `src/DeploymentPoC.Orchestrator/Observability/LogRedactionPolicy.cs`
  - Modify `src/DeploymentPoC.Orchestrator/Program.cs`
- Interfaces/contracts impacted: NFR-002, NFR-003.
- Test requirements: integration tests for rotation/retention policy and secret/token redaction behavior.
- Verification commands:
  - `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Otel`
  - `dotnet test tests/DeploymentPoC.SecurityTests --filter Redaction`
- Acceptance links: AC-102, AC-103
- Suggested commit boundary: `feat(observability): enforce otel file export defaults and redaction policy`
- Checklist:
  - [ ] File-based OTel export with rotation and retention is enforced.
  - [ ] Sensitive fields (secrets/tokens/credentials) are redacted in logs.
  - [ ] Access policy for telemetry endpoints/log files is least-privilege by default.
