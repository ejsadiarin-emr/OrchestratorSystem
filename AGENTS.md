# DeploymentPoC — Agent Instructions

## Project Overview

Enterprise deployment orchestration PoC: an ASP.NET Core 10.0 orchestrator that deploys software packages to Windows nodes via HTTP-polling agents.

## Architecture at a Glance

```
apps/
  orchestrator/backend/   — ASP.NET Core 10.0 REST API (dev port 5124, prod port 5000)
  orchestrator/web/       — React 18 + Vite + Tailwind v4 + shadcn/ui
  agent/backend/          — Windows agent (HTTP poller, Kestrel on port 5001)
shared/
  contracts/              — DTOs/contracts shared between orchestrator and agent
tests/
  orchestrator/unit/      — NUnit + Moq
  orchestrator/integration/
  agent/unit/
  agent/integration/
  contracts/              — Contract serialization tests
```

- Frontend builds **into** `apps/orchestrator/backend/wwwroot/` — the backend serves the SPA
- SQLite via EF Core (DB file at `dist/deployment-poc.db` in production, `artifacts/` next to exe in dev)
- Agent ↔ Orchestrator: **HTTP polling** (primary). SignalR hub at `/hubs/agent` exists but is **disabled** — push dispatch is commented out on both sides.
- Agent auth: `agent_id` query parameter (NodeId GUID). No bearer token, no agent secret.
- Agent polls `GET /api/workload-runs/pending?agent_id={nodeId}` every 10s (configurable via `Agent:PollIntervalSeconds`)
- Orchestrator probes agent's `/api/detect` via `POST http://{nodeIp}:5001/api/detect` for pre-checks

## Essential Commands

| Task | Command |
|------|---------|
| Run orchestrator (dev) | `dotnet run --project apps\orchestrator\backend` (port 5124, Swagger at /swagger) |
| Run agent (dev) | `dotnet run --project apps\agent\backend` (port 5001) |
| Run frontend (dev) | `cd apps/orchestrator/web && pnpm dev` (proxies /api and /hubs to :5124) |
| Build everything (debug) | `make build` |
| Full publish to `dist/` | `make publish` (clean, stop-processes, build-frontend, publish both exes, copy-workloads) |
| Full publish + download artifacts | `make full` |
| .NET tests (all) | `dotnet test` |
| .NET tests (single project) | `dotnet test tests/orchestrator/unit` |
| Frontend tests | `cd apps/orchestrator/web && pnpm test` |
| Frontend lint | `cd apps/orchestrator/web && pnpm lint` |
| Frontend typecheck | `cd apps/orchestrator/web && pnpm build` (tsc -b runs as part of build) |
| Clean dist | `make clean` |

## Important Context

- **`make publish` kills running processes first** — will force-kill orchestrator/agent exes before overwriting
- **Windows-only agent** — agent is Windows-only; orchestrator and frontend are cross-platform
- **Frontend is embedded** — `vite.config.ts` writes `build.outDir` to `../backend/wwwroot/`; the backend serves the SPA
- **Agent runs its own Kestrel on port 5001** — serves `/api/detect` and `/health` endpoints for orchestrator probing
- **Agent polls every 10s by default** — configurable via `Agent:PollIntervalSeconds` in agent `appsettings.json`
- **Node heartbeat monitor** runs every 30s on orchestrator, marks nodes Offline if `LastSeenUtc` > 2 min
- **Publish produces self-contained single-file exes** — no .NET runtime install needed on target machines

### Agent CLI

```
DeploymentPoC.Agent.exe --enroll <token> --orchestrator-url <url> [--name <displayName>]
DeploymentPoC.Agent.exe --reset
```

- `--name` sets display name (not `--display-name`)
- `--reset` deletes `agent.json` and exits (no API call, no service unregistration)
- `--enroll` consumes the enrollment token, writes `agent.json` to `%LOCALAPPDATA%\DeploymentPoC\`, then exits
- Agent must be registered as a Windows Service separately (e.g., `sc create`)

### Agent Config (`agent.json`)

```
{ "NodeId": "<guid>", "OrchestratorUrl": "http://host:5000" }
```

- Contains only NodeId (GUID) and OrchestratorUrl — no agent secret or bearer token
- Auth is via `agent_id={NodeId}` query parameter on all API calls
- Written by `--enroll`, deleted by `--reset`

### Workload Concepts (non-obvious)

- **Workload Definition** = named container with a **published revision** reference
- **Workload Revision** = versioned snapshot of packages + init steps; **immutable** after creation (only step fields + publish status can change)
- Revisions must be **published** before they can be used in runs (`POST /api/workloads/{id}/publish`)
- Bulk import from JSON/JSONC files: `POST /api/workloads/bulk-import` (auto-creates definitions, revisions, packages)

### Artifact & Detection Conventions

- **Artifact manifest pairing**: ZIP uploads use stem-matching — `foo.msi` + `foo.manifest.json` (`.manifest.json` extension)
- **Detection types**: `file` (path exists), `registry` (DisplayName match in Uninstall key), `version_manifest` (find binary + run `--version`)
- **Init steps default shell is PowerShell** (`-Command`), not cmd — configured via `DefaultShell` field on revisions
- **Install adapters** specify `command`, `arguments`, `uninstallCommand`, `uninstallArgs`, `upgradeBehavior`, `expectedExitCodes`, `timeoutSeconds`
- **Upgrade behavior**: `"InPlace"` (run over existing) or `"UninstallFirst"` (uninstall then install)
- **Chunked upload** for large artifacts: create session → upload chunks → complete (resumable, up to 2GB)

### Pre-Checks

- Explicit triggered task (not passive scanning): admin selects nodes + workload → orchestrator `POST`s to agent's `/api/detect`
- Agent runs local detection per package, returns `AlreadySatisfied`, `WrongVersion` (with version comparison), or `NotPresent`
- Results feed into `NodeWorkloadStateEntities` and pre-check summary actions: `FreshInstall`, `Update`, `Skip`, `InstallMissing`, `BlockedDowngrade`

### Database

- SQLite via EF Core, auto-created on startup with `EnsureCreated()`
- Revisions are **immutable** — enforced by `SaveChanges` interceptor (delete blocked, only step/publish fields mutable)
- Filtered unique index prevents concurrent active runs on same node+workload: `(NodeId, WorkloadId) WHERE State IN ('Queued','Running')`
- Legacy tables still exist: `JobEntities`, `JobStepEntities`, `AssignmentLeaseEntities`, `ConfigSnapshotEntities`

## Testing

- .NET tests: **NUnit** + **Moq** + EF Core InMemory
- Frontend tests: **Vitest** + jsdom + @testing-library/react
- Integration tests require orchestrator to be running (they send HTTP requests)
- To add a new test: create a class with `[TestFixture]`, methods with `[Test]`, standard NUnit conventions

## Frontend Conventions

- Vite path alias: `@/` maps to `src/`
- React Router v7 with `<Routes>`
- API client in `src/services/api.ts`
- Uses Tailwind v4 with PostCSS config

## OpenCode Config

- `opencode.json` references `AGENTS.md` and `.opencode/instructions/INSTRUCTIONS.md`
- Available commands: `/plan`, `/code-review`, `/security`, `/build-fix`, `/e2e`, `/refactor-clean`, `/verify`, `/update-docs`
- This repo does not have `.github/workflows/` — no CI pipeline is configured
