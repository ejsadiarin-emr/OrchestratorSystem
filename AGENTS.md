# DeploymentPoC — Agent Instructions

## Project Overview

Enterprise deployment orchestration PoC: an ASP.NET Core 10.0 orchestrator that deploys software packages to Windows nodes via SignalR-connected agents.

## Architecture at a Glance

```
apps/
  orchestrator/backend/   — ASP.NET Core 10.0 REST API + SignalR hub (port 5000)
  orchestrator/web/       — React 18 + Vite + Tailwind + shadcn/ui
  agent/backend/          — Windows agent (SignalR client, runs on each target node)
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
- SQLite via EF Core (DB file at `dist/deployment-poc.db` in production, `artifacts/` next to exe)
- Agent communicates via SignalR at `/hubs/agent`

## Essential Commands

| Task | Command |
|------|---------|
| Run orchestrator (dev) | `dotnet run --project apps\orchestrator\backend` (port 5000, Swagger at /swagger) |
| Run agent (dev) | `dotnet run --project apps\agent\backend` (port 5001) |
| Run frontend (dev) | `cd apps/orchestrator/web && pnpm dev` (proxies API to :5124) |
| Build frontend | `cd apps/orchestrator/web && pnpm build` |
| Full publish to `dist/` | `make publish` (kills running processes, builds all, copies workloads) |
| .NET tests (all) | `dotnet test` |
| Frontend tests | `cd apps/orchestrator/web && pnpm test` |
| Frontend lint | `cd apps/orchestrator/web && pnpm lint` |
| Frontend typecheck | `cd apps/orchestrator/web && pnpm build` (tsc -b is part of build script) |
| Clean dist | `make clean` |

## Important Context

- **`make publish` runs `stop-processes` first** — will kill running orchestrator/agent exes
- **Windows-only agent** — the agent is Windows-only for PoC; orchestrator frontend runs cross-platform
- **Frontend is embedded** — `vite.config.ts` writes `build.outDir` to `../backend/wwwroot/`. The frontend is compiled into the backend `wwwroot` at publish time.
- **Agent defaults to port 5001** to avoid collision with orchestrator's 5000
- **The SLN uses Solution Folders** — `dotnet test` at root discovers all test projects; use `dotnet test tests/orchestrator/unit` to target a single project
- **Agent enrollment**: `.\agent.exe --enroll <token> --orchestrator-url http://<host>:5124`
- **Artifact upload zip convention**: media + manifest pair by shared base name at zip root

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

- `opencode.json` references `AGENTS.md` and `.opencode/instructions/INSTRUCTIONS.md` (generic ECC boilerplate)
- Available commands: `/plan`, `/code-review`, `/security`, `/build-fix`, `/e2e`, `/refactor-clean`, `/verify`
- This repo does not have `.github/workflows/` — no CI pipeline is configured
