# DeploymentPoC

Proof-of-Concept for an enterprise Windows package orchestration system. Operators upload installer artifacts, define versioned workload packages, enroll target nodes via secure tokens, and execute orchestrated deployments (install / update / uninstall) across a fleet of Windows agents.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Orchestrator (ASP.NET Core)                         │
│  ┌──────────┐  ┌─────────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │ Artifacts│  │  Workloads  │  │   Runs   │  │  Nodes   │  │  Tokens  │  │
│  │Controller│  │ Controller  │  │Controller│  │Controller│  │Controller│  │
│  └──────────┘  └─────────────┘  └──────────┘  └──────────┘  └──────────┘  │
│        ↓              ↓               ↓              ↓              ↓       │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │         SQLite (EF Core) + On-Disk Artifact Store                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│        ↑                                                        ↑           │
│   (HTTP download)                                    (HTTP POST pre-check)   │
└─────────────────────────────────────────────────────────────────────────────┘
         ↑                                            ↓
         │         Agent ←── HTTP Polling ──→ Orchestrator               │
         │         (10 s interval)                                         │
         │                                                                  │
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Windows Agent (per node, Kestrel :5001)                  │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Polling Loop          Detection Endpoint        Health Endpoint     │   │
│  │  GET /pending          POST /api/detect          GET /health         │   │
│  │  Claim runs            Registry / file /                              │   │
│  │  Report timeline       version_manifest detection                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│        ↓                                                                    │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Local Execution: detect → acquire → install/uninstall → verify     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘

Notes:
• SignalR hub at /hubs/agent exists but is currently disabled. Push dispatch is
  commented out; agents use HTTP polling as the primary task-delivery mechanism.
• The orchestrator probes the agent directly (HTTP POST to agent :5001 /api/detect)
  for on-demand pre-checks.
• Artifacts are downloaded by the agent via chunked HTTP from /api/artifacts/…
```

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | ASP.NET Core 10.0 |
| Frontend | React 18 + TypeScript + Vite |
| Database | SQLite via EF Core |
| API | REST + SignalR |
| UI | Tailwind CSS + shadcn/ui |

## Prerequisites

- **.NET 10.0 SDK** (for development)
- **Node.js 20+** and **pnpm**
- **Windows** (agent runtime is Windows-only for PoC)

## Development Setup

### Quick start (Makefile)

```bash
# Run orchestrator backend (dev)
make run-orchestrator          # http://localhost:5124

# Run frontend hot-reload dev server
make run-frontend              # proxies API to :5124

# Run agent (dev)
make run-agent                 # http://localhost:5001
```

### Manual start

```bash
# Backend
cd apps/orchestrator/backend
dotnet run                     # http://localhost:5124

# Frontend (Terminal 2)
cd apps/orchestrator/web
pnpm install
pnpm dev                       # http://localhost:5173, proxies /api to :5124

# Agent (Terminal 3)
cd apps/agent/backend
dotnet run                     # http://localhost:5001
```

### Verify

- Open http://localhost:5124
- Swagger UI: http://localhost:5124/swagger

## Production Build

### One-shot publish (recommended)

```bash
make publish
```

This kills running processes, builds the frontend into `wwwroot/`, publishes both
exes as self-contained single-file binaries, and copies test workloads to `dist/`.

### Manual publish

```bash
# 1. Build frontend (outputs to ../backend/wwwroot/)
cd apps/orchestrator/web
pnpm install
pnpm build

# 2. Publish orchestrator
cd ../backend
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  -o .\..\..\..\dist

# 3. Publish agent
cd ..\..\agent\backend
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  -o .\..\..\..\dist
```

**Output:** `dist/DeploymentPoC.Orchestrator.exe` and `dist/DeploymentPoC.Agent.exe`

### Run production builds

**Orchestrator:**
```powershell
.\dist\DeploymentPoC.Orchestrator.exe
# http://localhost:5000
```

**Agent (on each target node):**
```powershell
.\dist\DeploymentPoC.Agent.exe
# Defaults to http://localhost:5001
# Override: --urls http://localhost:<port>
```

## Demo Flow

### Prerequisites
- Orchestrator running
- At least one target Windows node
- An installer artifact (MSI or EXE) and its `.manifest.json`

### Step 1: Upload an artifact

Artifacts can be uploaded as a standalone file, a single-artifact zip, or a bulk zip.

**Manifest format** (`.manifest.json`):
```json
{
  "packageId": "git",
  "version": "2.48.1",
  "channel": "stable",
  "artifactType": "exe",
  "installAdapter": {
    "type": "exe",
    "command": "Git-2.48.1-64-bit.exe",
    "arguments": "/VERYSILENT /NORESTART /NOCANCEL",
    "uninstallCommand": "C:\\Program Files\\Git\\unins000.exe",
    "uninstallArgs": "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
    "upgradeBehavior": "InPlace",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "registry",
    "path": "Git",
    "expectedVersion": "2.48.1"
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
```

**Upload modes:**
- **Standalone** — drag-and-drop a raw installer, fill in the manifest form
- **Single zip** — one media file + manifest pair at zip root (same base name)
- **Bulk zip** — multiple media + manifest pairs at zip root

The system auto-analyzes standalone uploads and prefills metadata.

**Artifact storage** (`artifacts/` next to the running executable):
```
artifacts/
└── {packageId}/
    └── {version}/
        ├── artifact.bin
        └── resolved-manifest.json
```

**Chunked upload** (for large files > 2 GB):
```
POST /api/artifacts/upload-sessions
POST /api/artifacts/upload-sessions/{id}/chunks?index=&totalChunks=
POST /api/artifacts/upload-sessions/{id}/complete
```

### Step 2: Create a workload definition and revision

1. Navigate to **Workloads**
2. Click **Create Workload** — enter name and description
3. Click **+ New Revision** on the workload — enter version (e.g. `1.0.0`)
4. Add packages from the artifact store
5. Click **Save Draft** or **Publish** (only published revisions can be used in runs)

### Step 3: Enroll a node

1. Navigate to **Nodes** → **Enrollment Tokens**
2. Click **Generate Token** (default TTL: 20 minutes)
3. Copy the token
4. On the target node, run:
   ```powershell
   .\DeploymentPoC.Agent.exe --enroll <token> --orchestrator-url http://<host>:5000
   ```
   This writes `%LOCALAPPDATA%\DeploymentPoC\agent.json` containing the `NodeId` and `OrchestratorUrl`.
5. Start the agent service (e.g. `sc create DeploymentPoC.Agent binPath= "path\to\agent.exe"` then start it)
6. Verify the node appears in the Nodes list with status **Online**

> **Note:** `--enroll` does **not** auto-register the agent as a Windows Service. Use `sc create` or similar.

### Step 4: Run a workload

#### 4a. Fresh install

1. Navigate to **Workload Runs** → **Create Run**
2. Select workload, a **published** revision, target node(s), mode **install**
3. Click **Create Run**
4. Watch progress: **Queued → Running → Completed**

#### 4b. Update (differential)

1. Create a new revision with changed packages and **Publish** it
2. Create a run with mode **update**
3. The system computes a delta:
   - **Unchanged packages** — skipped entirely
   - **Changed packages** — uninstalled (old) then installed (new), or updated in-place depending on `upgradeBehavior`
   - **Added packages** — installed
   - **Removed packages** — uninstalled (reverse order)

#### 4c. Uninstall

1. Create a run with mode **uninstall**
2. The revision dropdown shows only revisions currently installed on nodes
3. The agent uninstalls every package in reverse order

#### 4d. Dry-run preview

Before creating a run, preview the delta:
```
GET /api/workload-runs/preview?workloadId=...&revisionId=...&mode=...&nodeIds=...
```

Returns per-node breakdown of `install`, `update`, `uninstall`, and `unchanged` packages.

#### 4e. Pre-checks (automatic)

Pre-checks run automatically when workload + revision are selected in the Run Creator. A badge appears per node:
- **pre-check passed** — all packages detected correctly
- **pre-check: issues** — some packages missing, wrong version, or not detected

The orchestrator sends detection requests directly to the agent's `/api/detect` endpoint (`:5001`).

### Step 5: Observe execution

On the target node, the agent:
1. Polls `/api/workload-runs/pending?agent_id={nodeId}` every 10 seconds
2. Claims the run (atomic `Queued → Running` transition)
3. For each package executes: **detect → preInitSteps → acquire → install/uninstall → postInitSteps → verify → report**
4. Posts timeline events to `/api/workload-runs/{runId}/timeline`
5. Reports final status (`Completed` or `Failed`) via PATCH
6. On success, the orchestrator updates `NodeWorkloadState` with the new revision

### Bulk import for testing

```bash
# Import baseline workload
curl -X POST http://localhost:5124/api/workloads/bulk-import \
  -H "Content-Type: multipart/form-data" \
  -F "file=@test-workloads/workloads-older.json"

# Import updated workload
curl -X POST http://localhost:5124/api/workloads/bulk-import \
  -H "Content-Type: multipart/form-data" \
  -F "file=@test-workloads/workloads-newer.json"
```

After import, create runs in `install`, `update`, and `uninstall` modes to observe differential behavior.

## API Reference

### Artifacts

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/artifacts` | POST | Upload artifact (standalone or auto-detected ZIP) |
| `/api/artifacts/bulk` | POST | Bulk import via flat ZIP |
| `/api/artifacts/upload-sessions` | POST | Create chunked upload session |
| `/api/artifacts/upload-sessions/{id}/chunks` | POST | Upload chunk |
| `/api/artifacts/upload-sessions/{id}/complete` | POST | Finalize upload |
| `/api/artifacts` | GET | List artifacts |
| `/api/artifacts/{packageId}/{version}` | GET | Download binary (range-enabled) |
| `/api/artifacts/{packageEntityId:guid}/download` | GET | Download by GUID |
| `/api/artifacts/{packageId}/{version}` | DELETE | Delete artifact + PackageEntity |

### Workloads

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/workloads` | GET | List workloads |
| `/api/workloads` | POST | Create workload definition |
| `/api/workloads/{id}` | GET | Get workload detail |
| `/api/workloads/{id}` | DELETE | Delete workload (cascades) |
| `/api/workloads/{id}/revisions` | POST | Create revision |
| `/api/workloads/{id}/revisions/{revisionId}` | PUT | Update mutable revision fields (steps, shell) |
| `/api/workloads/{id}/publish` | POST | Publish a revision |
| `/api/workloads/{id}/installed-revisions` | GET | List revisions installed on nodes |
| `/api/workloads/bulk-import` | POST | Bulk import from JSON/JSONC file |

### Workload Runs

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/workload-runs` | GET | List runs |
| `/api/workload-runs` | POST | Create run (one record per node) |
| `/api/workload-runs/{runId}` | GET | Run detail |
| `/api/workload-runs/{runId}/steps` | GET | Delta-computed step list |
| `/api/workload-runs/{runId}/cancel` | POST | Cancel run |
| `/api/workload-runs/preview` | GET | Dry-run delta preview |
| `/api/workload-runs/pending` | GET | Agent poll endpoint (also heartbeat) |
| `/api/workload-runs/{runId}` | PATCH | Agent claims / completes run |
| `/api/workload-runs/{runId}/timeline` | POST | Agent reports step timeline event |

### Nodes

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/nodes` | GET | List nodes |
| `/api/nodes` | POST | Manual node creation |
| `/api/nodes/{id}` | GET | Node detail |
| `/api/nodes/{id}` | PUT | Update node |
| `/api/nodes/{id}/display-name` | PATCH | Update display name |
| `/api/nodes/{id}` | DELETE | Delete node |
| `/api/nodes/{id}/details` | GET | Node detail with workloads |
| `/api/nodes/workload-states` | GET | All node-workload assignment states |
| `/api/nodes/prechecks` | POST | Trigger pre-checks on agents |
| `/api/nodes/prechecks/summary` | POST | Pre-check summary (actions per node) |
| `/api/nodes/enroll` | POST | Generate enrollment token |

### Enrollment Tokens & Agent Download

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/enrollment-tokens` | GET | List tokens |
| `/api/enrollment-tokens/{token}/consume` | POST | Consume token, create node |
| `/api/agent/download?token=` | GET | Download agent binary |

### SignalR (implemented but disabled)

| Hub | Path | Status |
|-----|------|--------|
| Agent Hub | `/hubs/agent` | Implemented, push dispatch commented out |

## Project Structure

```
apps/
  orchestrator/backend/   — ASP.NET Core 10.0 REST API + SignalR hub (port 5000)
  orchestrator/web/       — React 18 + Vite + Tailwind + shadcn/ui
  agent/backend/          — Windows agent (HTTP polling + own Kestrel on port 5001)
shared/
  contracts/              — DTOs/contracts shared between orchestrator and agent
tests/
  orchestrator/unit/      — NUnit + Moq
  orchestrator/integration/
  agent/unit/
  agent/integration/
  contracts/              — Contract serialization tests
```

- Frontend builds **into** `apps/orchestrator/backend/wwwroot/` — served as embedded SPA
- SQLite DB file at `dist/deployment-poc.db` (production) or `artifacts/` (dev)
- Agent communicates via **HTTP polling** at `/api/workload-runs/pending`
- SignalR hub exists at `/hubs/agent` but is currently disabled

## Key Concepts

| Term | Description |
|------|-------------|
| **Workload Definition** | Named container for a workload (e.g. "Dev Tools Stack") |
| **Workload Revision** | Immutable versioned snapshot of packages and steps within a definition. Only one revision can be published at a time. |
| **Workload Run** | Dispatched execution of a revision against one or more nodes (`install` / `update` / `uninstall`) |
| **Node Workload State** | Tracks which revision is installed on a node and per-package status (`Current`, `Drifted`, `Unknown`) |
| **Pre-Check** | On-demand detection probe sent to agent's `/api/detect` endpoint to determine installed state before a run |
| **Diff Engine** | Computes delta between node's current packages and target revision: Added, Removed, Changed, Unchanged |
| **Detection Config** | Per-package specification for verifying installation: `registry`, `file`, or `version_manifest` |
| **Install Adapter** | Per-package specification for install/uninstall: type, command, args, upgrade behavior (`InPlace` / `UninstallFirst`), timeout |
| **Init Steps** | Shell commands (`preInitSteps`, `postInitSteps`, `preWorkloadSteps`, `postWorkloadSteps`, `preUninstallSteps`, `postUninstallSteps`) run via PowerShell or cmd |
| **Enrollment Token** | Short-lived, single-use token for node enrollment (default TTL 20 min) |

## Commands Cheat Sheet

```bash
# Dev
make run-orchestrator      # backend
make run-frontend          # frontend dev server
make run-agent             # agent

# Build
make build                 # debug build (fast)
make publish               # full self-contained publish to dist/
make clean                 # clean dist/

# Tests
dotnet test                # all .NET tests
cd apps/orchestrator/web && pnpm test   # frontend tests
cd apps/orchestrator/web && pnpm lint   # frontend lint
```

## Troubleshooting

### Frontend not loading in production build

Ensure `wwwroot` exists in the output directory:
```bash
cd apps/orchestrator/web && pnpm build
# Outputs to ../backend/wwwroot/
```

### Node shows Offline

1. Verify the agent process is running on the target node
2. Verify network connectivity between agent and orchestrator
3. Check that `agent.json` exists in `%LOCALAPPDATA%\DeploymentPoC\`
4. The heartbeat monitor marks nodes `Offline` if `LastSeenUtc` > 2 minutes (checked every 30 s)

### Run creation fails

1. Ensure at least one node is enrolled and **Online**
2. Ensure a **published** workload revision exists (draft revisions cannot be used)
3. Check that no other run is **Queued** or **Running** for the same node+workload combination

### Agent enrollment fails

1. Verify the token hasn't expired (check **Enrollment Tokens** page)
2. Verify the token hasn't already been used (single-use)
3. Ensure `--orchestrator-url` matches the orchestrator's actual URL

## Documentation

- PRD: `docs/prd-phase1.md`
- Implementation Tracker: `docs/implementation-tracker-phase1.md`
- Architecture: `docs/03-architecture-and-design.md`
- API Contracts: `docs/distributed-installer/10-core-contracts-pack.md`
- ADRs: `docs/adr/`
  - Uninstall pipeline: `docs/adr/0009-uninstall-pipeline-registry-resolution.md`
- Full MVP Plan: `MVP_Plan_PackageOrchestration.md`
