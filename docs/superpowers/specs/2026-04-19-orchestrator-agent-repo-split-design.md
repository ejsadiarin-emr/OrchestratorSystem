# Orchestrator + Agent Repo Split Design

Date: 2026-04-19
Status: Draft for implementation planning
Scope: Big-bang repository restructure into symmetric app layouts for Orchestrator and Agent

## 1. Objective

Restructure the repository into two explicit application projects with matching structure:

- `apps/orchestrator` (self-contained .NET backend + embedded React web UI)
- `apps/agent` (self-contained .NET backend + embedded React web UI)

while preserving Phase 1 product behavior and introducing clear ownership boundaries through `shared/contracts`.

## 2. Approved Strategic Decisions

1. Use a **big-bang physical restructure** (single migration stream) rather than staged in-place refactors.
2. Keep both applications structurally symmetric:
   - `backend/` (.NET host/runtime)
   - `web/` (React app embedded into backend static hosting output)
3. Keep cross-application interoperability explicit via `shared/contracts`.
4. Remove `/api/jobs` runtime pathway (not just deprecate) and standardize runtime lifecycle semantics on workload-runs.
5. Execute migration autonomously with subagent implementation and reviewer feedback loops before completion claims.

## 3. Canonical Target Topology

```text
apps/
  orchestrator/
    backend/
    web/
  agent/
    backend/
    web/
shared/
  contracts/
tests/
  orchestrator/
    unit/
    integration/
  agent/
    integration/
  contracts/
docs/
```

## 4. Ownership Boundary Contract

### 4.1 Orchestrator backend

- Owns remote control-plane responsibilities: API contracts, run orchestration, policy/lease handling, persistence, artifact distribution endpoints, and orchestrator web static hosting.
- Must not own agent-local execution pipeline concerns.

### 4.2 Agent backend

- Owns node-local execution-plane responsibilities: local runtime host, package-step execution, local diagnostics, node-local status surface, and agent web static hosting.
- Must not own remote control-plane orchestration authority.

### 4.3 Shared contracts

- Owns cross-app message, DTO, and protocol types.
- Is the only location allowed to define Orchestrator-Agent wire contracts.

### 4.4 UI boundaries

- `apps/orchestrator/web` surfaces fleet/control-plane operations.
- `apps/agent/web` surfaces node-local operations and guided update controls.

## 5. Path Migration Design

## 5.1 Primary path remap

- `src/DeploymentPoC.Orchestrator` -> `apps/orchestrator/backend`
- `src/DeploymentPoC.Agent` -> `apps/agent/backend`
- `src/DeploymentPoC.Contracts` -> `shared/contracts`
- `web` -> split into:
  - `apps/orchestrator/web`
  - `apps/agent/web`
- `tests/DeploymentPoC.Orchestrator.Tests` -> `tests/orchestrator/unit`
- `tests/DeploymentPoC.Orchestrator.IntegrationTests` -> `tests/orchestrator/integration`
- `tests/DeploymentPoC.Agent.IntegrationTests` -> `tests/agent/integration`
- `tests/DeploymentPoC.Contracts.Tests` -> `tests/contracts`

### 5.2 Root-level assets retained

- `DeploymentPoC.sln`
- `docs/`
- `.gitignore`, `.gitattributes`
- `.opencode/`, `.superpowers/`
- optional root workspace package metadata

### 5.3 Regenerated or excluded artifacts

- Do not migrate generated artifacts:
  - `**/bin/**`, `**/obj/**`, `**/node_modules/**`, `**/dist/**`
  - backend-generated `wwwroot` bundles
- Regenerate static assets from each app web build after move.

## 6. Contract and Endpoint Policy

1. Runtime naming standard uses run semantics (`AssignRun`, `runId`, `workload-runs`).
2. `/api/jobs` runtime endpoints are removed from source and tests in this migration.
3. Web app typed clients and backend contracts must converge on `shared/contracts` authority.

## 7. Build and Runtime Wiring Rules

1. Each web app has its own `vite.config.ts` and writes build output to its sibling backend static content target.
2. .NET solution and all project references must be updated in one migration stream.
3. Backend static file configuration must point to app-local build output only.
4. Command surfaces become:
   - `apps/orchestrator/web` npm scripts
   - `apps/agent/web` npm scripts
   - root `dotnet build/test DeploymentPoC.sln`

## 8. Migration Risks and Mitigations

### 8.1 High risks

- Broken project/solution references after move.
- Broken web embed path wiring in both backends.
- Contract drift while removing `/api/jobs` and aligning run semantics.

### 8.2 Mitigations

- Validate each migration task with focused build/test gates.
- Use reviewer subagents for spec-compliance then code-quality loops per task.
- Keep a single, explicit move map in implementation plan and execute in bounded chunks.

## 9. Acceptance Criteria

1. Repository contains symmetric app structures under `apps/orchestrator` and `apps/agent`.
2. Both backends compile from moved locations via root solution build.
3. Both web apps compile and emit static assets to their corresponding backend targets.
4. `/api/jobs` runtime endpoints and associated route wiring are removed.
5. Contract/test/project references resolve only through new path topology.
6. Documentation and plan files reflect the new canonical structure.

## 10. Implementation Readiness

This design is implementation-ready once the migration plan includes:

- explicit task-by-task path moves,
- solution/reference rewires,
- endpoint removal scope,
- per-task verification commands,
- reviewer-subagent feedback loops,
- and final full build/test verification for both app families.
