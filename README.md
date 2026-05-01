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

### Build the Orchestrator

```bash
# Build frontend
cd apps/orchestrator/web
pnpm build

# Build self-contained backend
cd ../backend
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

**Output:** `apps/orchestrator/backend/bin/Release/net10.0/win-x64/DeploymentPoC.Orchestrator.exe`

The frontend is embedded in the backend's `wwwroot/` folder and served automatically.

### Build the Agent (per target node)

```bash
cd apps/agent/backend
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

**Output:** `apps/agent/backend/bin/Release/net10.0/win-x64/DeploymentPoC.Agent.exe`

### Run Production Builds

**Orchestrator:**
```bash
./apps/orchestrator/backend/bin/Release/net10.0/win-x64/DeploymentPoC.Orchestrator.exe
# Opens at http://localhost:5000
```

**Agent (on each target node):**
```powershell
.\apps\agent\backend\bin\Release\net10.0\win-x64\DeploymentPoC.Agent.exe
# Defaults to http://localhost:5001
# Override: --urls http://localhost:port
```

## Demo Flow

### Prerequisites
- Orchestrator running
- At least one target Windows node (physical or VM)
- An installer artifact (MSI or EXE)

### Step 1: Upload an artifact

The Artifact Store supports three upload modes: standalone media file, single-artifact zip, and bulk zip.

#### Upload Modes

##### Mode 1: Standalone Media File

Upload a raw installer (MSI or EXE) directly. The UI presents a form to fill in the manifest metadata manually.

##### Mode 2: Single-Artifact Zip

Package one installer media binary and its manifest in a zip. Both files must be at the **zip root** (no subdirectories). The installer and manifest must share the same base name:

```
my-artifact.zip
├── Git-2.48.1-64-bit.exe              ← installer media
└── Git-2.48.1-64-bit.manifest.json   ← manifest with same base name
```

Pairing is done by base name — `Git-2.48.1-64-bit.exe` pairs with `Git-2.48.1-64-bit.manifest.json`. If the base names don't match, the upload fails with a validation error.

##### Mode 3: Bulk Zip

Package multiple artifacts into one zip. Each artifact is a media file + manifest pair at the zip root, identified by shared base name:

```
artifacts-bulk.zip
├── Git-2.48.1-64-bit.exe
├── Git-2.48.1-64-bit.manifest.json
├── node-v24.13.0-x64.msi
├── node-v24.13.0-x64.manifest.json
├── 7z2600-x64.exe
└── 7z2600-x64.manifest.json
```

Each valid pair is ingested as a separate artifact. Invalid or unpaired files are reported as errors.

#### Manifest Format

The manifest (`.manifest.json`) is JSON with these fields:

```json
{
  "packageId": "git",
  "version": "2.48.1",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "Git-2.48.1-64-bit.exe",
    "arguments": "/VERYSILENT /NORESTART /NOCANCEL",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "version_manifest",
    "path": "git",
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

| Field | Required | Description |
|-------|----------|-------------|
| `packageId` | Yes | Unique identifier for the package (e.g., `git`, `nodejs`) |
| `version` | Yes | Semantic version string (e.g., `2.48.1`, `24.13.0`) |
| `channel` | No | `stable` (default), `canary`, or `test` |
| `artifactType` | No | `exe` or `msi`; auto-detected if omitted |
| `installAdapter.command` | Yes | Filename or command to execute (e.g., `Git-2.48.1-64-bit.exe`) |
| `installAdapter.type` | Yes | `exe` or `msi` |
| `installAdapter.arguments` | No | Install command-line arguments |
| `installAdapter.expectedExitCodes` | No | Valid exit codes (default: `[0]`) |
| `installAdapter.timeoutSeconds` | No | Install timeout (default: `300`) |
| `detection.type` | No | `version_manifest` (default), `file`, or `registry` |
| `detection.path` | No | Binary name, full file path, or registry path to check after install |
| `detection.expectedVersion` | No | Expected version string (e.g., `==2.48.1`, `>=3.13.0`) |
| `policyTags.riskLevel` | No | `low`, `medium`, `high`, or `critical` |
| `policyTags.approvalRequired` | No | Whether a human must approve before deployment |
| `policyTags.retryabilityClass` | No | `idempotent`, `non-idempotent`, or `conditional` |
| `policyTags.idempotencyMode` | No | `none`, `patch`, or `replace` |

#### Upload via UI

1. Navigate to **Artifact Store** in the sidebar
2. Choose an upload mode:
   - **Standalone**: drag-and-drop a raw installer (MSI/EXE), then fill in the manifest form
   - **Single zip**: drag-and-drop a zip containing one media + manifest pair
   - **Bulk zip**: drag-and-drop a zip containing multiple artifacts
3. For standalone uploads, the system auto-analyzes the installer and prefills metadata:
   - Package ID (defaults to filename)
   - Version
   - Channel (stable/canary/test)
   - Install adapter command
   - Detection type and path
   - Risk level
4. Click **Ingest Artifact** (or **Ingest All** for bulk)
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
   - Agent defaults to `http://localhost:5001` (avoids port collision with orchestrator on 5000/5124)
   - Override with `--urls http://localhost:port` if needed
5. Verify the node appears in the Nodes table with status **"Online"** (refreshes automatically every 5s)
6. The agent sends heartbeats every 15s; if stopped, node status changes to **"Offline"** within 30s

### Step 5: Run a workload

The system supports three run modes: **install**, **update**, and **uninstall**.

#### 5a. Fresh install (baseline)

1. Navigate to **Workload Runs**
2. Click **Create Run**
3. Select:
   - Workload (from Step 2)
   - Revision (published revision from Step 3)
   - Target nodes (enrolled node from Step 4)
   - Mode: **install**
4. Click **Create Run**
5. Watch the run progress:
   - Status cycles through: Queued → Running → Completed
   - Click the run to see step-by-step timeline
   - Every package in the revision is acquired and installed

#### 5b. Update (differential)

Create a new revision with changed packages, then run in **update** mode. The agent computes a diff and only touches changed packages.

**Example:** Using the test workload files:
- `test-workloads/workloads-older.json` — baseline with `git-2.47.1`, `nodejs-22.14.0`, `python-3.13.3`
- `test-workloads/workloads-newer.json` — updated with `git-2.48.1`, `nodejs-24.13.0`, `python-3.14.4` (all versions bumped)

1. Create a new revision (version `2.0.0`) with the updated packages
2. Publish the revision
3. Navigate to **Workload Runs** → **Create Run**
4. Select:
   - Workload
   - The new revision (`2.0.0`)
   - Target node
   - Mode: **update**
5. Click **Create Run**
6. Observe differential behavior in the agent logs:
   - **Unchanged packages are skipped entirely** (no download, no install)
   - **Changed packages** are uninstalled (old version) then installed (new version)
   - **Added packages** are installed
   - **Removed packages** are uninstalled

**Note:** The agent executes in two phases:
- Phase 1: Uninstall removed packages (reverse order)
- Phase 2: Install added and changed packages (normal order)

#### 5c. Uninstall

Uninstall removes all packages in a revision from the target nodes.

1. Navigate to **Workload Runs** → **Create Run**
2. Select:
   - Workload
   - Revision (published revision whose packages should be removed)
   - Target nodes
   - Mode: **uninstall**
3. Click **Create Run**
4. The agent uninstalls every package in the revision from the node in reverse order

### Step 6: Observe execution (agent-side)

On the target node, the agent:
1. Receives the workload assignment via SignalR
2. For **install** mode: downloads and installs every package in the revision
3. For **update** and **uninstall** modes:
   - Compares the target revision against the node's `CurrentPackages` (from the last completed run)
   - Computes diff: added, removed, changed, unchanged
   - Skips unchanged packages entirely
   - Uninstalls removed packages first (reverse order)
   - Then installs added/changed packages (normal order)
4. Reports `StepStatus` after each step to the orchestrator
5. Sends `Complete` on success or `Fail` on error
6. The orchestrator updates `CurrentRevisionId` only on `Complete` (not on `AckClaim` or failure)
7. On disconnect, node status auto-changes to **"Offline"** within 30s (heartbeat monitor)

### Bulk Import for Testing

Two test workload files are included for quick manual testing:

```bash
# Import baseline workload
curl -X POST http://localhost:5124/api/workloads/bulk \
  -H "Content-Type: application/json" \
  -d @test-workloads/workloads-older.json

# Import updated workload (creates new revisions with bumped versions)
curl -X POST http://localhost:5124/api/workloads/bulk \
  -H "Content-Type: application/json" \
  -d @test-workloads/workloads-newer.json
```

After import, publish each workload's revision, then create runs in `install`, `update`, and `uninstall` modes to observe differential behavior.

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
| `/hubs/agent` | SignalR | Agent runtime hub (identify, send messages, heartbeats) |

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
