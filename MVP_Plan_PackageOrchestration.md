# MVP Plan: Orchestrator System

---

## 1. Overview

A Windows-only package orchestration system composed of two components:

- **Orchestrator** — Locally hosted by system admins. Manages artifacts, workloads, agent nodes, and dispatches workload tasks. Exposes a React Web UI backed by a C# .NET API and SQLite database with a local on-disk artifact store.
- **Agent (Node)** — A stateless background service installed on remote Windows machines. Polls the Orchestrator for pending tasks and executes them locally. Reports results back to the Orchestrator.

---

## 2. Project Structure & Deployment

Both components are separate C# .NET projects (`.csproj`) published as fully self-contained Windows executables — all dependencies and native libraries bundled into a single `.exe`. No runtime installation required on either machine.

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

### Orchestrator (`Orchestrator.csproj`)
- ASP.NET Core web application — runs an embedded Kestrel HTTP server
- Serves the React UI as embedded static files (built into `wwwroot` at publish time)
- Hosts the REST API consumed by both the React UI and Agent nodes
- Deployed and run manually by system admins on a local/internal Windows machine

### Agent (`Agent.csproj`)
- .NET Windows Service — runs as a background service via `UseWindowsService()`
- Always run with elevated privileges (as Administrator) — assumed for all operations
- Stateless: all persistent state lives in the Orchestrator DB; Agent stores only its local identity in `agent.json`
- **PowerShell is never invoked.** All detection and execution uses .NET APIs and `cmd.exe /c` process invocation directly — see Section 7 for details.

---

## 3. Configuration

### Orchestrator `appsettings.json`

```json
{
  "Agent": {
    "DefaultPollingIntervalSeconds": 30,
    "LostThresholdMultiplier": 3
  },
  "ArtifactStore": {
    "BasePath": "C:\\OrchestratorData\\Artifacts"
  },
  "Enrollment": {
    "TokenTtlHours": 24
  }
}
```

| Key | Purpose |
|---|---|
| `DefaultPollingIntervalSeconds` | Returned to Agent at enrollment. Written into `agent.json`. Default: 30s. |
| `LostThresholdMultiplier` | Agent is marked `LOST` if no heartbeat within `pollingInterval × multiplier`. Default: 3 (e.g., 90s at 30s polling). |
| `ArtifactStore.BasePath` | Local disk path where uploaded binaries and manifests are stored. |
| `Enrollment.TokenTtlHours` | Lifetime of a generated enrollment token. Default: 24h. |

### Agent `agent.json`

Written by `Agent.exe --enroll`. Deleted by `Agent.exe --reset`. Not manually edited.

```json
{
  "agentId": "uuid-generated-by-orchestrator",
  "orchestratorUrl": "http://192.168.1.10:5000",
  "agentSecret": "long-lived-uuid-secret",
  "pollingIntervalSeconds": 30
}
```

Stored in `%ProgramData%\OrchestratorAgent\agent.json`.

> `pollingIntervalSeconds` is set by the Orchestrator at enrollment time (sourced from its `DefaultPollingIntervalSeconds`). Changing the Orchestrator default does not affect already-enrolled agents — they use the value baked into their `agent.json`.

---

## 4. Agent Enrollment & Registration

### Overview

Agents connect to the Orchestrator via a token-based enrollment flow — a single command run by the user on the Agent machine. This is compliance-safe (no custom PowerShell scripts), auditable, and requires no manual service registration steps.

The enrollment token and the ongoing polling credential are **intentionally separate**:
- **Enrollment token** — short-lived, single-use. Used exactly once to register the Agent.
- **Agent secret** — long-lived UUID returned at enrollment. Used on every subsequent polling request as a bearer token. Never re-exposed after initial enrollment.

---

### Single-Command Enrollment

Since `Agent.exe` always runs with elevated privileges, it can register itself as a Windows Service internally using the Windows Service Control Manager (SCM) API — via P/Invoke (`advapi32.dll`: `OpenSCManager` -> `CreateService` -> `StartService`). No `sc create` or `sc start` is ever visible to the user.

**The user runs exactly one command:**

```
Agent.exe --enroll <token> --url <orchestrator-url-or-ip>
```

Internally, this performs the full setup sequence:

```
1. Call POST /api/agents/enroll  ->  receive agentId, agentSecret, pollingIntervalSeconds
2. Write agent.json to %ProgramData%\OrchestratorAgent\
3. Call CreateService() via SCM API to register Agent.exe as a Windows Service
4. Call StartService() to start the service immediately
5. Print confirmation and exit
```

The service is now running in the background. The user is done.

---

### Single-Command Reset & Unregistration

**The user runs exactly one command:**

```
Agent.exe --reset
```

Internally, this performs the full teardown sequence:

```
1. Call POST /api/agents/{agentId}/unregister  (if Orchestrator reachable)
2. Call StopService() via SCM API
3. Call DeleteService() via SCM API
4. Delete agent.json from %ProgramData%\OrchestratorAgent\
5. Print confirmation and exit
```

If the Orchestrator is unreachable during `--reset`, steps 2-4 still execute. `agent.json` is always deleted regardless of Orchestrator connectivity. The Orchestrator marks the Agent as `LOST` automatically after the heartbeat timeout elapses.

---

### Enrollment Flow (Orchestrator side)

**Step 1 — Admin generates a token in the Orchestrator UI or API:**
```
POST /api/enrollment/tokens
Response: { token: "abc123...", expiresAt: "2025-06-01T12:00:00Z" }
```

Token properties:
- Single-use — invalidated immediately after a successful enrollment call
- Time-limited — TTL configured in Orchestrator `appsettings.json` (default: 24h)
- Stored in the `EnrollmentTokens` table with `used`, `usedAt`, and `expiresAt` fields

**Step 2 — Agent calls the enroll endpoint (triggered by `--enroll` CLI flag):**
```
POST /api/agents/enroll
Body:     { token, hostname, ipAddress }
Response: { agentId, agentSecret, pollingIntervalSeconds }
```

Orchestrator validates the token (exists, not expired, not already used), creates an `AgentNode` record, and invalidates the token.

---

### Task Dispatch Timing

The Orchestrator dispatches workload runs by creating a `WorkloadRun` record with status `PENDING`. The Agent discovers this at its **next poll cycle** — maximum latency of one polling interval (0–30s by default).

From the admin's perspective in the UI: the run appears as `PENDING` immediately after dispatch. It transitions to `RUNNING` once the Agent picks it up at next poll.

> **Post-MVP:** SignalR/WebSocket can replace or supplement polling to achieve near-zero dispatch latency if needed.

---

### Agent Download Page (Post-MVP)

A future Orchestrator UI page at `/download-agent` would allow users on target machines to:
1. Download `Agent.exe` directly from the Orchestrator
2. See the Orchestrator URL pre-filled (since they're already browsing it)
3. Copy a freshly generated enrollment token from the page
4. Run `Agent.exe --enroll <token> --url <url>` as instructed on-screen

This is explicitly a post-MVP feature — for MVP, admins distribute `Agent.exe` manually and share the token out-of-band.

---

### Enrollment API Endpoints

```
POST /api/enrollment/tokens
  -> Generate a new enrollment token
  Response: { token, expiresAt }

POST /api/agents/enroll
  Body:     { token, hostname, ipAddress }
  Response: { agentId, agentSecret, pollingIntervalSeconds }

POST /api/agents/{agentId}/unregister
  Header:   Authorization: Bearer <agentSecret>
  -> Marks AgentNode as UNREGISTERED in DB
```

---

## 5. Key Concepts

| Concept | Description |
|---|---|
| **Artifact** | An installer binary (.exe / .msi) paired with a manifest JSON describing the package |
| **Package** | The logical unit described by the manifest (e.g., SSMS, Python, DBeaver) |
| **Workload** | A versioned collection of packages with specific versions, pre/post steps, and deployment intent |
| **Workload Run** | A dispatched execution of a workload against an Agent node (install / update / uninstall / pre-check) |
| **DB Reconciliation** | The process of updating `AgentPackages` records to reflect the Agent's actual installed state. Agent reality is the source of truth — the DB follows it. |
| **Workload-level Orphan** | A package defined in the old workload version but absent from the new workload version. Still physically installed on the Agent and recorded in `AgentPackages`. During Update mode, orphans are surfaced in the delta summary and auto-uninstalled after the new packages are successfully installed (with admin confirmation before execution). Distinct from Agent-vs-DB mismatches, which are resolved by DB reconciliation during pre-checks. |

---

## 6. Data Schemas

> **Authorship note:** Both the package manifest JSON and workload definition JSON are hand-authored by system admins or developers. They are not auto-generated. Admins are responsible for ensuring detection rules, install/uninstall commands, and workload package lists are accurate before uploading them to the Orchestrator.

### 6.1 Package Manifest JSON

Stored alongside the binary in the artifact store. Describes the artifact itself — static and reusable across workloads. Authored once per package version by an admin or developer.

**`packageId` and `version` are kept as separate fields** — they serve different roles:
- `packageId` is the stable identity of a package across versions (e.g., `dbeaver-ce` regardless of whether it's 24.0 or 24.1). Used for workload references, DB lookups, and cross-version comparisons.
- `version` is a standalone field because all core logic — pre-checks, delta computation, downgrade rejection, `AgentPackages` records — performs version comparisons. Parsing a version out of a combined string would be fragile and error-prone.
- `installerFile` captures the exact binary filename, which also serves as the pairing key during bulk ZIP import (see Section 6.3).

```json
{
  "packageId": "dbeaver-ce",
  "packageName": "DBeaver Community Edition",
  "version": "24.1.0",
  "installerFile": "dbeaver-ce-24.1.0-x86_64-setup.exe",
  "installCommand": "dbeaver-ce-24.1.0-x86_64-setup.exe",
  "installArgs": "/S",
  "uninstallCommand": "MsiExec.exe",
  "uninstallArgs": "/X{GUID} /qn",
  "updateStrategy": "overinstall",
  "detection": {
    "type": "registry",
    "key": "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DBeaver Community_is1",
    "valueName": "DisplayVersion",
    "expectedValue": "24.1.0"
  }
}
```

**Update strategies:** `overinstall` (run installer over existing) | `reinstall` (uninstall then install)

#### Detection Types & Field Semantics

The `detection` object uses the same 4-field shape across all types, but the meaning of each field differs per `type`. They are **not** semantically identical.

```json
"detection": {
  "type":          "registry | filePath | wmi",
  "key":           "...",
  "valueName":     "...",
  "expectedValue": "..."
}
```

**`registry`** — Reads a value from the Windows registry and compares it.

| Field | Meaning | Example |
|---|---|---|
| `key` | Full registry hive + path | `HKLM\SOFTWARE\...\{GUID}` |
| `valueName` | Registry value name under that key | `DisplayVersion` |
| `expectedValue` | Exact string to match | `24.1.0` |

```json
"detection": {
  "type": "registry",
  "key": "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DBeaver Community_is1",
  "valueName": "DisplayVersion",
  "expectedValue": "24.1.0"
}
```

**`filePath`** — Checks whether a file or directory exists, optionally validating its PE version metadata.

| Field | Meaning | Example |
|---|---|---|
| `key` | Full path to the file or directory | `C:\Program Files\Python311\python.exe` |
| `valueName` | `null` = existence check only. `"FileVersion"` = read PE file version metadata | `"FileVersion"` or `null` |
| `expectedValue` | Version string if `valueName` is `"FileVersion"`. `null` if existence-only | `3.11.0` or `null` |

```json
// Existence-only check
"detection": {
  "type": "filePath",
  "key": "C:\\Program Files\\MyTool\\mytool.exe",
  "valueName": null,
  "expectedValue": null
}

// File version check (reads PE metadata)
"detection": {
  "type": "filePath",
  "key": "C:\\Program Files\\Python311\\python.exe",
  "valueName": "FileVersion",
  "expectedValue": "3.11.0"
}
```

> `valueName` acts as a **mode switch** for `filePath`: `null` = "does this path exist?", `"FileVersion"` = "does this file exist AND does its version match?"

**`wmi`** — Executes a WQL query against WMI and reads a property from the result. (Stretch goal — registry and filePath are sufficient for MVP.)

| Field | Meaning | Example |
|---|---|---|
| `key` | Full WQL query string including WHERE filter | `SELECT * FROM Win32_Product WHERE Name = 'Python 3.11.0'` |
| `valueName` | WMI property to read from the query result row | `Version` |
| `expectedValue` | Exact string to match against that property | `3.11.0` |

```json
"detection": {
  "type": "wmi",
  "key": "SELECT * FROM Win32_Product WHERE Name = 'Python 3.11.0'",
  "valueName": "Version",
  "expectedValue": "3.11.0"
}
```

> The `key` for `wmi` carries the full WQL query — including the `WHERE` clause — so no additional filter field is needed.

**Detection type guidance:**

| Package type | Recommended detection |
|---|---|
| MSI-based installers (most GUI apps) | `registry` — MSIs always write to Uninstall registry keys |
| Portable / no-installer tools | `filePath` — check executable presence and optionally version |
| Complex or non-standard installers | `wmi` — stretch goal; fallback when registry/file paths are unpredictable |

---

### 6.2 Workload Definition JSON

Describes how packages are deployed in a specific context. Authored by admins or developers per workload version. Pre/post init steps are workload-level — the same package can have different steps depending on which workload it belongs to.

**Single workload (standard upload):**

```json
{
  "workloadId": "dbms-workload",
  "workloadName": "DBMS Workload",
  "version": "2.0",
  "packages": [
    {
      "packageId": "ssms-2019",
      "version": "15.0.18390.0",
      "preInitSteps": [
        "net stop SQLBrowser",
        "reg add HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows /v DisableWindowsUpdateAccess /t REG_DWORD /d 1 /f"
      ],
      "postInitSteps": [
        "net start SQLBrowser",
        "setx SSMS_HOME \"C:\\Program Files (x86)\\Microsoft SQL Server Management Studio 18\" /M"
      ]
    },
    {
      "packageId": "dbeaver-ce",
      "version": "24.1.0",
      "preInitSteps": [],
      "postInitSteps": [
        "mkdir \"C:\\ProgramData\\DBeaverData\""
      ]
    }
  ]
}
```

**Bulk workload upload (array in one JSON file):**

```json
[
  {
    "workloadId": "dbms-workload",
    "workloadName": "DBMS Workload",
    "version": "2.0",
    "packages": [ ... ]
  },
  {
    "workloadId": "dev-workload",
    "workloadName": "Developer Workload",
    "version": "1.0",
    "packages": [ ... ]
  }
]
```

The Orchestrator accepts the same endpoint for both — it detects whether the uploaded JSON is a single object or an array and processes accordingly. On conflict (same `workloadId` + `version` already exists): **upsert** — update any fields that have changed, leave unchanged fields as-is.

> **Design rationale:** `preInitSteps` / `postInitSteps` live at the workload level (not the manifest) because the same artifact may require different environment preparation depending on which workload it's deployed under. The manifest remains a static, reusable descriptor of the artifact itself.

---

### 6.3 Bulk Artifact Import (ZIP)

Multiple artifacts can be imported in a single operation by uploading a ZIP file. The ZIP must be a **flat archive** (no subdirectories) containing paired files: one installer binary and one manifest JSON per package.

**Pairing rule: filename stem must match exactly.**

```
artifacts-import.zip
  dbeaver-ce-24.1.0-x86_64-setup.exe     <- binary
  dbeaver-ce-24.1.0-x86_64-setup.json    <- manifest (same stem, .json extension)
  ssms-setup-15.0.18390.0.exe
  ssms-setup-15.0.18390.0.json
```

The manifest's `installerFile` field must match the binary filename exactly. This serves as a self-referencing validation check during import.

**Orchestrator import logic:**
1. Extract ZIP to a temp directory
2. For each `.json` file found, look for a binary with the same filename stem
3. If no matching binary -> mark as failed, include in error summary, continue
4. Validate manifest JSON schema (required fields, valid detection type, etc.)
5. If `installerFile` value does not match the paired binary filename -> mark as failed
6. If `packageId` + `version` already exists in DB -> reject (existing record preserved)
7. Store the pair in the artifact store, create `Artifacts` DB record
8. Return import summary

**Import summary response:**
```json
{
  "imported": [
    { "packageId": "dbeaver-ce", "version": "24.1.0", "installerFile": "dbeaver-ce-24.1.0-x86_64-setup.exe" }
  ],
  "failed": [
    { "file": "ssms-setup-15.0.18390.0.json", "reason": "No matching binary found" },
    { "file": "python-3.11.json", "reason": "Duplicate: packageId 'python' version '3.11.0' already exists" }
  ]
}
```

---

## 7. Database Schema (SQLite)

```
EnrollmentTokens
  id, token, createdAt, expiresAt, used (bool), usedAt, usedByAgentId

Artifacts
  id, packageId, packageName, version, installerFile, manifestPath, binaryPath, uploadedAt

Workloads
  id, workloadId, workloadName, version, definitionPath, uploadedAt

WorkloadPackages
  workloadId, packageId, packageVersion,
  preInitSteps  TEXT (JSON array, nullable),
  postInitSteps TEXT (JSON array, nullable)

AgentNodes
  id, agentId (UUID), hostname, ipAddress, agentSecret,
  lastSeenAt, registeredAt, status (REGISTERED | UNREGISTERED | LOST),
  assignedWorkloadId, assignedWorkloadVersion   <- NULL until workload installed

AgentPackages                                   <- source of truth for Agent installed state
  agentId, packageId, installedVersion, detectedAt,
  status (installed | missing | unknown)

WorkloadRuns                                    <- audit log of all operations
  id, agentId, workloadId, workloadVersion,
  mode   (PRE_CHECK | INSTALL | UPDATE | UNINSTALL),
  status (PENDING | RUNNING | SUCCESS | FAILED | SKIPPED),
  createdAt, startedAt, completedAt

WorkloadRunSteps                                <- per-step log within a run
  id, runId, packageId, packageVersion,
  action  (DETECT | PRE_INIT_STEP | INSTALL | POST_INIT_STEP | SKIP | UPDATE | UNINSTALL | VERIFY),
  status, message, exitCode, startedAt, completedAt
```

> **Note:** Each individual command inside `preInitSteps` / `postInitSteps` is logged as its own `WorkloadRunStep` entry with stdout and exit code captured — not batched — so failures are debuggable at the command level.

---

## 8. Agent Communication Model

**Pattern: HTTP Polling (Pull)**

The Agent polls the Orchestrator on a configurable interval (sourced from `agent.json`, set at enrollment from the Orchestrator's `DefaultPollingIntervalSeconds`). No persistent connection required. Works across firewalls. Every request after enrollment is authenticated with the `agentSecret` as a bearer token.

**PowerShell prohibition:** The Agent never invokes `powershell.exe`. All operations use native .NET APIs:

| Operation | .NET API used |
|---|---|
| Registry detection | `Microsoft.Win32.Registry` |
| File/path detection | `System.IO.FileInfo` |
| PE file version detection | `System.Diagnostics.FileVersionInfo` |
| WMI detection (stretch) | `System.Management.ManagementObjectSearcher` |
| preInitSteps / postInitSteps / install / uninstall | `System.Diagnostics.Process` launching `cmd.exe /c <command>` |
| Windows Service self-registration | P/Invoke `advapi32.dll`: `OpenSCManager`, `CreateService`, `StartService`, `StopService`, `DeleteService` |

### API Contract

**Agent Polling (fetch next task)**
```
GET /api/agents/{agentId}/tasks/next
Header:   Authorization: Bearer <agentSecret>
Response: null | {
  runId, mode, workloadId, workloadVersion,
  packages: [{
    packageId, version,
    manifestUrl, binaryUrl,
    installCommand, installArgs,
    uninstallCommand, uninstallArgs,
    updateStrategy,
    detection,
    preInitSteps,
    postInitSteps
  }]
}
```

**Agent Heartbeat**
```
POST /api/agents/{agentId}/heartbeat
Header:   Authorization: Bearer <agentSecret>
-> Updates AgentNode.lastSeenAt.
   Orchestrator marks agent LOST if no heartbeat within pollingIntervalSeconds x LostThresholdMultiplier.
```

**Step-level reporting (incremental)**
```
POST /api/runs/{runId}/steps
Header:   Authorization: Bearer <agentSecret>
Body:     { packageId, action, status, message, exitCode }
```

**Run completion**
```
POST /api/runs/{runId}/complete
Header:   Authorization: Bearer <agentSecret>
Body:     { status, detectedPackages: [{ packageId, version, detected: bool }] }
```

**Artifact download**
```
GET /api/artifacts/{artifactId}/download   <- binary
GET /api/artifacts/{artifactId}/manifest   <- manifest JSON
(both require Authorization: Bearer <agentSecret>)
```

---

## 9. Orchestrator API — Upload & Bulk Import Endpoints

```
--- Single artifact upload ---
POST /api/artifacts
Content-Type: multipart/form-data
Body: { binary: <file>, manifest: <file> }
-> Validates pairing (installerFile in manifest matches binary filename)
-> Rejects if packageId + version already exists in DB
-> Stores both, creates Artifacts record

--- Bulk artifact import (ZIP) ---
POST /api/artifacts/bulk
Content-Type: multipart/form-data
Body: { archive: <zip file> }
-> Flat ZIP only (no subdirectories)
-> Pairs by filename stem, validates each manifest
-> Rejects duplicate packageId + version entries
-> Returns: { imported: [...], failed: [...] }

--- Single or bulk workload upload ---
POST /api/workloads
Content-Type: application/json
Body: { single workload object } or [ array of workload objects ]
-> Auto-detects single vs array
-> Upserts on workloadId + version conflict (updates changed fields)
-> Returns: { imported: [...], updated: [...], failed: [...] }
```

---

## 10. Pre-Checks

Pre-checks are an explicit triggered task dispatched from the Orchestrator — not passive background scanning. The Agent only scans for packages defined in the selected workload (targeted scan), keeping it fast and scoped.

**Pre-check flow:**
1. Admin selects Agent + Workload in UI -> Orchestrator queues a `PRE_CHECK` run
2. Agent polls, receives task, runs local detection for each package using the manifest `detection` definition
3. Agent reports detected packages and versions back via `POST /api/runs/{runId}/complete`
4. Orchestrator performs DB reconciliation — updates `AgentPackages` to match what the Agent actually has. Packages detected but absent from `AgentPackages` are inserted; packages recorded as installed but not detected are updated to `missing`.
5. Orchestrator computes delta summary and returns it to the UI

**Delta summary output (per package):**

| Status | Meaning |
|---|---|
| `MATCHES` | Package installed, version matches workload exactly |
| `MISSING` | Package not detected on Agent |
| `VERSION_DRIFT` | Package detected but at a different version than workload specifies |
| `AHEAD` | Agent has a newer version than the workload specifies |
| `ORPHANED` | Package installed on Agent, in DB, but absent from the new workload version — will be auto-uninstalled after update (with admin confirmation) |

> **Pre-checks determine eligibility for all modes.** Install, Update, and Uninstall all require a pre-check run (or re-use a recent one) before execution proceeds.

---

## 11. Execution Modes

### 11.1 Per-Package Execution Order (Install & Update)

For every package processed during Install or Update mode, the Agent follows this exact sequence:

```
1. DETECT         -- run detection via manifest definition (reality check)
2. PRE_INIT_STEP  -- execute each command in preInitSteps[] via cmd.exe /c
3. INSTALL/UPDATE -- run installCommand + installArgs via cmd.exe /c
4. POST_INIT_STEP -- execute each command in postInitSteps[] via cmd.exe /c
5. VERIFY         -- re-detect to confirm installation succeeded
6. REPORT         -- post step result to Orchestrator
```

**Step failure behavior:**

| Failure point | preInitSteps ran? | postInitSteps ran? | Package outcome |
|---|---|---|---|
| preInitStep fails | Partial | No | Abort install for this package -> FAILED |
| Installer fails | Yes | No | Skip postInitSteps -> FAILED |
| postInitStep fails | Yes | Yes (installer succeeded) | Step flagged as WARNING -> PARTIAL_SUCCESS |
| Package skipped | No | No | Neither set runs |

> `postInitStep` failure does not fail the package installation itself since the installer already succeeded. It is flagged as `PARTIAL_SUCCESS` / `WARNING` so admins are aware without misrepresenting the installed state.

---

### 11.2 Install Mode

```
Select Agent + Workload
        |
  Run PRE_CHECK task
        |
  DB Reconcile (Agent reality -> AgentPackages in DB)
        |
  Evaluate delta
  +-------------+------------------+---------------------+
  |   0/N       |      X/N         |        N/N           |
  |  installed  |    installed     |      installed       |
  |             |  (partial)       |   (all present)      |
  +-------------+------------------+---------------------+
  | Install all | Skip X existing  | Skip everything      |
  |             | Install missing  | Pipeline -> SKIPPED  |
  +------+------+--------+---------+---------------------+
         |               |
     Verify installation (re-detect via manifest detection)
         |
  DB Reconcile + Assign Workload to AgentNode
```

After successful install (all edge cases): update `AgentNode.assignedWorkloadId` and `assignedWorkloadVersion`.

---

### 11.3 Update Mode

```
Select Agent Node
        |
  [Optional: auto pre-check to surface agent's current state]
        |
  Select new Workload version
  -> If selected version <= Agent's current assigned version -> REJECT (downgrade blocked)
        |
  Run PRE_CHECK (targeted to new workload packages)
  DB Reconcile
        |
  Compute full delta:
    - Packages to install   (in new workload, not on Agent)
    - Packages to update    (in new workload, Agent has older version)
    - Packages to skip      (in new workload, Agent version matches)
    - Packages to uninstall (in old workload, absent from new workload = orphans)
        |
  Show delta summary to admin (including orphans to be removed)
  Admin confirms before execution proceeds
        |
  Phase 1: Install / update new workload packages
        |
  Verify new packages
        |
  Phase 2: Uninstall orphaned packages (reuses uninstall execution path)
        |
  Verify removals
        |
  DB Reconcile -> Update AgentNode assignedWorkload to new version
```

**Package-level version handling during Update:**

| Agent version vs. Workload version | Action |
|---|---|
| Not installed | Install |
| Older | Update (per `updateStrategy` in manifest) |
| Same | Skip |
| Newer (agent ahead) | Reject that package — flag in delta, block run |
| In old workload, absent from new workload | Uninstall (orphan auto-removal, after confirmation) |

**DB-has-no-record but Agent-has-packages edge case:** Caught during pre-check after workload selection. DB reconcile updates `AgentPackages` to reflect reality first, then update proceeds normally.

---

### 11.4 Uninstall Mode

```
Select Agent Node
  (UI shows only workloads currently assigned to this Agent)
        |
  Select workload to uninstall
        |
  Run PRE_CHECK (confirm packages actually exist on Agent)
        |
  For each package in workload:
    Run uninstallCommand + uninstallArgs from manifest via cmd.exe /c
    Re-detect to confirm removal
        |
  DB Reconcile:
    - Remove AgentPackage records for uninstalled packages
    - Clear AgentNode.assignedWorkloadId / assignedWorkloadVersion
```

> `preInitSteps` / `postInitSteps` are **not executed** during Uninstall mode in MVP. A future `preUninstallSteps` field can be introduced if service-stop or teardown steps are needed before removal.

---

## 12. State Machines

### WorkloadRun Status
```
PENDING -> RUNNING -> SUCCESS
                   -> FAILED
                   -> SKIPPED  (all packages already at correct version -- nothing to do)
```

### AgentNode Status
```
UNREGISTERED -> REGISTERED -> WORKLOAD_ASSIGNED -> NEEDS_UPDATE
                                   |__________________________|^

REGISTERED / WORKLOAD_ASSIGNED -> LOST  (no heartbeat within pollingInterval x LostThresholdMultiplier)
LOST -> REGISTERED  (Agent comes back online and resumes heartbeat)
```

---

## 13. Workload Version Semantics (Update Mode)

| Package scenario (v1 -> v2) | Action |
|---|---|
| In v1 AND v2, same version | Skip |
| In v1 AND v2, v2 higher | Update |
| In v1 AND v2, v2 lower | Reject (downgrade) |
| Only in v2 (new package) | Install |
| Only in v1 (removed in v2) | Auto-uninstall after new packages confirmed (admin confirms delta first) |

---

## 14. MVP Scope Boundary

| Feature | MVP | Post-MVP |
|---|---|---|
| Upload artifact (binary + manifest, single) | Yes | |
| Bulk artifact import via flat ZIP | Yes | |
| Upload workload definition JSON (single or bulk array) | Yes | |
| Enrollment token generation (UI + API) | Yes | |
| Agent single-command enrollment (--enroll + SCM self-register) | Yes | |
| Agent single-command reset (--reset + SCM stop/delete + agent.json deletion) | Yes | |
| Agent registration + heartbeat + LOST detection | Yes | |
| agentSecret bearer auth on all Agent requests | Yes | |
| Polling interval configurable via appsettings.json + agent.json | Yes | |
| Pre-checks (targeted scan per workload) | Yes | |
| DB reconciliation (Agent reality -> AgentPackages) | Yes | |
| Delta summary (MATCHES / MISSING / VERSION_DRIFT / AHEAD / ORPHANED) | Yes | |
| Install mode (all 3 edge cases) | Yes | |
| Update mode (incl. downgrade rejection + orphan auto-uninstall with confirmation) | Yes | |
| Uninstall mode | Yes | |
| preInitSteps / postInitSteps (Install + Update only) | Yes | |
| Per-step audit log with exit codes | Yes | |
| updateStrategy in manifest (overinstall / reinstall) | Yes | |
| Detection types: registry, filePath | Yes | |
| Detection type: wmi | Stretch | |
| One active WorkloadRun per Agent at a time | Yes | |
| Workload upsert on conflict | Yes | |
| Artifact duplicate rejection on import | Yes | |
| /download-agent page with auto-filled URL + token | | Yes |
| Real-time task dispatch (SignalR / WebSocket) | | Yes |
| Full agent-wide package scan (FULL_SCAN mode) | | Yes |
| Rollback on failure | | Yes |
| Multi-agent batch dispatch | | Yes |
| Scheduled / cron runs | | Yes |
| preUninstallSteps on uninstall mode | | Yes |

---

## 15. Build Order

### Phase 1 — Foundation
1. SQLite schema + EF Core models (all tables)
2. Orchestrator `appsettings.json` configuration wiring (polling interval, LOST threshold, artifact store path, token TTL)
3. Enrollment token generation endpoint
4. Agent enrollment endpoint + `agent.json` config writing (Agent side)
5. Agent `--enroll` CLI mode (enroll API call + agent.json write + SCM CreateService + StartService)
6. Agent `--reset` CLI mode (unregister API call + SCM StopService + DeleteService + agent.json delete)
7. Artifact single upload endpoint + disk store (binary + manifest pair validation)
8. Artifact bulk import endpoint (flat ZIP, stem-matched pairs, duplicate rejection)
9. Workload single + bulk upload (JSON object or array auto-detect, upsert on conflict)

### Phase 2 — Core Pipeline
10. Agent polling endpoint + task queue (WorkloadRuns, PENDING -> RUNNING transition)
11. Agent heartbeat + LOST detection (configurable threshold from appsettings)
12. Pre-check task dispatch + Agent detection execution (registry + filePath via native .NET APIs)
13. DB reconciliation logic (AgentPackages upsert from Agent reality report)
14. Delta summary computation (MATCHES / MISSING / VERSION_DRIFT / AHEAD / ORPHANED)

### Phase 3 — Execution Modes
15. Install mode — Orchestrator dispatch + Agent execution (0/N, X/N, N/N edge cases)
16. Update mode — downgrade rejection, Phase 1 install/update, Phase 2 orphan auto-uninstall, admin confirmation gate
17. Uninstall mode

### Phase 4 — Web UI (React)
18. Artifact upload page (single binary + manifest pair)
19. Artifact bulk import page (ZIP upload with per-artifact import summary)
20. Workload upload page (single object or bulk JSON array, upsert feedback)
21. Enrollment token generation page
22. Agent nodes list (status badge, assigned workload, last seen timestamp, LOST indicator)
23. Run wizard: Select Agent -> Select Workload -> Choose Mode -> Pre-check -> Execute
24. Delta summary view (per-package status table, ORPHANED packages highlighted with uninstall callout)
25. Admin confirmation step before Update mode execution (shows full delta including orphan removals)
26. Run log / step audit view (per-command granularity, exit codes, timestamps)

---

## 16. Resolved Decisions

| # | Decision | Resolution |
|---|---|---|
| 1 | Detection types for MVP | `registry` + `filePath`. `wmi` is a stretch goal. |
| 2 | Update strategy default | `overinstall` unless manifest explicitly specifies `reinstall` |
| 3 | Network topology | Agent requires HTTP access to Orchestrator for polling + artifact download |
| 4 | Concurrency | One active WorkloadRun per Agent at a time |
| 5 | Agent identity | UUID generated by Orchestrator at enrollment, stored in `agent.json`. Deleted on `--reset`. Not hostname-based. |
| 6 | postInitStep failure severity | `PARTIAL_SUCCESS` / `WARNING` — does not mark the package as FAILED |
| 7 | Workload-level orphan handling | Auto-uninstall in MVP. Orphans (packages in old workload, absent from new) are uninstalled in Phase 2 of Update mode, after new packages are verified. Admin sees and confirms the full delta (including removals) before execution. Distinct from Agent-vs-DB mismatches, which are resolved by DB reconciliation during pre-checks. |
| 8 | preInitSteps / postInitSteps in Uninstall | Not executed in MVP. Deferred to `preUninstallSteps` post-MVP. |
| 9 | Enrollment token TTL | Default 24h, configurable via `Enrollment.TokenTtlHours` in Orchestrator `appsettings.json` |
| 10 | Task dispatch latency + polling config | Agent polls on interval from `agent.json` (set at enrollment from Orchestrator `DefaultPollingIntervalSeconds`). Max dispatch latency = one polling interval. Both polling interval and LOST threshold multiplier are configurable via Orchestrator `appsettings.json`. |
| 11 | Duplicate artifact on import | Reject — existing artifact record is preserved. Error surfaced in import summary. |
| 12 | Duplicate workload on import | Upsert — update fields that have changed, leave others as-is. |
