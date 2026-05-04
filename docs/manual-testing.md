# Manual Testing Guide

This guide covers testing the DeploymentPoC system. The primary workflow is to **build on WSL/Linux and run the self-contained Windows executables on a Windows machine**.

## Prerequisites

### Build Machine (WSL/Linux)
- .NET 10 SDK installed
- Node.js 20+ installed
- `make` available

### Target Machine (Windows)
- Windows 10/11 or Windows Server 2019+
- No .NET runtime required (binaries are self-contained)
- PowerShell

---

## Production Workflow: Build on WSL, Run on Windows

### Step 1: Build Production Binaries

On your **WSL/Linux build machine**:

```bash
make dist
```

This produces the `dist/` directory:

```
dist/
├── Orchestrator.exe      # Self-contained single-file executable
├── Agent.exe             # Self-contained single-file executable
├── appsettings.json      # Orchestrator configuration
├── artifacts/            # Artifact storage directory
└── workload-definitions/ # Workload definition storage directory
```

> **Note:** .NET can cross-compile `win-x64` binaries from Linux/WSL. The executables include the .NET runtime and all native libraries.

---

### Step 2: Copy Binaries to Windows

From WSL, copy the `dist/` folder to a Windows-accessible location:

```bash
# Option A: Copy to a Windows drive mounted in WSL
cp -r dist/ /mnt/c/temp/deployment-poc/

# Option B: Copy to a network share
cp -r dist/ //your-windows-server/share/deployment-poc/
```

On the **Windows target machine**, open PowerShell:

```powershell
# Verify the files are there
ls C:\temp\deployment-poc\

# Output:
#     Directory: C:\temp\deployment-poc
# Mode                 LastWriteTime         Length Name
# ----                 -------------         ------ ----
# d-----          5/4/2026   9:00 AM                artifacts
# d-----          5/4/2026   9:00 AM                workload-definitions
# -a----          5/4/2026   9:00 AM       45000000 Orchestrator.exe
# -a----          5/4/2026   9:00 AM       25000000 Agent.exe
# -a----          5/4/2026   9:00 AM           1200 appsettings.json
```

---

### Step 3: Start the Orchestrator

On the **Windows Orchestrator server**, open PowerShell as Administrator (recommended for service features):

```powershell
cd C:\temp\deployment-poc

# Start the Orchestrator API
.\Orchestrator.exe
```

The API will be available at `http://localhost:5000/`.

To run on a different port, edit `appsettings.json` before starting:

```powershell
# View current config
Get-Content appsettings.json | ConvertFrom-Json | Select-Object -ExpandProperty WebHost

# Edit with your preferred editor (notepad, vscode, etc.)
notepad appsettings.json
```

---

### Step 4: Verify Orchestrator Health

From any machine that can reach the Orchestrator:

```powershell
# PowerShell
Invoke-RestMethod -Uri "http://localhost:5000/api/health"

# Or using curl (if available)
curl http://localhost:5000/api/health
```

**Expected:** `{"status":"healthy"}`

---

### Step 5: Generate an Enrollment Token

From any machine that can reach the Orchestrator:

```powershell
# PowerShell
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/enrollment/token" -Method Post
$response

# Expected output:
# token      : a1b2c3d4e5f6...
# expiresAt  : 2026-05-05T10:00:00Z
```

Save the token value for the next step.

---

### Step 6: Enroll the Agent

On the **Windows Agent node** (the machine that will run the Agent service), open PowerShell as Administrator:

```powershell
cd C:\temp\deployment-poc

# Enroll the agent
.\Agent.exe --enroll <token> --url http://<orchestrator-host>:5000
```

Replace:
- `<token>` with the token from Step 5
- `<orchestrator-host>` with the Orchestrator's IP or hostname (use `localhost` if same machine)

**Expected output:**
```
Enrollment successful. Agent ID: 1234567890abcdef...
Configuration written to: C:\temp\deployment-poc\agent.json
Service registration would occur here (requires admin privileges).
Run: sc.exe create OrchestratorAgent binPath= "C:\temp\deployment-poc\Agent.exe" start= auto
Run: sc.exe start OrchestratorAgent
```

Since you're already running as Administrator, register the service:

```powershell
# Register the Agent as a Windows Service
sc.exe create OrchestratorAgent binPath= "C:\temp\deployment-poc\Agent.exe" start= auto
cd C:\temp\deployment-poc
sc.exe start OrchestratorAgent
```

Verify the service is running:

```powershell
Get-Service OrchestratorAgent
```

> **Note:** The `agent.json` file is created in the same directory as `Agent.exe` and contains the Agent's credentials. Keep it secure.

---

### Step 7: Verify Agent Registration

From any machine that can reach the Orchestrator:

```powershell
# PowerShell
Invoke-RestMethod -Uri "http://localhost:5000/api/agents"

# Or curl
curl http://localhost:5000/api/agents
```

You should see the newly enrolled agent with status `REGISTERED`.

---

### Step 8: Upload Artifacts

From any machine that can reach the Orchestrator:

```powershell
# PowerShell - single upload
Invoke-RestMethod -Uri "http://localhost:5000/api/artifacts/upload" -Method Post -Form @{
    packageId = "MyPackage"
    version = "1.0.0"
    packageName = "My Package"
    file = Get-Item "C:\temp\MyPackage_1.0.0.msi"
}

# PowerShell - bulk import (filename format: PackageId_Version.ext)
Invoke-RestMethod -Uri "http://localhost:5000/api/artifacts/import" -Method Post -Form @{
    files = Get-Item "C:\temp\MyPackage_1.0.0.msi"
}

# List artifacts
Invoke-RestMethod -Uri "http://localhost:5000/api/artifacts"
```

Or using `curl` on WSL/Linux:

```bash
curl -X POST http://localhost:5000/api/artifacts/upload \
  -F "packageId=MyPackage" \
  -F "version=1.0.0" \
  -F "packageName=My Package" \
  -F "file=@/path/to/MyPackage_1.0.0.msi"

curl http://localhost:5000/api/artifacts
```

---

### Step 9: Upload a Workload

From any machine that can reach the Orchestrator:

```bash
# curl (works on WSL, Git Bash, or anywhere)
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
```

Or PowerShell:

```powershell
$body = @{
    workloadId = "StandardDesktop"
    workloadName = "Standard Desktop"
    version = "1.0.0"
    packages = @(
        @{
            packageId = "MyPackage"
            version = "1.0.0"
            preInitSteps = @("echo pre-init")
            postInitSteps = @("echo post-init")
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri "http://localhost:5000/api/workloads/upsert" -Method Post -Body $body -ContentType "application/json"
```

---

### Step 10: Test Workload Watcher

On the **Windows Orchestrator server**, create a workload definition file in the watched directory:

```powershell
cd C:\temp\deployment-poc

$json = @'
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
'@

$json | Out-File -FilePath "workload-definitions\AutoImport_2.0.0.json" -Encoding utf8
```

Wait ~1 second, then verify it was auto-imported:

```powershell
Invoke-RestMethod -Uri "http://localhost:5000/api/workloads"
```

You should see the `AutoImport` workload in the list.

---

### Step 11: Reset/Unregister the Agent

On the **Windows Agent node**, open PowerShell as Administrator:

```powershell
cd C:\temp\deployment-poc

# Unregister the agent
.\Agent.exe --reset
```

**Expected output:**
```
Successfully unregistered from Orchestrator.
Agent configuration deleted.
Service removal would occur here (requires admin privileges).
Run: sc stop OrchestratorAgent
Run: sc delete OrchestratorAgent
```

Complete the cleanup:

```powershell
# Stop and remove the service
sc.exe stop OrchestratorAgent
sc.exe delete OrchestratorAgent
```

Verify the agent is gone from the Orchestrator:

```powershell
Invoke-RestMethod -Uri "http://localhost:5000/api/agents"
```

---

## Development Workflow (dotnet run)

For rapid iteration during development, you can run directly via `dotnet run` instead of using published binaries.

### Start Orchestrator in Dev Mode

```bash
make run-dev
# or
cd orchestrator/backend && dotnet run
```

### Start Frontend Dev Server

```bash
make run-frontend
# or
cd orchestrator/web && npm run dev
```

The dev server runs at `http://localhost:5173/` and proxies `/api/*` to `http://localhost:5000/`.

---

## Quick Reference: Makefile Targets

| Target | Description |
|--------|-------------|
| `make publish` | Build self-contained win-x64 executables |
| `make dist` | Package into `dist/` for copying to Windows |
| `make build` | Build .NET solution (Release, not self-contained) |
| `make frontend` | Build React frontend |
| `make run-dev` | Run Orchestrator in dev mode |
| `make run-frontend` | Run React dev server |
| `make clean` | Clean all build artifacts |
| `make help` | Show available targets |

---

## Troubleshooting

### WSL Build Issues

- **`make dist` fails with "dotnet not found"**: Ensure .NET 10 SDK is installed in WSL (`sudo apt install dotnet-sdk-10.0` on Ubuntu)
- **Permission denied on `dist/`**: Run `chmod +x dist/*.exe` if copying back to WSL (not needed for Windows)

### Windows Runtime Issues

- **"Windows cannot run this app"**: Ensure you're on a 64-bit Windows system (the binaries target `win-x64`)
- **Orchestrator won't start (port 5000 in use)**: Edit `appsettings.json` and change `WebHost:Port`
- **Agent enrollment fails**: Verify the token hasn't expired and the Orchestrator URL is reachable from the Agent node
- **Service registration fails**: Ensure PowerShell is running as Administrator

### Cross-Platform Notes

- The `Orchestrator.exe` and `Agent.exe` binaries are **pure Windows executables**. They cannot run on Linux.
- However, they can be **built on Linux/WSL** because .NET supports cross-platform compilation.
- The SQLite database (`orchestrator.db`) is created in the working directory where `Orchestrator.exe` runs.
- Artifacts are stored in the `artifacts/` folder relative to the working directory.

---

## Architecture Summary

```
┌─────────────────┐         ┌─────────────────┐         ┌─────────────────┐
│   WSL/Linux     │         │  Windows Server │         │  Windows Agent  │
│   (Build)       │         │  (Orchestrator) │         │  (Agent Node)   │
│                 │         │                 │         │                 │
│  make dist      │ ──────> │  Orchestrator.exe        │  Agent.exe      │
│                 │  copy   │  ├─ SQLite DB            │  ├─ agent.json  │
│                 │  dist/  │  ├─ artifacts/           │  └─ Windows     │
│                 │         │  └─ wwwroot/ (React)     │     Service     │
└─────────────────┘         └─────────────────┘         └─────────────────┘
                                    ^                             │
                                    │      HTTP API calls         │
                                    └─────────────────────────────┘
```
