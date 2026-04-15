# P1 Implementation Design (P1-01, P1-02, P1-03)

Date: 2026-04-15
Scope: PoC Phase 1 only
Status: Approved for implementation by user chat confirmation ("yes default", "yes proceeed")

## 1. Objective

Implement all `P1-*` tasks from `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md` in dependency order:

1. `P1-01` SQLite persistence for canonical entities
2. `P1-02` API contract alignment (`/api/jobs`, `/api/jobs/{jobId}/steps`, `/api/jobs/{jobId}/cancel`, `/api/nodes`)
3. `P1-03` Artifact HTTP transport with range/chunk retrieval and no artifact bytes over SignalR

Because the repository does not currently contain `src/DeploymentPoC.Agent`, `P1-03` will include a minimal agent project and only the acquisition surface needed for artifact retrieval tests. This does not include full P3/P4 agent service/runtime architecture.

## 2. Inputs and Constraints

Primary sources:

- `docs/distributed-installer/poc-phase1-prd-final.md`
- `docs/distributed-installer/18-installation-and-operational-storyboards-canonical.md`
- `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
- `docs/distributed-installer/10-core-contracts-pack.md`
- `docs/distributed-installer/08-requirements-contract.md`

Non-negotiable constraints applied by this design:

- PoC Phase 1 scope only (no Phase 2 hardening work)
- Single orchestrator assumption
- Internal-only artifact source
- SignalR for control/status only
- HTTP artifact transfer with range/chunk retrieval for large payloads
- Runtime operations exposed via API/CLI, not script orchestration
- Canonical runtime sequence remains authoritative even if full runtime hub work is scheduled in later tasks

## 3. Current Baseline and Gaps

Current implementation is a bootstrap PoC skeleton:

- Runtime state uses in-memory dictionaries in `AppStore`.
- Jobs and nodes APIs exist but do not conform to API-001..API-005 contract shapes.
- Job cancel currently uses `DELETE /api/jobs/{id}`, not `POST /api/jobs/{jobId}/cancel`.
- No `/api/jobs/{jobId}/steps` endpoint.
- No artifact HTTP controller/service with range support.
- No agent project to implement `AcquireArtifact`.

Gap conclusion:

- `P1-01` must establish durable control-plane state.
- `P1-02` must contract-align API routes and payloads.
- `P1-03` must create a dedicated artifact data-plane and a minimal client consumer.

## 4. Execution Strategy

### 4.1 Order

Strict dependency order will be followed:

1. Persistence foundation (`P1-01`)
2. API contract alignment on persisted model (`P1-02`)
3. Artifact transport and minimal agent acquisition path (`P1-03`)

### 4.2 Branch and isolation

Implementation proceeds in the current feature branch/worktree.
Subagents may be used for narrow, independent chunks (for example DTO creation, integration test scaffolding), but shared files (`Program.cs`, controllers, solution file) are coordinated sequentially to avoid conflicts.

### 4.3 Commit boundaries

Commit boundaries are one per tracker task:

- Commit A: `P1-01`
- Commit B: `P1-02`
- Commit C: `P1-03`

If a task requires a split to remain reviewable, the split follows tracker-compatible suffixing and keeps AC mapping explicit.

## 5. P1-01 Design: SQLite Persistence for Canonical Entities

### 5.1 New data layer

Add orchestrator data layer under `src/DeploymentPoC.Orchestrator/Data`:

- `InstallerDbContext`
- `Entities/JobEntity`
- `Entities/JobStepEntity`
- `Entities/NodeEntity`
- `Entities/AssignmentLeaseEntity`
- `Entities/ConfigSnapshotEntity`
- `Migrations/*` (initial migration)

`JobStepEntity` is included to support API-003 (`/steps`) with durable step timeline behavior.

### 5.2 Entity model (contract-aligned)

- `JobEntity`
  - `JobId` (Guid PK)
  - `State` (string or enum-backed string)
  - `Mode` (`install|update|rollback|modify|cancel` constrained)
  - `ReasonCode` (nullable int/string)
  - `CreatedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`
  - Node target linkage (normalized table or persisted target list; implementation will use normalized link if needed by query patterns)
- `JobStepEntity`
  - `JobStepId` (Guid PK)
  - `JobId` (FK)
  - `StepId` (string contract key)
  - `Name`, `Status`, `Sequence`
  - `ReasonCode`, `TelemetryRef`, `Detail`
  - `StartedAtUtc`, `CompletedAtUtc`, `UpdatedAtUtc`
- `NodeEntity`
  - `NodeId` (Guid PK)
  - `AgentId` (string, nullable until auth flows in later phases)
  - `Hostname`, `AgentVersion`
  - `Status`, `LastSeenUtc`
- `AssignmentLeaseEntity`
  - `AssignmentId` (Guid/string PK)
  - `LeaseId`, `JobId`, `AgentId`
  - `TtlSeconds`, `LastHeartbeatUtc`, `LastAckedSequence`
  - `State`
- `ConfigSnapshotEntity`
  - `ConfigSnapshotId` (Guid/string PK)
  - `JobId`, `NodeId`, `PackageId`
  - `SourceSchemaVersion`
  - `CapturedAtUtc`, `StorageLocation`, `IntegrityHash`

### 5.3 Program wiring

In `Program.cs`:

- Register `DbContext` with SQLite provider.
- Resolve connection string from configuration (`ConnectionStrings:InstallerDb`) with sensible local default.
- Apply migrations at startup for PoC convenience (`Database.Migrate()` in startup scope).

### 5.4 In-memory deprecation boundary

For runtime state (`Job`, `Node`, `AssignmentLease`, `ConfigSnapshot`), controllers/services will be switched off `AppStore` and onto EF-backed services/repositories.

`AppStore` can remain temporarily only for non-runtime transitional scaffolding, but no runtime-state read/write path may depend on it after `P1-01` completion.

### 5.5 Acceptance mapping

- AC-001/AC-002: durable job lifecycle state
- AC-007: config snapshot persistence baseline
- AC-101: lease persistence primitives available

## 6. P1-02 Design: API Contract Alignment

### 6.1 New API contracts

Add DTOs under `src/DeploymentPoC.Orchestrator/Contracts/Api/`:

- `CreateJobRequest`
- `CreateJobResponse`
- `JobDetailResponse`
- `JobStepListResponse`
- `CancelJobRequest`
- `CancelJobResponse`
- `NodeListResponse` (supporting API-004)

Contract intent is to align to API-001..API-005 behavior in `10-core-contracts-pack.md`.

### 6.2 Route and behavior alignment

`JobsController` target route: `api/jobs`

- `POST /api/jobs` (API-001)
  - Validate request shape, mode, targets, and manifest/policy fields required by PoC.
  - Persist job + initial timeline/assignment-ready state.
  - Return `CreateJobResponse` with `jobId`, `state`.
- `GET /api/jobs/{jobId}` (API-002)
  - Return summary + timeline metadata + reason code info.
- `GET /api/jobs/{jobId}/steps` (API-003)
  - Return ordered step list from `JobStepEntity`.
- `POST /api/jobs/{jobId}/cancel` (API-005)
  - Cancel safely from queued/assigned/running states.
  - Preserve auditable state transition semantics and return `CancelJobResponse`.

`NodesController` target route: `api/nodes`

- `GET /api/nodes` (API-004)
  - Return node list and health/status fields from persisted nodes.

### 6.3 Backward compatibility handling

If legacy endpoints conflict with canonical routes (example: `DELETE /api/jobs/{id}`), legacy routes may remain temporarily as deprecated aliases but canonical routes become primary and documented behavior.

### 6.4 Acceptance mapping

- AC-001: submit + observe job through canonical API path
- AC-002: cancel reflects auditable state transition
- AC-104: runtime actions exposed through API contract surface

## 7. P1-03 Design: Artifact HTTP Transport + Range/Chunk Retrieval

### 7.1 Orchestrator artifact service

Add:

- `src/DeploymentPoC.Orchestrator/Services/ArtifactStoreService.cs`
- `src/DeploymentPoC.Orchestrator/Controllers/ArtifactsController.cs`

Storage backend for Phase 1 remains local filesystem on orchestrator host.

Service responsibilities:

- Resolve artifact path from package/version identity.
- Provide metadata (`size`, `hash`, content type, etag/version token).
- Open read stream for controller.

### 7.2 HTTP contract behavior

`ArtifactsController` provides:

- `HEAD` endpoint for metadata discovery (size, etag, digest metadata headers as needed).
- `GET` endpoint with `Range` support (`206 Partial Content` when requested).
- Consistent errors for not found/invalid range.

Implementation will use framework range support where possible (`enableRangeProcessing`) plus explicit header handling needed for deterministic behavior in tests.

### 7.3 Control/data-plane boundary enforcement

- SignalR/control payloads must not include artifact bytes.
- Assignment/control messages carry only artifact references/metadata pointers.

### 7.4 Minimal agent stub for acquisition

Create minimal project:

- `src/DeploymentPoC.Agent/DeploymentPoC.Agent.csproj`
- `src/DeploymentPoC.Agent/Steps/AcquireArtifact.cs`

Scope of this stub:

- HTTP artifact acquisition (HEAD + ranged GET loop or resumable download strategy)
- Local file assembly and integrity-ready handoff
- No Windows service host, no runtime hub loop, no full pipeline orchestration (those remain in later tracker tasks)

### 7.5 Acceptance mapping

- AC-006: artifact retrieval reliability and adapter pipeline dependency readiness
- AC-102: transport boundary and integrity-first posture baseline

## 8. Testing and Verification Design

### 8.1 Test project additions

- Add `tests/DeploymentPoC.Orchestrator.IntegrationTests` for persistence/API/artifact transport.
- Add `tests/DeploymentPoC.Agent.IntegrationTests` for `AcquireArtifact` behavior.

### 8.2 P1-01 tests

- Persistence integration tests for create/read/update of canonical entities.
- Migration startup test to verify schema creation.
- Controller tests adjusted to use DbContext-backed paths.

### 8.3 P1-02 tests

- API contract tests for all API-001..API-005 endpoints.
- Validation tests for create/cancel semantics and state transitions.
- `/api/jobs/{jobId}/steps` ordering and payload shape checks.

### 8.4 P1-03 tests

- Artifact upload/register and download tests.
- Range request tests including multi-chunk assembly behavior.
- Negative tests for invalid ranges and missing artifacts.
- Agent acquisition tests proving HTTP-only transfer path.

### 8.5 Verification commands

- `dotnet build DeploymentPoC.sln`
- `dotnet ef database update --project src/DeploymentPoC.Orchestrator`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Persistence`
- `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter Controllers`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ApiContract`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Artifact`
- `dotnet test tests/DeploymentPoC.Agent.IntegrationTests --filter AcquireArtifact`

## 9. Risks and Mitigations

- Existing lightweight model/controller coupling may cause wider refactor churn.
  - Mitigation: introduce mapping layer from entities to API DTOs and migrate controller-by-controller.
- Solution file currently has structural inconsistencies (duplicate GUID usage).
  - Mitigation: normalize solution entries while adding projects; verify `dotnet build` and tests after each task.
- New agent stub may be mistaken for full runtime agent implementation.
  - Mitigation: keep project scope narrow and document explicit exclusions in code and tracker updates.

## 10. Definition of Done for This Design

This design is complete when:

- P1-01, P1-02, and P1-03 are implemented in order with passing verification commands.
- Runtime state no longer depends on in-memory dictionaries for canonical entities.
- API behavior matches API-001..API-005 contract shape and routes.
- Artifact transfer uses HTTP with range/chunk support and no SignalR payload transport.
- Minimal agent acquisition path exists solely to satisfy P1 artifact retrieval requirements and tests.
