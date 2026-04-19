# DeploymentPoC Web UI

React + TypeScript + Vite frontend for Phase 1 distributed installer workflows.

This app is currently a **frontend-only mock** for storyboard validation. Core flows use typed in-memory services in `src/services/api.ts` and realtime simulation in `src/services/realtime.ts`.

## What this UI includes

- Artifact ingest mock: upload/import -> analyze/prefill -> verify -> store
- Agent bootstrap mock: `POST /api/nodes/enroll` token semantics + URL+token bootstrap
- First-connect node metadata auto-collection simulation
- Delivery protocol mock: AssignJob -> HEAD -> ranged GET loop -> digest/signature verify -> complete/fail

## Prerequisites

- Node.js 20+
- pnpm 10+

## Install

```bash
cd apps/agent/web
pnpm install
```

## Run in development

```bash
cd apps/agent/web
pnpm dev
```

Open the local URL shown by Vite (typically `http://localhost:5173`).

## Quality checks

```bash
cd apps/agent/web
pnpm lint
pnpm test
pnpm build
```

## Build output and embedding

Production build outputs directly to the orchestrator embedded static site directory:

- `../backend/wwwroot`

That behavior is configured in `apps/agent/web/vite.config.ts` (`build.outDir`).

To regenerate embedded assets:

```bash
cd apps/agent/web
pnpm build
```

## Project layout (web)

- `src/pages/Install.tsx` - artifact ingest flow
- `src/pages/Nodes.tsx` - enrollment/bootstrap flow
- `src/pages/Jobs.tsx` - job delivery progression
- `src/pages/Dashboard.tsx` - summary/events view
- `src/services/api.ts` - typed in-memory mock API
- `src/services/realtime.ts` - simulated realtime updates
- `src/types.ts` - shared domain contracts

## Notes

- This mock is intentionally backend-free for UX/protocol validation.
- The `Packages` page still reflects legacy API-style calls and is not part of the new mock flow contract.
