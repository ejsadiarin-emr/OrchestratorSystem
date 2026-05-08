# DeploymentPoC (Bird's Eye View)

---

## Overview

A Windows-only package orchestration system composed of two components:

- **Orchestrator** — Locally hosted by system admins. Manages packages, workload definitions (with versioned revisions), agent nodes, and dispatches workload runs. Exposes a React Web UI backed by an ASP.NET Core 10.0 API (Kestrel, port 5000) with a SQLite database and a local on-disk artifact store. Also serves the agent binary for download.
- **Agent (Node)** — A background service installed on remote Windows machines. Polls the Orchestrator for pending workload runs and executes them locally (install/update/uninstall/detect). Reports results back step-by-step via the Orchestrator API. Also exposes a `/api/detect` endpoint for on-demand pre-checks.

Both components share a `DeploymentPoC.Contracts` library that defines the message types used between them.

---

## 1. Architecture & Tech Stack

| Layer | Technology | Details |
|---|---|---|
| Orchestrator Backend | ASP.NET Core 10.0 | Kestrel HTTP server, port 5000 |
| Orchestrator Frontend | React 18 + Vite + Tailwind v4 + shadcn/ui | Built into `wwwroot/`, served as embedded SPA |
| Database | SQLite via EF Core | DB file at `dist/deployment-poc.db` (prod) or `artifacts/` (dev) |
| Agent Backend | .NET 10.0 self-contained single-file exe | `UseWindowsService()` / `UseSystemd()`, own Kestrel on port 5001 |
| Shared Contracts | `DeploymentPoC.Contracts` class library | DTOs shared between orchestrator and agent |
| Agent ↔ Orchestrator | HTTP polling (primary) | `GET /api/workload-runs/pending?agent_id=` every 10s |
| Agent ↔ Orchestrator | SignalR hub (exists but disabled) | `/hubs/agent` — push dispatch commented out in MVP |
| Orchestrator → Agent probe | Direct HTTP POST | `http://{nodeIp}:5001/api/detect` for pre-checks |
| Artifact Transfer | HTTP download | Agent downloads binaries from `/api/artifacts/{id}/download` |
| Test Frameworks | NUnit + Moq (orchestrator), NUnit (agent), Vitest + testing-library (frontend) | |
| Build & Publish | `make publish` | Kills processes, builds frontend, publishes both exes to `dist/` |
| Agent CLI | `System.CommandLine`-style args | `--enroll`, `--orchestrator-url`, `--display-name`, `--reset` |

---

## 2. Language

### Core Components

| Term | Definition |
|---|---|
| **Orchestrator** | The centrally hosted ASP.NET Core web application that manages packages, workload definitions, agent nodes, and dispatches workload runs. Exposes a REST API and React UI. |
| **Agent (Node)** | A .NET background service installed on remote Windows machines that polls for pending workload runs, executes them locally, and reports results back to the Orchestrator. |

### Workload Concepts

| Term | Definition |
|---|---|
| **Workload Definition** | A named, versioned collection of packages. The top-level entity (`WorkloadDefinitionEntity`) holds a name, description, and a reference to its published revision. |
| **Workload Revision** | A specific version of a workload — the actual package list, init steps, and shell configuration. Workload definitions can have multiple revisions; one can be published as the active version. |
| **Workload Run** | A dispatched execution of a workload against one or more agent nodes. Created with a mode (`install`, `update`, `uninstall`) and state transitions from `Queued` → `Running` → `Completed`/`Failed`/`Cancelled`. |
| **Workload Run Timeline** | A chronological log of step-level events within a workload run — detection results, init step outputs, install outcomes, and finalization — stored in `WorkloadRunTimelineEntity`. |

### Package & Artifact Concepts

| Term | Definition |
|---|---|
| **Package** | A single installable artifact tracked in the system. A `PackageEntity` record stores the artifact metadata (name, version, install adapter, detection config, upgrade behavior) and is linked to a stored binary file. |
| **Artifact** | A binary file (`.exe`, `.msi`) paired with an ingest manifest JSON describing how to install and detect it. Artifacts are stored on disk and paired by filename stem (e.g., `nodejs-22.14.0.msi` + `nodejs-22.14.0.manifest.json`). |
| **Artifact Ingest Manifest** | A JSON file describing an artifact's install adapter, detection config, and policy tags. Uploaded alongside (or embedded within) the binary to create a `PackageEntity`. |

### Node & State Concepts

| Term | Definition |
|---|---|
| **Node** | A registered agent machine tracked by the Orchestrator. Each node has a GUID identity, hostname, IP address, and an `Online`/`Offline` status derived from heartbeat activity. |
| **Node Workload State** | Tracks which workload revision a node has installed and the per-package status (`Current`, `Drifted`, `Unknown`). Updated after pre-checks and run completion. |
| **Pre-Check** | An on-demand detection probe sent to an agent's `/api/detect` endpoint to determine which packages are installed and at what versions, used to compute a delta before dispatching a run. |
| **Delta** | The computed difference between a node's current packages and a target workload revision — identifies packages to add, remove, change, or skip. Used in update mode and pre-check summaries. |

### Configuration & Execution Concepts

| Term | Definition |
|---|---|
| **Enrollment Token** | A short-lived, single-use token generated by the Orchestrator that an agent consumes to register itself. TTL is configurable (1–120 minutes, default 20). |
| **Detection Config** | Per-package specification of how to verify installation — type (`file`, `registry`, `version_manifest`), path, and optional expected version. Used by the agent's `PackageDetector`. |
| **Install Adapter** | Per-package specification of how to install/uninstall — type (`exe`, `msi`), command, arguments, uninstall command/args, upgrade behavior (`InPlace` / `UninstallFirst`), expected exit codes, and timeout. |
| **Init Steps** | Arbitrary shell commands executed before or after package install (`preInitSteps` / `postInitSteps`), before/after the entire workload (`preWorkloadSteps` / `postWorkloadSteps`), or before/after uninstall (`preUninstallSteps` / `postUninstallSteps`). Default shell is PowerShell. |
| **Upgrade Behavior** | Per-package: `"InPlace"` (run installer over existing) or `"UninstallFirst"` (uninstall old, then install new). |
| **Chunked Upload** | A three-step upload flow (create session, upload chunks, complete) for large artifact files, supporting resumable uploads up to 2GB. |

---

## 3. Project Structure & Deployment

```
apps/
  orchestrator/backend/   — ASP.NET Core 10.0 REST API + SignalR hub (port 5000)
  orchestrator/web/       — React 18 + Vite + Tailwind + shadcn/ui
  agent/backend/          — Windows agent (HTTP polling + own Kestrel server on port 5001)
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
- Agent communicates via **HTTP polling** at `/api/workload-runs/pending` (SignalR hub exists at `/hubs/agent` but is currently **disabled** — push dispatch is commented out)

Both components are separate C# .NET 10.0 projects published as fully self-contained Windows executables — all dependencies and native libraries bundled into a single `.exe`. No runtime installation required on either machine.

### Publish Command (both projects)

```bash
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:EnableCompressionInSingleFile=true
```

| Flag | Purpose |
|---|---|
| `-r win-x64` | Target Windows 64-bit runtime |
| `--self-contained true` | Bundle the .NET runtime itself into the output |
| `PublishSingleFile=true` | Merge all assemblies into one `.exe` |
| `IncludeNativeLibrariesForSelfExtract=true` | Bundle native `.dll`s (e.g., SQLite interop) into the single file |
| `EnableCompressionInSingleFile=true` | Compress bundled content to reduce file size |

### Orchestrator (`DeploymentPoC.Orchestrator.csproj`)
- ASP.NET Core 10.0 web application — runs an embedded Kestrel HTTP server on `http://0.0.0.0:5000`
- Serves the React UI as embedded static files (built into `wwwroot/` at publish time)
- Hosts the REST API consumed by both the React UI and Agent nodes
- SignalR hub at `/hubs/agent` (implemented but currently disabled — agents use HTTP polling)
- Deployed and run manually by system admins on a local/internal Windows machine

### Agent (`DeploymentPoC.Agent.csproj`)
- .NET 10.0 project with `Microsoft.Extensions.Hosting.WindowsServices` — runs as a Windows Service via `UseWindowsService()` on Windows, or systemd via `UseSystemd()` on Linux
- Always run with elevated privileges (as Administrator) — assumed for all operations
- Runs its own Kestrel HTTP server on `http://0.0.0.0:5001` (provides `/api/detect` and `/health` endpoints)
- Stateless: all persistent state lives in the Orchestrator DB; Agent stores only its identity in `agent.json`
- **PowerShell is available as a default shell for init steps.** The `DefaultShell` field on workload revisions controls whether `cmd.exe /c` or PowerShell is used. Default is `"powershell"`. Detection and install/uninstall operations use `System.Diagnostics.Process` directly.
- **Init steps** (`preInitSteps`, `postInitSteps`, `preWorkloadSteps`, `postWorkloadSteps`, `preUninstallSteps`, `postUninstallSteps`) run via `System.Diagnostics.Process` with the configured shell (`cmd` uses `/C`, PowerShell uses `-Command`), with environment variables injected for run context.

---

## 4. Configuration

### Orchestrator `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "InstallerDb": "Data Source=deployment-poc.db"
  },
  "ArtifactStore": {
    "RootPath": "artifacts"
  },
  "AgentDownload": {
    "AgentExePath": "agent.exe"
  }
}
```

| Key | Purpose | Default/Fallback |
|---|---|---|
| `ConnectionStrings:InstallerDb` | SQLite connection string. Falls back to `{distRoot}/deployment-poc.db` at runtime. | `Data Source=deployment-poc.db` |
| `ArtifactStore:RootPath` | Local disk path where uploaded binaries and manifests are stored. Falls back to `{distRoot}/artifacts`. | `artifacts` |
| `AgentDownload:AgentExePath` | Path to `agent.exe` served at `/api/agent/download`. Falls back to `{distRoot}/agent.exe`. | `agent.exe` |
| `Cors:AllowedOrigins` | Array of allowed CORS origins. If empty, AllowAnyOrigin is used. | Empty (AllowAnyOrigin) |
| `AgentProbeTimeoutSeconds` | Timeout for probing agent `/api/detect` endpoint during pre-checks. | 30 |

> Runtime path resolution: The orchestrator resolves paths at startup using `ProcessModule.FileName` to find the dist root and sets absolute paths for the artifact store and agent download.

### Agent `agent.json`

Written by `DeploymentPoC.Agent.exe --enroll`. Deleted by `DeploymentPoC.Agent.exe --reset`. Not manually edited.

```json
{
  "NodeId": "guid-generated-by-orchestrator",
  "OrchestratorUrl": "http://192.168.1.10:5000"
}
```

Stored in:
- Windows: `%LOCALAPPDATA%\DeploymentPoC\agent.json`
- Linux primary: `/var/lib/deploymentpoc/agent.json`
- Linux fallback: `$HOME/.config/deploymentpoc/agent.json`

> Unlike the original plan, `agent.json` contains only `NodeId` (a GUID, not a separate agentSecret) and `OrchestratorUrl`. Authentication uses the NodeId as the `agent_id` query parameter. There is no separate `agentSecret` or `pollingIntervalSeconds` in the config — the agent polls every **10 seconds** (hard-coded in `AgentRuntimeService`).

### Agent `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Orchestrator": {
    "BaseUrl": "http://localhost:5000"
  },
  "Agent": {
    "ChunkSizeBytes": 2097152,
    "UseChunkedDownload": true
  }
}
```

| Key | Purpose | Default |
|---|---|---|
| `Orchestrator:BaseUrl` | Orchestrator base URL (overridden at enrollment) | `http://localhost:5000` |
| `Agent:ChunkSizeBytes` | Chunk size for artifact downloads | 2097152 (2 MB) |
| `Agent:UseChunkedDownload` | Whether to use chunked HTTP download for artifacts | true |
| `Agent:PipelineTimeoutMinutes` | Timeout for the entire workload run pipeline | 30 |
| `Agent:HeartbeatIntervalSeconds` | SignalR heartbeat interval (unused while SignalR is disabled) | 15 |

---

## 5. Agent Enrollment & Registration

### Overview

Agents connect to the Orchestrator via a token-based enrollment flow — a single command run by the user on the Agent machine. The enrollment token and the ongoing authentication are **separate**:
- **Enrollment token** — short-lived, single-use. Used exactly once to register the Agent (consume the token).
- **NodeId (GUID)** — returned at enrollment. Used as the `agent_id` query parameter on all subsequent API calls. Not a secret — it's a database key, not a bearer token.

> **Difference from original plan:** There is no `agentSecret` bearer token. Authentication is by `agent_id` query parameter containing the NodeId GUID. The original plan described `Authorization: Bearer <agentSecret>` — this does not exist in the implementation.

---

### Enrollment Token Generation

Tokens are generated via the Orchestrator API (not a separate enrollment-specific UI in MVP):

```
POST /api/nodes/enroll
Body: { "requestedBy": "admin-name", "orchestratorUrl": "http://host:5000", "ttlMinutes": 120 }
Response: 201 { "token": "abc123...", "issuedAt": "...", "expiresAt": "...", "requestedBy": "...", "orchestratorUrl": "...", "singleUse": true, "used": false }
```

Token properties:
- `requestedBy` — who generated the token
- `orchestratorUrl` — the URL where the agent should connect (returned to the agent during enrollment)
- `singleUse` — always `true`; invalidated immediately after a successful consume call
- `ttlMinutes` — configurable (1–120 minutes). Default in the API is 20 minutes (not 24 hours)
- Stored in the `EnrollmentTokens` table with `Used`, `ConsumedAtUtc`, and `ExpiresAtUtc` fields

Tokens can also be listed:
```
GET /api/enrollment-tokens
Response: 200 [ { token, issuedAt, expiresAt, requestedBy, orchestratorUrl, singleUse, used }, ... ]
```

---

### Single-Command Enrollment

**The user runs exactly one command:**

```
DeploymentPoC.Agent.exe --enroll <token> --orchestrator-url <url> [--display-name <name>]
```

Internally, this performs the full setup sequence:

```
1. POST /api/enrollment-tokens/{token}/consume
   Body: { hostname, displayName?, ipAddress: "", osVersion, agentVersion }
   -> On success: receive { "id": "<guid>" }
   -> On 410: token expired, on 409: token already consumed, on 404: token not found
2. Write agent.json to %LOCALAPPDATA%\DeploymentPoC\ with { NodeId, OrchestratorUrl }
3. Print confirmation and exit
```

The service must be registered separately — `--enroll` does **not** auto-register as a Windows Service. Registration uses `sc create` or similar tools (the .NET `UseWindowsService()` integration handles SCM integration once registered).

> **Difference from original plan:** The original plan described P/Invoke SCM API calls (`OpenSCManager`, `CreateService`, `StartService`) during enrollment. The actual implementation uses `Microsoft.Extensions.Hosting.WindowsServices` — the agent must be registered as a Windows Service externally (e.g., `sc create DeploymentPoC.Agent binPath= "path\to\agent.exe"`).

---

### Agent Binary Download

The Orchestrator serves the agent executable for download:

```
GET /api/agent/download?token=<enrollment-token>
Response: 200 application/octet-stream (agent.exe binary)
```

This validates the enrollment token (must exist, not expired, not already used). This enables a future `/download-agent` page (post-MVP).

---

### Single-Command Reset & Unregistration

**The user runs exactly one command:**

```
DeploymentPoC.Agent.exe --reset
```

This deletes `agent.json` and exits. It does **not** call an unregister API or stop/unregister the Windows Service. The user must do that separately.

> **Difference from original plan:** The original plan described calling `POST /api/agents/{agentId}/unregister`, SCM `StopService`, SCM `DeleteService`, and deleting `agent.json`. The actual implementation only deletes `agent.json` — no API call and no SCM operations during reset. The Orchestrator marks the node as `Offline` automatically via the `NodeHeartbeatMonitorService` (30s interval, marks nodes Offline if last seen > 2 minutes ago).

---

### Enrollment API Endpoints

```
POST /api/nodes/enroll
  -> Generate a new enrollment token
  Body:   { requestedBy, orchestratorUrl, ttlMinutes }
  Response: 201 { token, issuedAt, expiresAt, requestedBy, orchestratorUrl, singleUse: true, used: false }

GET /api/enrollment-tokens
  -> List all enrollment tokens (ordered by issuedAtUtc desc)

POST /api/enrollment-tokens/{token}/consume
  -> Consume token, create NodeEntity, return node ID
  Body:   { hostname?, displayName?, ipAddress?, osVersion?, agentVersion? }
  Response: 200 { id, hostname, displayName, ipAddress, status, lastSeenAt, ... }

GET /api/agent/download?token=<token>
  -> Download agent.exe binary (validates enrollment token)
  Response: 200 application/octet-stream
```

---

## 6. Key Concepts

| Concept | Description |
|---|---|
| **Package (Artifact)** | An installer binary (.exe / .msi) paired with a manifest describing how to install, detect, and uninstall it. Stored as a `PackageEntity` with an `ArtifactIngestManifest`. |
| **Workload Definition** | A named, versioned collection of packages. Contains identity info (`Name`, `Slug`) and tracks which revision is currently published. |
| **Workload Revision** | A specific version of a workload — contains the package list with per-package init steps, workload-level init steps, and a published/unpublished state. Revisions are **immutable** once created (only `PreWorkloadStepsJson`, `PostWorkloadStepsJson`, `PreUninstallStepsJson`, `PostUninstallStepsJson`, `DefaultShell`, and `IsPublished` can be modified). |
| **Workload Run** | A dispatched execution of a workload revision against one or more Agent nodes (install / update / uninstall). The `RunId` is a shared GUID across all per-node records in the run. |
| **Detection** | Three types: `file` (path existence), `registry` (registry key/value matching), `version_manifest` (find binary + run `--version` + parse). Defined per-package in the manifest's `DetectionConfig`. |
| **Init Steps** | Shell commands executed before/after package installation (`preInitSteps`, `postInitSteps`) or before/after entire workload (`preWorkloadSteps`, `postWorkloadSteps`, `preUninstallSteps`, `postUninstallSteps`). Run with configurable shell (`DefaultShell`: `"powershell"` or `"cmd"`). |
| **Upgrade Behavior** | Per-package: `"InPlace"` (run installer over existing) or `"UninstallFirst"` (uninstall old, then install new). |
| **Node Workload State** | Tracks which workload revision is currently deployed on each node, with per-package state stored as JSON in `PackageStatesJson`. Status: `Current`, `Drifted`, or `Unknown`. |
| **Pre-Check** | On-demand detection probe sent to the agent's `/api/detect` endpoint. Returns per-package status: `AlreadySatisfied`, `WrongVersion`, or `NotPresent`. |
| **Diff Engine** | Computes package-level delta between current and target revisions: Added, Removed, Changed, Unchanged packages. Determines install/uninstall/update/skip actions per package. |

---

## 7. Data Schemas

> **Authorship note:** The artifact manifest JSON (uploaded during artifact import) and the workload definition JSON (uploaded during bulk workload import) are hand-authored by system admins or developers. They are not auto-generated. Admins are responsible for ensuring detection configs, install commands, and workload package lists are accurate before uploading them to the Orchestrator.

### 7.1 Artifact Ingest Manifest JSON

Uploaded alongside the binary (or embedded in a ZIP). The manifest describes how to install, detect, and uninstall the artifact. The `PackageId` field in the manifest is optional — if missing, it's inferred from the filename.

**Key difference from original plan:** The manifest shape uses `installAdapter` (not separate top-level `installCommand`/`installArgs`/`updateStrategy` fields) and `detection` (not the 4-field `type`/`key`/`valueName`/`expectedValue` shape). The actual schema uses `path` instead of `key`/`valueName`, and supports 3 detection types: `file`, `registry`, `version_manifest`.

```json
{
  "packageId": "dbeaver-ce",
  "version": "24.1.0",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "dbeaver-ce-24.1.0-x86_64-setup.exe",
    "arguments": "/S",
    "uninstallCommand": "MsiExec.exe",
    "uninstallArgs": "/X{GUID} /qn",
    "upgradeBehavior": "InPlace",
    "expectedExitCodes": [0, 3010],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "registry",
    "path": "DBeaver Community_is1"
  },
  "policyTags": {
    "retryabilityClass": "safe",
    "idempotencyMode": "idempotent",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
```

**Install adapter fields:**

| Field | Purpose | Default |
|---|---|---|
| `type` | Installer type: `"exe"`, `"msi"`, etc. | Inferred from file extension |
| `command` | Install command or binary name. If `{artifactPath}` or empty, uses artifact path as filename | Required (inferred if missing) |
| `arguments` | Install arguments. `{artifactPath}` placeholder is expanded at runtime | `" /S"` for exe, `/quiet /norestart` for msi |
| `uninstallCommand` | Uninstall command. If empty, agent resolves from Windows registry | Empty (registry fallback) |
| `uninstallArgs` | Uninstall arguments | Empty |
| `upgradeBehavior` | `"InPlace"` (run installer over existing) or `"UninstallFirst"` (uninstall then install) | `"InPlace"` |
| `expectedExitCodes` | List of exit codes considered successful | `[0]` |
| `timeoutSeconds` | Process timeout for install/uninstall operations | `300` |

**Detection types & field semantics:**

The `detection` object uses a 3-field shape with `type`, `path`, and optional `expectedVersion`:

```json
"detection": {
  "type": "registry | file | version_manifest",
  "path": "...",
  "expectedVersion": "..."
}
```

**`registry`** — Scans `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` and `HKCU\...\Uninstall` in both 64-bit and 32-bit registry views. Matches `DisplayName` (exact, case-insensitive) against the `path` value.

| Field | Meaning | Example |
|---|---|---|
| `path` | Registry `DisplayName` to match | `"DBeaver Community_is1"` |
| `expectedVersion` | Version string to compare against registry `DisplayVersion` | `"24.1.0"` |

```json
"detection": {
  "type": "registry",
  "path": "DBeaver Community_is1",
  "expectedVersion": "24.1.0"
}
```

**`file`** — Checks whether a file exists at the given path. No version check — purely existence-based.

| Field | Meaning | Example |
|---|---|---|
| `path` | Full path to the file or directory | `"C:\\Program Files\\MyTool\\mytool.exe"` |
| `expectedVersion` | Not used (ignored) | — |

```json
"detection": {
  "type": "file",
  "path": "C:\\Program Files\\MyTool\\mytool.exe"
}
```

**`version_manifest`** — Finds a binary by name, runs `{binary} --version`, parses the output. If `path` contains path separators, checks file existence directly. Otherwise, searches PATH directories and `Program Files` directories (including immediate subdirectories) for the binary.

| Field | Meaning | Example |
|---|---|---|
| `path` | Binary name or full path | `"python"` or `"C:\\Python311\\python.exe"` |
| `expectedVersion` | Version string for comparison (prefix matching: `"3.14"` matches `"3.14.4"`) | `"3.14.0"` |

```json
"detection": {
  "type": "version_manifest",
  "path": "python",
  "expectedVersion": "3.14.0"
}
```

**Detection type guidance:**

| Package type | Recommended detection |
|---|---|
| MSI-based installers (most GUI apps) | `registry` — MSIs always write to Uninstall registry keys. Match by `DisplayName`. |
| Portable / no-installer tools | `version_manifest` — find binary in PATH/Program Files and run `--version` |
| Simple file-presence checks | `file` — just check if a path exists, no version validation |

---

### 7.2 Workload Definition JSON

Workloads are organized as **Workload Definitions** (named containers) with **Workload Revisions** (versioned snapshots of packages and steps). This two-level structure replaces the original flat workload model.

#### Workload Definition (created via API)

```
POST /api/workloads
Body: { "name": "DBMS Workload", "description": "Database management tools" }
Response: 201 { workloadId, name, description, publishedRevision: null, ... }
```

#### Workload Revision (versioned package list)

Created under a workload definition, contains the actual package references and steps:

```
POST /api/workloads/{workloadId}/revisions
Body: {
  "version": "2.0.0",
  "packages": [
    { "packageId": "<guid>", "packageIndex": 1, "preInitSteps": [], "postInitSteps": [] },
    { "packageId": "<guid>", "packageIndex": 2, "preInitSteps": ["net stop SQLBrowser"], "postInitSteps": ["net start SQLBrowser"] }
  ],
  "preWorkloadSteps": ["Write-Host 'Starting workload'"],
  "postWorkloadSteps": ["Write-Host 'Workload complete'"],
  "preUninstallSteps": [],
  "postUninstallSteps": [],
  "defaultShell": "powershell"
}
Response: 201 { revisionId, version, isPublished: false, ... }
```

#### Publishing a Revision

A revision must be published before it can be used in workload runs:

```
POST /api/workloads/{workloadId}/publish
Body: { "revisionId": "<guid>", "replacePublished": true }
Response: 200 { ... }
```

Only one revision can be published per workload at a time. Publishing sets `IsPublished = true` and updates the workload's `PublishedRevisionId`.

#### Bulk Workload Import (from JSON file)

```
POST /api/workloads/bulk-import
Content-Type: multipart/form-data
Body: file=<JSON or JSONC file>
Response: 200 { results: [{ name, slug, status: "success" | "failed", reason? }] }
```

The JSON format supports both string-or-object package entries:

```json
[
  {
    "name": "Dev Tools Stack",
    "slug": "dev-tools-stack",
    "description": "Essential development tools",
    "version": "2.0.0",
    "preWorkloadSteps": ["Write-Host 'Starting dev tools setup'"],
    "postWorkloadSteps": ["Write-Host 'Dev tools installed'"],
    "preUninstallSteps": [],
    "postUninstallSteps": [],
    "defaultShell": "powershell",
    "packages": [
      { "name": "nodejs-24.13.0", "preInitSteps": [], "postInitSteps": [] }
    ]
  },
  {
    "name": "Utility Pack",
    "slug": "utility-pack",
    "version": "1.0.0",
    "packages": ["7zip-24.09", "git-2.47.1"]
  }
]
```

When `packages` entries are strings (e.g., `"nodejs-24.13.0"`), the system looks up or creates a `PackageEntity` by name and infers the version. When entries are objects, they can include `preInitSteps` and `postInitSteps`. The bulk import auto-creates workload definitions, revisions, and package entities, and publishes the revision.

> **Design rationale:** `preInitSteps` / `postInitSteps` live at the workload-package level (within the revision), while `preWorkloadSteps` / `postWorkloadSteps` / `preUninstallSteps` / `postUninstallSteps` live at the workload-revision level. The manifest remains a static, reusable descriptor of the artifact itself. Revisions are immutable — only step fields and publish status can be modified after creation.

---

### 7.3 Bulk Artifact Import (ZIP)

Multiple artifacts can be imported in a single operation by uploading a ZIP file. The ZIP must be a **flat archive** (no subdirectories) containing paired files: one installer binary and one manifest JSON per package.

**Pairing rule: filename stem must match.** Manifests use `.manifest.json` extension.

```
artifacts-import.zip
  nodejs-22.14.0.msi              <- binary
  nodejs-22.14.0.manifest.json   <- manifest (same stem, .manifest.json extension)
  dbeaver-ce-24.1.0-x86_64-setup.exe
  dbeaver-ce-24.1.0-x86_64-setup.manifest.json
```

**Orchestrator import logic:**
1. Extract ZIP to a temp directory
2. For each `.manifest.json` file, look for a media file (`.exe`, `.msi`, `.zip`, etc.) with the same base name
3. Validate manifest JSON schema (required fields, valid detection type, valid upgrade behavior)
4. If duplicate `packageId` + `version` already exists in DB → auto-create/infer (the system resolves conflicts by creating a deterministic GUID)
5. Store both files in the artifact store, create `PackageEntity` DB record with resolved manifest
6. Return import summary

Additionally, there's a **chunked upload** flow for large artifacts:
```
POST /api/artifacts/upload-sessions          -> Create session (optional manifest body)
POST /api/artifacts/upload-sessions/{id}/chunks?index=&totalChunks=  -> Upload chunk
POST /api/artifacts/upload-sessions/{id}/complete  -> Finalize (auto-detects bulk ZIP)
```

**Artifact listing and download:**
```
GET /api/artifacts                              -> List all stored artifacts
GET /api/artifacts/{packageId}/{version}         -> Download binary (range-enabled)
HEAD /api/artifacts/{packageId}/{version}        -> Check existence, get size/ETag
GET /api/artifacts/{packageEntityId:guid}/download -> Download by PackageEntity ID
DELETE /api/artifacts/{packageId}/{version}        -> Delete artifact + PackageEntity
```

---

### 7.4 Artifact Ingest Manifest — Full Schema

The manifest is resolved through a multi-layered system that fills in defaults and infers missing fields:

| Manifest Field | Required? | Default/Inference | Description |
|---|---|---|---|
| `packageId` | No | Inferred from filename stem | Package identifier |
| `version` | No | Inferred from filename or manifest | Package version |
| `channel` | No | `"stable"` | Release channel: `"stable"`, `"canary"`, `"test"` |
| `artifactType` | No | Inferred from file extension (`.exe` → `"exe"`, `.msi` → `"msi"`) | Type of the installer |
| `verificationResult` | No | `"pass"` | Integrity verification result: `"pass"`, `"warn"`, `"fail"` |
| `installAdapter.type` | No | Inferred from `artifactType` | Installer type |
| `installAdapter.command` | No | Inferred as `{artifactPath}` or filename | Install command |
| `installAdapter.arguments` | No | `"/S"` for exe, `"/quiet /norestart"` for msi | Install arguments |
| `installAdapter.uninstallCommand` | No | Empty (falls back to registry resolution) | Uninstall command |
| `installAdapter.uninstallArgs` | No | Empty | Uninstall arguments |
| `installAdapter.upgradeBehavior` | No | `"InPlace"` | `"InPlace"` or `"UninstallFirst"` |
| `installAdapter.expectedExitCodes` | No | `[0]` | List of successful exit codes |
| `installAdapter.timeoutSeconds` | No | `300` | Process timeout in seconds |
| `detection.type` | No | `"registry"` | Detection type |
| `detection.path` | No | Inferred from `packageId` or `name` | Detection path/query |
| `policyTags.*` | No | Various defaults | Retryability, idempotency, risk level, approval |

Each field tracks its provenance via a `Sources` breakdown (whether it came from `Admin`, `Template`, `Analyzer`, or `Default`).

---

## 8. Database Schema (SQLite)

```
EnrollmentTokens
  TokenId           Guid PK (Guid.NewGuid())
  Token             string MaxLength(128) UNIQUE INDEX
  IssuedAtUtc       DateTime
  ExpiresAtUtc      DateTime
  RequestedBy       string MaxLength(255)
  OrchestratorUrl   string MaxLength(512)
  SingleUse         bool default true
  Used              bool default false
  ConsumedAtUtc     DateTime?
  ConsumedByNodeId  Guid? FK -> NodeEntity

NodeEntities
  NodeId            Guid PK (Guid.NewGuid())
  AgentId           string? MaxLength(128)
  Hostname           string MaxLength(255) UNIQUE INDEX
  DisplayName        string MaxLength(255)
  IpAddress          string MaxLength(64)
  Description        string MaxLength(512)
  AgentVersion      string MaxLength(64)
  Status             string MaxLength(64) CHECK IN ('Offline','Online') default 'Offline'
  LastSeenUtc        DateTime
  FirstConnectedUtc  DateTime?
  OsVersion          string MaxLength(255)
  (Nav: ConfigSnapshots, WorkloadRuns, NodeWorkloadStates)

PackageEntities
  PackageId              Guid PK (deterministic GUID for auto-created)
  Name                   string MaxLength(255)
  Version                string MaxLength(64)
  SourcePath              string MaxLength(1024)       -- usually the install command
  InstallType            string MaxLength(64)          -- "exe", "msi", etc.
  InstallArgs             string MaxLength(2048)
  UninstallArgs           string MaxLength(2048)
  UninstallCommand        string MaxLength(2048)
  UpgradeBehavior         string MaxLength(2048)       -- "InPlace" or "UninstallFirst"
  ExpectedExitCodesJson   string MaxLength(256)        -- JSON array e.g. [0, 3010]
  DetectionConfigJson     string MaxLength(2048)       -- serialized DetectionConfig
  TimeoutSeconds          int default 300
  CreatedAtUtc            DateTime

WorkloadDefinitionEntities
  WorkloadId          Guid PK
  Name                string MaxLength(128) UNIQUE INDEX
  Description         string? MaxLength(512)
  PublishedRevisionId Guid? FK -> WorkloadRevisionEntity ON DELETE SET NULL
  CreatedAtUtc        DateTime
  UpdatedAtUtc         DateTime
  (Nav: PublishedRevision, Revisions, Runs, NodeStates)

WorkloadRevisionEntities
  RevisionId              Guid PK
  WorkloadId              Guid FK -> WorkloadDefinitionEntity CASCADE
  Version                  string MaxLength(64) UNIQUE(WorkloadId, Version)
  IsPublished              bool
  CreatedAtUtc             DateTime
  PreWorkloadStepsJson     string MaxLength(4096) default "[]"
  PostWorkloadStepsJson    string MaxLength(4096) default "[]"
  PreUninstallStepsJson    string MaxLength(4096) default "[]" REQUIRED
  PostUninstallStepsJson   string MaxLength(4096) default "[]" REQUIRED
  DefaultShell              string MaxLength(64) default "powershell"
  (Nav: Workload, Packages)

  IMMUTABILITY: Only IsPublished, Pre*StepsJson, Post*StepsJson, DefaultShell
  can be modified. Deletion is blocked by SaveChanges interceptor.

WorkloadPackageEntities
  WorkloadPackageId  Guid PK
  RevisionId         Guid FK -> WorkloadRevisionEntity CASCADE
  PackageId           Guid (logical FK to PackageEntity, no nav prop)
  PackageIndex        int UNIQUE(RevisionId, PackageIndex)
  PreInitStepsJson    string MaxLength(4096) default "[]"
  PostInitStepsJson   string MaxLength(4096) default "[]"
  (Nav: Revision)

  IMMUTABILITY: Only PreInitStepsJson and PostInitStepsJson can be modified.

WorkloadRunEntities
  WorkloadRunRecordId  Guid PK
  RunId                 Guid INDEXED                -- shared across per-node records
  WorkloadId            Guid FK -> WorkloadDefinitionEntity CASCADE
  RevisionId            Guid FK -> WorkloadRevisionEntity RESTRICT
  NodeId                Guid? FK -> NodeEntity SET NULL
  NodeDisplayName       string MaxLength(255)
  Mode                  string MaxLength(32) CHECK IN ('install','update','uninstall','cancel')
  State                 string MaxLength(32) CHECK IN ('Queued','Running','Completed','Failed','Cancelled')
  IdempotencyKey        string? MaxLength(128) UNIQUE
  IdempotencyRequestHash string? MaxLength(64)
  CancelReason          string? MaxLength(512)
  RiskLevel             string?
  RevisionSnapshotJson  string? MaxLength(8192)
  ForceInstall          bool default false
  CreatedAtUtc          DateTime
  UpdatedAtUtc          DateTime
  CompletedAtUtc        DateTime?
  (Nav: Workload, Revision, Node)

  UNIQUE FILTERED INDEX: (NodeId, WorkloadId) WHERE State IN ('Queued','Running')
  -- prevents concurrent active runs on same node+workload

WorkloadRunTimelineEntities
  TimelineId     Guid PK
  RunId          Guid INDEXED
  NodeId         Guid
  MessageType     string MaxLength(64)
  Sequence        int
  PackageId      string? MaxLength(128)
  PackageIndex    int?
  StepName        string? MaxLength(128)
  Status          string? MaxLength(64)
  Detail          string? MaxLength(2048)
  AtUtc           DateTime
  INDEX (RunId, NodeId)

NodeWorkloadStateEntities
  NodeWorkloadStateId  Guid PK
  NodeId                Guid FK -> NodeEntity CASCADE
  WorkloadId            Guid FK -> WorkloadDefinitionEntity CASCADE UNIQUE(NodeId, WorkloadId)
  CurrentRevisionId     Guid? FK -> WorkloadRevisionEntity SET NULL
  PackageStatesJson     string MaxLength(8192) default "{}"
  Status                 string MaxLength(32) CHECK IN ('Current','Drifted','Unknown') default 'Unknown'
  UpdatedAtUtc           DateTime
  (Nav: Node, Workload, CurrentRevision)

JobEntities (legacy — retained, role largely replaced by WorkloadRunEntity)
  JobId, Mode, State, ReasonCode, CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc,
  ManifestPackageId, ManifestTargetVersion, TargetNodeIdsCsv,
  IdempotencyKey, IdempotencyRequestHash, CancelReason
  (Nav: Steps, AssignmentLeases, ConfigSnapshots)

JobStepEntities (legacy)
  JobStepId, JobId FK, StepId, Name, Status, Sequence, ReasonCode,
  TelemetryRef, Detail, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc

AssignmentLeaseEntities (legacy — for old SignalR-based job assignment)
  AssignmentId, LeaseId UNIQUE, JobId FK, AgentId, TtlSeconds > 0,
  LastHeartbeatUtc, LastAckedSequence >= 0, State

ConfigSnapshotEntities (legacy)
  ConfigSnapshotId, JobId FK, NodeId FK, PackageId, SourceSchemaVersion,
  CapturedAtUtc, StorageLocation, IntegrityHash
```

> **Key differences from the original plan:**
> - `Workloads` is split into `WorkloadDefinitionEntities` + `WorkloadRevisionEntities` (named container + versioned snapshot model)
> - `WorkloadRunSteps` is replaced by `WorkloadRunTimelineEntities` with a more flexible message-based schema
> - `AgentNodes` is `NodeEntities` with status values `Online`/`Offline` (not `REGISTERED`/`UNREGISTERED`/`LOST`)
> - `AgentPackages` is replaced by `NodeWorkloadStateEntities` with `PackageStatesJson` (a JSON blob per node+workload)
> - `Artifacts` is `PackageEntities` (no separate artifact table; packages and their manifests are unified)
> - Pre/post steps are at both the **workload-revision level** (`PreWorkloadStepsJson`, `PostWorkloadStepsJson`, `PreUninstallStepsJson`, `PostUninstallStepsJson`) and the **package level** (`PreInitStepsJson`, `PostInitStepsJson`)
> - There is no `assignedWorkloadId`/`assignedWorkloadVersion` on `NodeEntities` — workload assignment is tracked in `NodeWorkloadStateEntities`

---

## 9. Agent Communication Model

**Pattern: HTTP Polling (Active) + SignalR Hub (Implemented but Disabled)**

The Agent **currently uses HTTP polling**. A SignalR hub at `/hubs/agent` is implemented but the push dispatch (`AssignRun` message) is commented out in `WorkloadRunDispatcher.DispatchAsync()`.

### Active HTTP Polling Path

The Agent polls the Orchestrator on a **hard-coded 10-second interval** (not configurable via `agent.json`). Every request uses the `agent_id` query parameter containing the NodeId GUID for identification.

| Operation | Method | Endpoint | Auth |
|---|---|---|---|
| Poll for next task | GET | `/api/workload-runs/pending?agent_id={nodeId}` | `agent_id` query param |
| Claim run (Queued → Running) | PATCH | `/api/workload-runs/{runId}?agent_id={nodeId}` | `agent_id` query param |
| Report step status | POST | `/api/workload-runs/{runId}/timeline?agent_id={nodeId}` | `agent_id` query param |
| Report final status | PATCH | `/api/workload-runs/{runId}?agent_id={nodeId}` | `agent_id` query param |
| Pre-check detection | POST | `http://{agentIp}:5001/api/detect` (agent's own server) | None |

### POL Intercept — Node Online Status

The polling endpoint (`GET /api/workload-runs/pending`) also serves as a heartbeat — it refreshes `NodeEntity.LastSeenUtc` and sets `Status = "Online"`.

A background service (`NodeHeartbeatMonitorService`) runs every **30 seconds** and marks nodes as `Offline` if `LastSeenUtc` is more than **2 minutes** ago.

### Agent Detection Endpoint

The Agent runs its own Kestrel HTTP server on `http://0.0.0.0:5001`:

| Method | Path | Purpose |
|---|---|---|
| GET | `/health` | Returns `{ service: "agent", status: "ok" }` |
| POST | `/api/detect` | Accepts detection request, runs local detection, returns results |
| * | `/*` | Serves static files from `wwwroot/` (SPA fallback) |

The orchestrator calls the agent's `/api/detect` endpoint during pre-checks (`POST /api/nodes/prechecks`) — it sends the detection config for each package and the agent runs local detection and returns results.

### Artifact Download

Artifacts are downloaded by the agent directly from the orchestrator using chunked HTTP:

```
GET /api/artifacts/{packageId}/{version}        -> binary (range-enabled)
GET /api/artifacts/{packageEntityId:guid}/download -> binary by GUID
HEAD /api/artifacts/{packageId}/{version}         -> check existence, size, ETag
```

The agent uses SHA-256 verification on downloaded artifacts and supports chunked downloads (2 MB chunks by default).

### PowerShell / Shell Usage

The Agent uses `System.Diagnostics.Process` for all command execution:

| Operation | .NET API / Method | Shell |
|---|---|---|
| Registry detection | `Microsoft.Win32.Registry` | N/A (native .NET) |
| File/path detection | `System.IO.File.Exists()` | N/A (native .NET) |
| PE file version detection | `System.Diagnostics.FileVersionInfo` | N/A (native .NET) |
| `version_manifest` detection | `System.Diagnostics.Process` with `--version` arg | N/A (native process) |
| Init steps (pre/post) | `System.Diagnostics.Process` | Configurable: `"powershell"` (`-Command`) or `"cmd"` (`/C`). Default: `"powershell"` |
| Install/uninstall | `System.Diagnostics.Process` launching the installer directly | N/A (direct process, no shell) |
| Elevation retry | `ProcessStartInfo.Verb = "runas"` with `UseShellExecute = true` | UAC elevation prompt |
| Windows Service self-registration | `Microsoft.Extensions.Hosting.WindowsServices` (`UseWindowsService()`) | N/A |

> **Difference from original plan:** The original plan stated "PowerShell is never invoked" and that `cmd.exe /c` is used for all init steps. In reality, the default shell for init steps is **PowerShell** (`-Command`), and `cmd.exe /C` is also supported via `DefaultShell`. Additionally, for MSI installers, the agent wraps the command as `msiexec /i "{artifactPath}" {arguments}` automatically.

---

## 10. Orchestrator API — All Endpoints

### Artifact Management

```
POST /api/artifacts
Content-Type: multipart/form-data
Body: { file: <installer binary or ZIP>, manifest: <optional JSON> }
-> Auto-detects ZIP vs plain file
-> Validates manifest, infers defaults
-> Creates PackageEntity + stores artifact
-> Returns { resolvedManifest, packageEntityId }

POST /api/artifacts/bulk
Content-Type: multipart/form-data
Body: { file: <ZIP containing multiple artifact pairs> }
-> Returns { results: [{ fileName, status, reason?, artifact? }] }

POST /api/artifacts/upload-sessions
Body: optional ArtifactIngestManifest JSON
-> Returns { sessionId }

POST /api/artifacts/upload-sessions/{sessionId}/chunks?index=&totalChunks=
Body: { chunk: <binary chunk> }

POST /api/artifacts/upload-sessions/{sessionId}/complete
-> Returns { resolvedManifest, packageEntityId } or { results } (bulk)

GET /api/artifacts
-> Returns list of all stored artifacts

HEAD /api/artifacts/{packageId}/{version}
-> Returns Content-Length, ETag headers

GET /api/artifacts/{packageId}/{version}
-> Returns binary (range-enabled for chunked download)

GET /api/artifacts/{packageEntityId:guid}/download
-> Returns binary by PackageEntity GUID

DELETE /api/artifacts/{packageId}/{version}
-> Deletes artifact files + PackageEntity
```

### Workload Management

```
POST /api/workloads
Body: { name: string (required, max 128), description?: string (max 512) }
-> Creates workload definition (no packages yet)

POST /api/workloads/{workloadId}/revisions
Body: { version, packages[], preWorkloadSteps?, postWorkloadSteps?, preUninstallSteps?, postUninstallSteps?, defaultShell? }
-> Creates a new revision (immutable)

PUT /api/workloads/{workloadId}/revisions/{revisionId}
Body: (same shape as create revision)
-> Updates mutable fields on existing revision (steps, defaultShell)
-> Returns { changed: bool, revision }

POST /api/workloads/{workloadId}/publish
Body: { revisionId: guid, replacePublished?: bool (default true) }
-> Marks revision as published, updates workload's PublishedRevisionId

GET /api/workloads
-> Returns list of all workloads with summary info

GET /api/workloads/{workloadId}
-> Returns full workload detail including revisions and packages

GET /api/workloads/{workloadId}/installed-revisions
-> Returns which nodes have which revisions installed

DELETE /api/workloads/{workloadId}
-> Cascades: deletes runs, packages, revisions, node states

POST /api/workloads/bulk-import
Content-Type: multipart/form-data
Body: { file: <JSON/JSONC file> }
-> Creates WorkloadDefinition + WorkloadRevision + PackageEntity from JSON
-> Returns { results: [{ name, slug, status, reason? }] }
```

### Workload Run Management

```
POST /api/workload-runs
Body: { workloadId, revisionId, mode: "install"|"update"|"uninstall", idempotencyKey, nodeIds[], forceInstall?, reinstall? }
-> Creates WorkloadRun records (one per node)
-> Validates: no concurrent active runs on same node+workload, no downgrade, no non-sequential upgrade
-> Returns { runId, state, riskLevel? }

GET /api/workload-runs?status=
-> Lists runs, grouped by RunId

GET /api/workload-runs/{runId}
-> Returns run detail including per-node status

GET /api/workload-runs/{runId}/steps
-> Returns delta-computed step list for the run

GET /api/workload-runs/pending?agent_id={nodeId}
-> Returns list of pending runs for the agent (also refreshes heartbeat)

PATCH /api/workload-runs/{runId}?agent_id={nodeId}
Body: { status: "Running"|"Completed"|"Failed"|"Cancelled", error? }
-> Atomic state transition (Queued→Running, or final status)

POST /api/workload-runs/{runId}/timeline?agent_id={nodeId}
Body: { step, status, message? }
-> Append timeline event for step-level reporting

POST /api/workload-runs/{runId}/cancel
Body: { reason: string (required, 2-512 chars) }
-> Cancel a queued or running workload run

GET /api/workload-runs/preview?workloadId=&revisionId=&mode=&nodeIds=
-> Returns dry-run delta preview (no execution)
```

### Node Management

```
GET /api/nodes
-> Lists all nodes (status derived from LastSeenUtc)

GET /api/nodes/{id}
-> Returns node detail

POST /api/nodes
Body: { hostname, ipAddress, description? }
-> Creates a node manually (alternative to enrollment)

PUT /api/nodes/{id}
Body: { hostname, displayName?, ipAddress, description? }
-> Updates node

PATCH /api/nodes/{id}/display-name
Body: { displayName }
-> Updates display name only

DELETE /api/nodes/{id}
-> Deletes node

GET /api/nodes/workload-states
-> Returns all node-workload assignment states

GET /api/nodes/{id}/details
-> Returns node detail including workloads and latest pre-check

POST /api/nodes/prechecks
Body: { nodeIds: [guid], workloadId? }
-> Triggers pre-check on agents (POST to http://{ip}:5001/api/detect)
-> Returns per-node detection results

POST /api/nodes/prechecks/summary
Body: { nodeIds: [guid], workloadId, revisionId }
-> Returns per-node action: FreshInstall, Update, Skip, InstallMissing, BlockedDowngrade

POST /api/nodes/{id}/prechecks?workloadId=
-> Single-node pre-check
```

### Node Enrollment

```
POST /api/nodes/enroll
Body: { requestedBy, orchestratorUrl, ttlMinutes }
-> Issues enrollment token (stored in EnrollmentTokens table)

GET /api/enrollment-tokens
-> Lists all tokens (ordered by issuedAtUtc desc)

POST /api/enrollment-tokens/{token}/consume
Body: { hostname?, displayName?, ipAddress?, osVersion?, agentVersion? }
-> Validates token, creates NodeEntity, marks token consumed
-> Returns node details including id (which becomes NodeId)

GET /api/agent/download?token=<enrollment-token>
-> Downloads agent.exe binary (validates token)
```

---

## 11. Pre-Checks

Pre-checks are an explicit triggered task dispatched from the Orchestrator — not passive background scanning. The Orchestrator sends detection requests directly to the Agent's `/api/detect` endpoint (the Agent runs its own HTTP server on port 5001).

**Pre-check flow:**
1. Admin selects Node(s) + Workload in UI → `POST /api/nodes/prechecks`
2. Orchestrator sends `POST http://{node.IpAddress}:5001/api/detect` with `DetectRequest` containing package detection configs
3. Agent runs local detection for each package using `PackageDetector`
4. Agent returns `NodeDetectResponse` with per-package results: `AlreadySatisfied`, `WrongVersion`, or `NotPresent` (with `Comparison`: `"same"`, `"older"`, `"newer"`)
5. Orchestrator upserts `NodeWorkloadStateEntity` based on results (`Status`: `"Current"` or `"Drifted"`)
6. `POST /api/nodes/prechecks/summary` returns per-node action:

| Action | Meaning |
|---|---|
| `FreshInstall` | Package not detected on Agent |
| `Update` | Package detected but at a different version than workload specifies |
| `Skip` | Package already at the correct version |
| `InstallMissing` | Some packages missing, others at correct version |
| `BlockedDowngrade` | Agent has a newer version than workload specifies |
| `Unknown` | Agent unreachable or detection failed |

**Detection result statuses returned by Agent:**

| Status | Meaning |
|---|---|
| `AlreadySatisfied` | Package detected, version matches |
| `WrongVersion` | Package detected, but version differs (includes `Comparison`: `"older"` or `"newer"`) |
| `NotPresent` | Package not detected on Agent |

---

## 12. Execution Modes

### 12.1 Per-Package Execution Order (Install & Update)

For every package processed during Install or Update mode, the Agent follows this sequence:

```
1. DETECT         -- run detection via PackageDetector (reality check)
2. PRE_INIT_STEP  -- execute each command in preInitSteps[] via configured shell
3. ACQUIRE        -- download artifact with SHA-256 verification (chunked if enabled)
4. INSTALL/UPDATE -- run installCommand + installArgs via System.Diagnostics.Process
5. POST_INIT_STEP -- execute each command in postInitSteps[] via configured shell
6. VERIFY         -- re-detect to confirm installation succeeded
7. REPORT         -- post step result to Orchestrator timeline
```

Workload-level steps run around the package loop in non-uninstall modes:

```
PRE_WORKLOAD_STEPS  -- execute each command in preWorkloadSteps[]
[per-package sequence above]
POST_WORKLOAD_STEPS -- execute each command in postWorkloadSteps[]
```

**Elevation handling:** If the install process exits with code 1603 (-1) or throws `Win32Exception` with `NativeErrorCode == 740` (ERROR_ELEVATION_REQUIRED), the Agent retries with `UseShellExecute = true` and `Verb = "runas"` (UAC elevation prompt). Elevation denied (Win32 error 1223) returns `elevation_denied`.

**MSI handling:** If `InstallAdapterConfig.Type == "msi"` and the command equals the artifact path, the Agent wraps the command as `msiexec /i "{artifactPath}" {arguments}`. Default MSI args: `/quiet /norestart`.

**Init step shells:** Init steps are run using the workload revision's `DefaultShell` field (`"powershell"` or `"cmd"`). Environment variables are injected: `DEPLOY_RUN_ID`, `DEPLOY_AGENT_ID`, `DEPLOY_WORKLOAD_NAME`, `DEPLOY_ORCHESTRATOR_URL`, `DEPLOY_PACKAGE_NAME`, `DEPLOY_PACKAGE_VERSION`, `DEPLOY_ARTIFACT_PATH`.

**Init step timeouts:** PreInit=60s, PreWorkload/PreUninstall=60s, PostWorkload/PostUninstall=180s, PostInit=120s.

**Step failure behavior:**

| Failure point | preInitSteps ran? | postInitSteps ran? | Package outcome |
|---|---|---|---|
| preInitStep fails | Partial | No | Abort install for this package → FAILED |
| Installer fails | Yes | No | Skip postInitSteps → FAILED |
| postInitStep fails | Yes | Yes (installer succeeded) | Step flagged as WARNING, package = PARTIAL_SUCCESS |

> `postInitStep` failure does not fail the package installation itself since the installer already succeeded. It is flagged so admins are aware without misrepresenting the installed state.

---

### 12.2 Install Mode

```
Select Node(s) + Workload Revision
         |
   Run PRE_CHECK (POST to agent /api/detect)
         |
   DB Reconcile (Agent reality -> NodeWorkloadStateEntity)
         |
   Evaluate delta via DiffEngine:
   +-----------------+------------------+--------------------+
   |   0/N installed |  X/N installed  |     N/N installed  |
   |                 |   (partial)      |   (all present)     |
   +-----------------+------------------+--------------------+
   | Install all     | Skip existing   | Skip everything     |
   |                 | Install missing  | Run -> SKIPPED      |
   +-----------------+------------------+--------------------+
         |
   Pipeline execution (per-package: detect → acquire → install → verify)
         |
   Update NodeWorkloadState
```

**Diff Engine output for Install mode:**
- Added packages: all packages in the revision (since none are currently installed)
- Changed packages: none (fresh install)
- Removed packages: none
- Unchanged packages: none (or re-detected as `AlreadySatisfied` if ForceInstall is false)

When `ForceInstall = true`, the pre-check phase is skipped and all packages are installed regardless of current state.

---

### 12.3 Update Mode

```
Select Node(s)
         |
   Select new Workload Revision
   -> If selected version <= current assigned version -> REJECT (downgrade blocked)
   -> If selected version is not the immediate next revision -> REJECT (non-sequential upgrade)
         |
   Run PRE_CHECK (targeted to new revision packages)
   DB Reconcile
         |
   Compute full delta via DiffEngine:
     - Added:    in new revision, not previously installed
     - Removed:  in old revision, absent from new revision = orphans
     - Changed:  in both revisions, different package at same PackageIndex
     - Unchanged: same package at same index
         |
   Show delta summary to admin (including orphans to be removed)
   Admin confirms before execution proceeds
         |
   Phase 1: Uninstall removed + changed (UpgradeBehavior=UninstallFirst) packages
         |
   Phase 2: Install/Upgrade added + changed packages
         |
   Phase 3: Verify + PostWorkloadSteps
         |
   DB Reconcile -> Update NodeWorkloadState revision
```

Sequential upgrades are enforced for nodes with an existing revision: only the immediately next workload revision is eligible (e.g., v1 -> v2). Jumping from v1 -> v3 is rejected.

**Package-level version handling during Update:**

| Agent version vs. Target version | Action |
|---|---|
| Not installed | Install |
| Older | Update (per `upgradeBehavior`: InPlace or UninstallFirst) |
| Same | Skip |
| Newer (agent ahead) | Blocked/downgrade rejected |
| In old revision, absent from new revision | Auto-uninstall (orphan removal) |

---

### 12.4 Uninstall Mode

```
Select Node(s)
   (UI shows workloads currently assigned)
         |
   Select workload revision to uninstall
         |
    Run PRE_CHECK (confirm packages actually exist on Agent)
          |
    Execute pipeline in uninstall mode:
      All target packages become "Removed" in DiffEngine
      Uninstall in reverse PackageIndex order
      `preUninstallSteps` and `postUninstallSteps` are executed (workload-level)
          |
    DB Reconcile:
      - Update NodeWorkloadState
      - Clear CurrentRevisionId
```

> `preInitSteps` / `postInitSteps` are **not executed** during Uninstall mode. However, `preUninstallSteps` and `postUninstallSteps` **are** executed — this was implemented as part of the workload-revision-level step support.

In uninstall mode, workload-level `preWorkloadSteps`/`postWorkloadSteps` are skipped in favor of `preUninstallSteps`/`postUninstallSteps`.

---

## 13. State Machines

### WorkloadRun State

```
Queued -> Running -> Completed
                 -> Failed
                 -> Cancelled   (admin or system cancellation)
```

State transitions are enforced atomically. The claim operation (Queued → Running) uses an atomic `ExecuteUpdateAsync` with `WHERE State = 'Queued'` to prevent double-claiming.

### WorkloadRun Modes

```
install | update | uninstall | cancel
```

### NodeEntity Status

```
Offline <-> Online
  (Online: LastSeenUtc within 2 minutes via heartbeat/polling)
  (Offline: LastSeenUtc more than 2 minutes ago, set by NodeHeartbeatMonitorService every 30s)
```

### NodeWorkloadState Status

```
Current  -- all packages match expected versions
Drifted  -- one or more packages at wrong version
Unknown  -- no pre-check has been run yet
```

---

## 14. Workload Version Semantics (Update Mode)

Workloads use a **Revision** model — each revision has a `Version` string and an `IsPublished` flag. Only published revisions can be used for workload runs.

| Package scenario (old revision → new revision) | Action |
|---|---|
| In both, same package at same index | Skip (unchanged) |
| In both, different package at same index | Update (InstallAdapter.UpgradeBehavior determines method) |
| Only in new revision (new PackageIndex) | Install |
| Only in old revision (PackageIndex removed) | Auto-uninstall after new packages confirmed |
| Agent has newer version than target | Block (downgrade rejected) |

Sequential upgrades are enforced: nodes can only advance one revision at a time (no jumping revisions).

> **Difference from original plan:** The original plan described packages being matched by `packageId` across workload versions. The actual implementation uses `PackageIndex` position-based comparison — packages at the same index in different revisions are compared, and index differences determine add/remove/change actions.

---

## 15. MVP Scope Boundary

| Feature | MVP | Post-MVP |
|---|---|---|
| Upload artifact (binary + manifest, single) | Yes | |
| Bulk artifact import via flat ZIP | Yes | |
| Chunked artifact upload (for large files) | Yes | |
| Upload workload definition (create + revision + publish) | Yes | |
| Bulk workload import from JSON file | Yes | |
| Enrollment token generation (API) | Yes | |
| Agent binary download via enrollment token | Yes | |
| Agent single-command enrollment (--enroll) | Yes | |
| Agent reset (--reset, deletes config only) | Yes | |
| Node CRUD (manual creation + enrollment-based) | Yes | |
| Node heartbeat + Offline detection (2-min threshold) | Yes | |
| Polling-based task dispatch (10s interval) | Yes | |
| SignalR hub (implemented but disabled) | Partial | Full push dispatch |
| Pre-checks (on-demand detection via agent /api/detect) | Yes | |
| DB reconciliation (Agent reality → NodeWorkloadState) | Yes | |
| Delta summary via DiffEngine | Yes | |
| Dry-run preview (preview endpoint) | Yes | |
| Install mode | Yes | |
| Update mode (revision-based, with downgrade rejection) | Yes | |
| Uninstall mode (with preUninstallSteps/postUninstallSteps) | Yes | |
| preInitSteps / postInitSteps (per-package) | Yes | |
| preWorkloadSteps / postWorkloadSteps / preUninstallSteps / postUninstallSteps (per-revision) | Yes | |
| Per-timeline-event audit log | Yes | |
| upgradeBehavior in manifest (InPlace / UninstallFirst) | Yes | |
| Detection types: registry, file, version_manifest | Yes | |
| WMI detection | | Stretch |
| One active WorkloadRun per Node+Workload | Yes | |
| Workload revision immutability | Yes | |
| Artifact chunked download with SHA-256 verification | Yes | |
| ForceInstall flag (skip pre-check, force all packages) | Yes | |
| Idempotency keys on workload runs | Yes | |
| /download-agent endpoint (token-based binary download) | Yes | |
| Node display names and descriptions | Yes | |
| Risk level evaluation from policyTags | Yes | |
| /api/agent/download?token= endpoint | Yes | |
| Agent artifact download with SHA-256 verification | Yes | |
| Multi-node workload run dispatch | Yes | |
| Real-time task dispatch (SignalR enabled) | | Yes |
| Full agent-wide package scan (FULL_SCAN mode) | | Yes |
| Rollback on failure | | Yes |
| Scheduled / cron runs | | Yes |

---

## 16. Build Order

### Phase 1 — Foundation
1. SQLite schema + EF Core models (all tables)
2. Orchestrator `appsettings.json` configuration wiring (artifact store path, agent download path, connection string)
3. Enrollment token generation endpoint (`POST /api/nodes/enroll`)
4. Node CRUD endpoints (`GET/POST/PUT/PATCH/DELETE /api/nodes`)
5. Token consumption + agent binary download (`POST /api/enrollment-tokens/{token}/consume`, `GET /api/agent/download`)
6. Agent `--enroll` CLI mode (consume token + write agent.json)
7. Agent `--reset` CLI mode (delete agent.json)
8. Artifact single upload endpoint + disk store (manifest validation, PackageEntity creation)
9. Artifact bulk import endpoint (flat ZIP, paired by stem)
10. Chunked upload session endpoint (3-step: create → upload chunks → complete)
11. Workload definition + revision CRUD endpoints
12. Workload publish endpoint
13. Workload bulk import from JSON

### Phase 2 — Core Pipeline
14. Node heartbeat via polling endpoint (`GET /api/workload-runs/pending?agent_id=`)
15. NodeHeartbeatMonitorService (30s interval, marks Offline after 2 min)
16. Agent polling loop (10s interval, claim runs, report timeline events)
17. Agent detection endpoint (`POST /api/detect`) with registry, file, version_manifest support
18. Orchestrator pre-check dispatch (`POST /api/nodes/prechecks`)
19. DiffEngine delta computation
20. NodeWorkloadState reconciliation
21. WorkloadRun creation, state transitions, and idempotency

### Phase 3 — Execution Modes
22. Install mode — Agent pipeline (detect → acquire → install → verify → report)
23. Update mode — downgrade rejection, diff-based phase execution, orphan removal
24. Uninstall mode — with preUninstallSteps/postUninstallSteps
25. Cancel workload run endpoint

### Phase 4 — Web UI (React)
26. Artifact upload page (single binary + manifest, ZIP bulk import, chunked upload)
27. Workload management page (create, revision, publish, bulk import)
28. Node list (status badge, display name, assigned workload, last seen)
29. Enrollment token generation + agent download page
30. Run creation wizard (select nodes → select workload revision → choose mode → pre-check → execute)
31. Dry-run preview (delta before execution)
32. Run list + detail view (per-package timeline events, step status)
33. Node detail view (workload states, pre-check results)

---

## 17. Resolved Decisions

| # | Decision | Resolution |
|---|---|---|
| 1 | Detection types for MVP | `registry`, `file`, and `version_manifest`. `wmi` is a stretch goal. |
| 2 | Update strategy naming | `"InPlace"` (run installer over existing) or `"UninstallFirst"` (uninstall then install). Renamed from original `"overinstall"` / `"reinstall"`. |
| 3 | Network topology | Agent requires HTTP access to Orchestrator for polling + artifact download. Orchestrator requires HTTP access to Agent's `/api/detect` endpoint for pre-checks. |
| 4 | Concurrency | One active WorkloadRun per Node+Workload combination. Enforced by unique filtered index on `(NodeId, WorkloadId) WHERE State IN ('Queued','Running')`. |
| 5 | Agent identity | GUID (`NodeId`) generated by Orchestrator at enrollment, stored in `agent.json`. Deleted on `--reset`. Not hostname-based. |
| 6 | postInitStep failure severity | `PARTIAL_SUCCESS` / `WARNING` — does not mark the package as FAILED |
| 7 | Workload-level orphan handling | Removed packages (PackageIndex absent in new revision) are uninstalled in the uninstall phase after new packages are verified. Admin confirms the full delta before execution. |
| 8 | Workload-level init steps | Implemented at both package level (`preInitSteps`, `postInitSteps`) and revision level (`preWorkloadSteps`, `postWorkloadSteps`, `preUninstallSteps`, `postUninstallSteps`). All are executed. |
| 9 | Enrollment token TTL | Configurable via `ttlMinutes` (1-120 minutes). Default in the API is 20 minutes. |
| 10 | Task dispatch | HTTP polling at 10-second intervals. SignalR hub implemented but disabled. |
| 11 | Node status model | `Online`/`Offline` based on LastSeenUtc. 2-minute threshold for Offline detection via background service. No `REGISTERED`/`UNREGISTERED`/`LOST` states. |
| 12 | Authentication model | NodeId as `agent_id` query parameter (not bearer token). No `agentSecret`. |
| 13 | Workload structure | Two-level: `WorkloadDefinition` (named container) + `WorkloadRevision` (versioned snapshot). Revisions are immutable except for step fields and publish status. |
| 14 | Detection config schema | `type` + `path` + `expectedVersion` (3 fields, not 4). `path` replaces `key`/`valueName`, `expectedVersion` replaces `expectedValue`. |
| 15 | Init step default shell | `"powershell"` (not `"cmd"`). Configurable per workload revision via `DefaultShell`. |
| 16 | Agent service registration | Uses `Microsoft.Extensions.Hosting.WindowsServices` (`UseWindowsService()`), not P/Invoke SCM directly. Must be registered externally (e.g., `sc create`). |
| 17 | Package entity model | `PackageEntity` unifies the artifact and manifest. No separate `Artifacts` table. `PackageId` is a GUID (not a string like `"dbeaver-ce"`). |
| 18 | Init step environment variables | `DEPLOY_RUN_ID`, `DEPLOY_AGENT_ID`, `DEPLOY_WORKLOAD_NAME`, `DEPLOY_ORCHESTRATOR_URL`, `DEPLOY_PACKAGE_NAME`, `DEPLOY_PACKAGE_VERSION`, `DEPLOY_ARTIFACT_PATH` |
| 19 | MSI install wrapping | Agent automatically wraps MSI installs as `msiexec /i "{artifactPath}" {arguments}` when `InstallAdapterConfig.Type == "msi"`. |
| 20 | Uninstall resolution | If `UninstallCommand` is empty, Agent resolves the uninstaller from Windows Registry (prefers `QuietUninstallString` over `UninstallString`). |
| 21 | Version comparison | Prefix matching: `"3.14"` matches `"3.14.4"`. Uses `VersionComparer` that extracts the first numeric-dot sequence. |
| 22 | Enrollment flow | `--enroll` does NOT auto-register as a Windows Service. It only calls the consume endpoint and writes `agent.json`. Service registration is external. |
| 23 | `--reset` behavior | Only deletes `agent.json`. Does NOT call any API or touch the Windows Service. The Orchestrator detects node absence via heartbeat timeout. |
