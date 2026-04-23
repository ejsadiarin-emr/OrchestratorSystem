# DeploymentPoC

Proof-of-Concept for an enterprise deployment orchestration system. The system enables operators to upload installer artifacts, define workload packages, enroll target nodes via secure tokens, and execute orchestrated deployment workloads across a fleet.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Orchestrator (ASP.NET Core)              │
│  ┌─────────┐  ┌──────────┐  ┌──────────┐  ┌───────────┐  │
│  │Artifacts│  │ Workloads │  │  Runs    │  │  Nodes    │  │
│  │Controller│ │Controller│ │Controller │ │ Controller│  │
│  └─────────┘  └──────────┘  └──────────┘  └───────────┘  │
│        ↓            ↓             ↓              ↓          │
│  ┌─────────────────────────────────────────────────────┐  │
│  │           SQLite (EF Core) + File System              │  │
│  └─────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                              ↕ (SignalR)
┌─────────────────────────────────────────────────────────────┐
│                  Windows Agent (per node)                   │
│  - Pulls work via SignalR                                   │
│  - Executes workload packages in sequence                  │
│  - Reports step status back to orchestrator                │
└─────────────────────────────────────────────────────────────┘
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

### 1. Clone and install dependencies

```bash
# Backend
cd apps/orchestrator/backend
dotnet restore

# Frontend
cd ../web
pnpm install
```

### 2. Run the orchestrator (development mode)

**Option A: Direct dotnet run**
```bash
cd apps/orchestrator/backend
dotnet run
# Opens at http://localhost:5124
```

**Option B: With frontend hot reload**
```bash
# Terminal 1: Backend
cd apps/orchestrator/backend
dotnet run

# Terminal 2: Frontend  
cd apps/orchestrator/web
pnpm dev
# Frontend proxies to backend at :5124
```

### 3. Verify the application

- Open http://localhost:5124
- Swagger UI: http://localhost:5124/swagger

## Production Build

### Build the application

```bash
# Build frontend
cd apps/orchestrator/web
pnpm build

# Build self-contained backend
cd ../backend
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Output

The published executable and embedded static files are in:
```
apps/orchestrator/backend/bin/Release/net10.0/win-x64/
```

The frontend is embedded in the backend's `wwwroot/` folder and served automatically.

### Run production build

```bash
./apps/orchestrator/backend/bin/Release/net10.0/win-x64/DeploymentPoC.Orchestrator.exe
# Opens at http://localhost:5000
```

## Demo Flow

### Prerequisites
- Orchestrator running
- At least one target Windows node (physical or VM)
- An installer artifact (MSI or EXE)

### Step 1: Upload an artifact

1. Navigate to **Artifact Store** in the sidebar
2. Drag-and-drop an installer file (MSI/EXE) or click to browse
3. The system auto-analyzes the installer and prefills metadata:
   - Package ID (defaults to filename)
   - Version
   - Channel (stable/canary/test)
   - Install adapter command
   - Detection type and path
   - Risk level
4. Click **Ingest Artifact**
5. Verify the artifact appears in the Artifact Store table

### Artifact Storage

Artifacts are stored in the `artifacts/` directory (relative to the running executable):

```
artifacts/
└── {packageId}/
    └── {version}/
        ├── artifact.bin      # the installer media (MSI/EXE)
        └── resolved-manifest.json  # metadata JSON
```

To view stored artifacts:

```bash
# Production path (relative to executable)
ls -la apps/orchestrator/backend/bin/Release/net10.0/win-x64/artifacts/

# View manifest for a specific artifact
cat -la apps/orchestrator/backend/bin/Release/net10.0/win-x64/artifacts/{packageId}/{version}/resolved-manifest.json
```

**Configuration:** Set `ArtifactStore:RootPath` in `appsettings.json` to customize the storage location.

**Production path:** The `artifacts/` folder is created next to the running executable (e.g., `bin/Release/net10.0/win-x64/artifacts/`).

<nitpicks>
- IMPORTANT features/functions for artifact ingestion: 
    - artifact uploading should support ZIP-FILE OR TARBALL UPLOADS as first-class (inside this zip is the artifact installer media binary AND manifest/metadata JSON file) 
    - should also support BULK UPLOADS via zip/tarball files (ex. zip contains 5 artifacts, 1 artifact = artifact installer media binary AND manifest/metadata JSON file - with maybe same name) 
    - retain installer-media only upload
    - UI-wise: can update frontend UI for this to inform users about the options for ingestion/artifact upload (bulk via zip, individual via zip, standalone installer media binary like .exe)
- should show 3 demo flows here: small and big artifact uploads (prioritize zip files for demo)
    - for big - main goal is to show "uploading..." progress clearly
    - for small - for faster demo purposes
    - bulk upload - via zip (contain maybe 3-5 artifacts)
- can improve UI:
    - better ingest timeline progress showing UI (since this is shown every artifact ingest right?)
    - instead of table we can to cards for showing the artifacts in "Artifact Inventory"
</nitpicks>

### Step 2: Create a workload definition

1. Navigate to **Workloads** in the sidebar
2. Click **Create Workload**
3. Enter:
   - Name (e.g., `ej-server-v2`)
   - Description
4. Click **Create**
5. Verify the workload appears in the Workloads table

### Step 3: Create a workload revision

1. Click **+ New Revision** on the workload
2. Enter version (e.g., `1.0.0`)
3. Add packages from the artifact list:
   - Select the artifact uploaded in Step 1
   - The package appears as a step with install adapter, detection, etc.
4. Click **Save Draft** (or **Publish** to make it the active revision)

### Step 4: Enroll a node

1. Navigate to **Nodes** → **Enrollment Tokens**
2. Click **Generate Token**
3. Copy the enrollment token
4. On the target node, run the agent with the token:
   ```powershell
   .\agent.exe --enroll <token> --orchestrator-url http://<orchestrator-host>:5124
   ```
5. Verify the node appears in the Nodes table with status "Online"

### Step 5: Run a workload

1. Navigate to **Workload Runs**
2. Click **Create Run**
3. Select:
   - Workload (from Step 2)
   - Revision (published revision from Step 3)
   - Target nodes (enrolled node from Step 4)
   - Mode (install/upgrade/rollback)
4. Click **Create Run**
5. Watch the run progress in the Workload Runs table:
   - Status cycles through: Queued → Running → Completed
   - Click the run to see step-by-step timeline

### Step 6: Observe execution (agent-side)

On the target node, the agent:
1. Receives the workload assignment via SignalR
2. Downloads each artifact from the orchestrator
3. Executes packages in sequence
4. Reports step status back to the orchestrator
5. Completes or fails based on exit codes

## API Reference

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/artifacts` | POST | Upload artifact |
| `/api/artifacts` | GET | List artifacts |
| `/api/workloads` | GET | List workloads |
| `/api/workloads` | POST | Create workload |
| `/api/workloads/{id}/revisions` | POST | Create revision |
| `/api/workloads/{id}/publish` | POST | Publish revision |
| `/api/workload-runs` | POST | Create run |
| `/api/workload-runs` | GET | List runs |
| `/api/workload-runs/{id}/steps` | GET | Get run steps |
| `/api/nodes` | GET | List nodes |
| `/api/nodes/enroll` | POST | Issue enrollment token |
| `/api/nodes/{id}/heartbeat` | POST | Node heartbeat |

## Project Structure

```
apps/orchestrator/
├── backend/                    # ASP.NET Core API
│   ├── Controllers/            # API endpoints
│   ├── Services/              # Business logic
│   ├── Data/                  # EF Core DbContext, entities
│   ├── Contracts/             # DTOs
│   └── Program.cs             # Entry point
└── web/                        # React frontend
    ├── src/
    │   ├── pages/             # Route pages
    │   ├── components/       # Shared components
    │   ├── services/         # API client
    │   └── types.ts          # TypeScript types
    └── index.html            # Entry point
```

## Key Files

- `apps/orchestrator/backend/Program.cs` — App configuration
- `apps/orchestrator/web/src/services/api.ts` — Frontend API client
- `apps/orchestrator/web/src/pages/Dashboard.tsx` — Main dashboard
- `apps/orchestrator/web/src/pages/Workloads.tsx` — Workload management

## Troubleshooting

### Frontend not loading in production build

Ensure `wwwroot` is in the output directory. The build step copies frontend assets:
```bash
cd apps/orchestrator/web && pnpm build
# Outputs to ../backend/wwwroot/
```

### Node not connecting

1. Verify network connectivity to orchestrator port
2. Check the enrollment token hasn't expired
3. Ensure the agent version is compatible

### Run creation fails

1. Ensure at least one node is enrolled and online
2. Ensure a published workload revision exists
3. Check the orchestrator logs for validation errors

## Documentation

- PRD: `docs/prd-phase1.md`
- Implementation Tracker: `docs/implementation-tracker-phase1.md`
- Architecture: `docs/03-architecture-and-design.md`
- API Contracts: `docs/distributed-installer/10-core-contracts-pack.md`
