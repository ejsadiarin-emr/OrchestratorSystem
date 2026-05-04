# Manual Testing Guide

This guide walks through testing the DeploymentPoC system manually.

## Prerequisites

- .NET 8 SDK installed
- Node.js 20+ installed
- `make` available (or use the Makefile targets as reference for manual commands)

---

## 1. Build Verification

```bash
# Build the full .NET solution
make build

# Build the React frontend
make frontend
```

Or manually:
```bash
dotnet build DeploymentPoC.sln --configuration Release
cd orchestrator/web && npm install && npm run build
```

---

## 2. Start the Orchestrator

On the **Orchestrator server/node**:

```bash
make run-orchestrator
```

Or manually:
```bash
cd orchestrator/backend
dotnet run
```

The API will be available at `http://localhost:5000/`.

---

## 3. Test the Health Endpoint

```bash
curl http://localhost:5000/api/health
```

**Expected response:**
```json
{"status":"healthy"}
```

---

## 4. Test Enrollment Token Generation

On the **Orchestrator server/node** (or any machine that can reach it):

```bash
# Generate a new enrollment token
curl -X POST http://localhost:5000/api/enrollment/token
```

**Expected response:**
```json
{"token":"a1b2c3d4e5f6...","expiresAt":"2026-05-05T10:00:00Z"}
```

```bash
# List all tokens
curl http://localhost:5000/api/enrollment/tokens
```

---

## 5. Test Agent Enrollment

> **Important:** This step is performed on the **Agent node** (the Windows machine that will run the Agent service).

First, publish the Agent executable (or use `dotnet run` for testing):

```bash
# Option A: Use the published self-contained executable
make publish
./dist/Agent.exe --enroll <token> --url http://<orchestrator-host>:5000

# Option B: Run via dotnet (development mode)
cd agent/backend
dotnet run -- --enroll <token> --url http://<orchestrator-host>:5000
```

Replace:
- `<token>` with the token from Step 4
- `<orchestrator-host>` with the Orchestrator's IP or hostname

**Expected output:**
```
Enrollment successful. Agent ID: 1234567890abcdef...
Configuration written to: C:\...\agent.json
Service registration would occur here (requires admin privileges).
Run: sc create OrchestratorAgent binPath= "Agent.exe" start= auto
Run: sc start OrchestratorAgent
```

> **Note:** On Linux, service registration is skipped and instructions are printed. On Windows, you would run the `sc` commands as Administrator to register the service.

---

## 6. Test Agent APIs

On the **Orchestrator server/node** (or any machine that can reach it):

```bash
# List all registered agents
curl http://localhost:5000/api/agents

# Get a specific agent (replace <agentId>)
curl http://localhost:5000/api/agents/<agentId>

# Send a heartbeat (simulates agent polling)
curl -X POST http://localhost:5000/api/agents/<agentId>/heartbeat
```

---

## 7. Test Artifact Upload

On the **Orchestrator server/node** (or via the frontend/API client):

```bash
# Create a dummy installer file
echo "dummy installer" > /tmp/MyPackage_1.0.0.msi

# Single file upload
curl -X POST http://localhost:5000/api/artifacts/upload \
  -F "packageId=MyPackage" \
  -F "version=1.0.0" \
  -F "packageName=My Package" \
  -F "file=@/tmp/MyPackage_1.0.0.msi"

# Bulk import (filename format: PackageId_Version.ext)
curl -X POST http://localhost:5000/api/artifacts/import \
  -F "files=@/tmp/MyPackage_1.0.0.msi"

# List all artifacts
curl http://localhost:5000/api/artifacts
```

---

## 8. Test Workload Upload/Upsert

On the **Orchestrator server/node**:

```bash
# Create or update a workload definition
curl -X POST http://localhost:5000/api/workloads/upsert \
  -H "Content-Type: application/json" \
  -d '{
    "workloadId": "StandardDesktop",
    "workloadName": "Standard Desktop",
    "version": "1.0.0",
    "packages": [
      {
        "packageId": "MyPackage",
        "version": "1.0.0",
        "preInitSteps": ["echo pre-init"],
        "postInitSteps": ["echo post-init"]
      }
    ]
  }'

# List all workloads
curl http://localhost:5000/api/workloads

# Get a specific workload
curl http://localhost:5000/api/workloads/StandardDesktop/1.0.0
```

---

## 9. Test Workload Watcher

On the **Orchestrator server/node**:

The Orchestrator watches the `dist/workload-definitions/` directory for `.json` files. Drop a file there to test auto-import:

```bash
mkdir -p dist/workload-definitions

cat > dist/workload-definitions/AutoImport_2.0.0.json << 'EOF'
{
  "workloadId": "AutoImport",
  "workloadName": "Auto Imported Workload",
  "version": "2.0.0",
  "packages": [
    {
      "packageId": "MyPackage",
      "version": "1.0.0"
    }
  ]
}
EOF
```

Wait ~1 second, then verify:

```bash
curl http://localhost:5000/api/workloads
```

You should see the `AutoImport` workload automatically imported.

---

## 10. Test Agent Reset/Unregistration

On the **Agent node** (where `agent.json` exists):

```bash
# Option A: Use the published self-contained executable
./dist/Agent.exe --reset

# Option B: Run via dotnet (development mode)
cd agent/backend
dotnet run -- --reset
```

**Expected output:**
```
Successfully unregistered from Orchestrator.
Agent configuration deleted.
Service removal would occur here (requires admin privileges).
Run: sc stop OrchestratorAgent
Run: sc delete OrchestratorAgent
```

> **Note:** On Windows, run the `sc` commands as Administrator to fully remove the service.

---

## 11. Test the React Frontend

On the **Orchestrator server/node** (for development):

```bash
cd orchestrator/web
npm run dev
```

Open `http://localhost:5173/` in a browser. The sidebar shows navigation links to all pages. The Vite dev server proxies `/api/*` calls to `http://localhost:5000/`.

For production, the frontend is built into `orchestrator/backend/wwwroot/` and served by the ASP.NET Core app automatically.

---

## 12. Full Production Distribution Build

```bash
make dist
```

This produces:
```
dist/
├── Orchestrator.exe      # Self-contained single-file executable
├── Agent.exe             # Self-contained single-file executable
├── appsettings.json      # Orchestrator configuration
├── artifacts/            # Artifact storage directory
└── workload-definitions/ # Workload definition storage directory
```

Copy the `dist/` folder to your target Windows server(s) to deploy.

---

## Quick Reference: Makefile Targets

| Target | Description |
|--------|-------------|
| `make build` | Build .NET solution (Release) |
| `make frontend` | Build React frontend |
| `make publish` | Publish self-contained executables |
| `make dist` | Create distribution directory |
| `make run-orchestrator` | Run Orchestrator in dev mode |
| `make run-agent` | Run Agent in dev mode |
| `make clean` | Clean all build artifacts |
| `make help` | Show available targets |

---

## Troubleshooting

- **Port 5000 already in use:** Change the port in `orchestrator/backend/appsettings.json` under `WebHost:Port`.
- **SQLite database locked:** Ensure only one Orchestrator instance is running.
- **Agent enrollment fails:** Verify the token hasn't expired and the Orchestrator URL is reachable from the Agent node.
- **Frontend build fails:** Ensure Node.js 20+ is installed and `npm install` was run.
