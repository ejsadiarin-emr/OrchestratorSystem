# Installation & Operational Storyboards

Date: 2026-04-13  
Status: Draft for review  
Scope: All installation, update, and modification flows for Distributed Installer PoC

## Purpose

This document provides step-by-step storyboards (flows) for all installation and operational scenarios:
- Fresh orchestrator deployment
- Fresh agent deployment  
- Agent bootstrap via WinRM
- Software updates via orchestrator
- Workload modification (orchestrator self-update, version changes)
- Package management without external sources
- Job retry/idempotency patterns

---

## 1. Orchestrator Deployment (Fresh Install)

### 1.1 Packaging Decision

| Option | Description | PoC Choice |
|--------|-------------|------------|
| ISO image | Bootable installer with all components | Deferred |
| Standalone EXE | Single self-contained executable | **Selected** |
| ZIP archive | Extractable archive for scripted deployment | Supported |
| Drag & drop | Manual file copy | Supported |

**PoC Decision**: Orchestrator ships as a single self-contained EXE that embeds:
- ASP.NET Core Kestrel server
- React UI assets (embedded)
- SQLite database (local file)
- OTel collector (embedded/exporting to configurable endpoint)

### 1.2 Fresh Orchestrator Deployment Flow

```
Step 1: Acquire Orchestrator Binary
+--------------------------------------------------+
| Source Option A: Drag & Drop                     |
| - Sysadmin copies Orchestrator.exe from USB/Repo |
| - Placed in C:\Program Files\DistributedInstaller|
|                                                  |
| Source Option B: Download Endpoint                |
| - POST /api/bootstrap/orchestrator-download     |
| - Returns self-contained EXE as binary stream   |
| - Requires pre-shared enrollment token          |
|                                                  |
| Source Option C: ZIP Extract                     |
| - Extracts to target directory                  |
| - Single EXE + appsettings.json                  |
+--------------------------------------------------+
                    |
                    v
Step 2: Initial Configuration
+--------------------------------------------------+
| - Run: Orchestrator.exe --init                  |
| - Prompts for:                                   |
|   * Orchestrator display name                    |
|   * Listen port (default: 5000)                 |
|   * Artifact storage path (local UNC or folder) |
|   * Initial admin credentials                    |
|   * OTel exporter endpoint (optional)            |
|   * SQLite database path                         |
|                                                  |
| Output: appsettings.json, enrollment token       |
+--------------------------------------------------+
                    |
                    v
Step 3: Verify Orchestrator Startup
+--------------------------------------------------+
| - Run: Orchestrator.exe                         |
| - Health check: GET http://localhost:5000/health |
| - Expected: {"status":"healthy","version":"1.0"}|
|                                                  |
| - UI verification: http://localhost:5000         |
| - Expected: React dashboard loads                |
|                                                  |
| - API verification: GET /api/nodes               |
| - Expected: [] (no agents registered yet)       |
+--------------------------------------------------+
```

### 1.3 Orchestrator Self-Update Flow

```
Sysadmin                    Orchestrator API              Artifact Storage
    |                              |                            |
    | POST /api/admin/update       |                            |
    | {version: "1.2.0"}          |                            |
    |----------------------------->|                            |
    |                              | GET /artifacts/orch/1.2.0  |
    |                              |--------------------------->|
    |                              | <Binary EXE + signature>    |
    |                              |<---------------------------|
    |                              |                            |
    |                              | Validate signature          |
    |                              | Verify version floor       |
    |                              |                            |
    |                              | [Backup current EXE]       |
    |                              | [Replace EXE]               |
    |                              |                            |
    |                              | Shutdown gracefully         |
    |                              |                            |
    <------------------------------|                            |
    | 200: Update scheduled         |                            |
    | Restart required              |                            |
    |                              |                            |
    | [Manual restart]             |                            |
    | OR                           |                            |
    | Auto-restart via wrapper     |                            |
```

**Key Security Points:**
- EXE must be signed by trusted publisher
- Version floor check prevents downgrade attacks (TH-007B)
- Backup enables rollback if startup fails
- Shutdown is graceful (complete active jobs first, configurable timeout)

---

## 2. Agent Bootstrap (Remote Machine)

### 2.1 Package Decision

| Option | Description | PoC Choice |
|--------|-------------|------------|
| ISO/Installer | Traditional MSI/EXE installer | Deferred |
| Single script | One-liner that downloads and installs | **Selected** |
| Manual steps | Step-by-step operator instructions | Supported |

**PoC Decision**: Single bootstrap script (PowerShell) run manually on target machine.

### 2.2 Agent Bootstrap Flow (WinRM)

```
Operator Workstation       Target Machine (Remote)       Orchestrator
        |                          |                          |
        | Step 1: Test connectivity|                          |
        | Test-NetConnection -Port 5985 |                    |
        |<------------------------->|                          |
        |                          |                          |
        | Step 2: Generate enrollment token                   |
        | POST /api/nodes/enroll                          |
        | {hostname: "WORKSTATION-01"}                     |
        |-------------------------------------------------->|
        | {token: "enr_a1b2c3...", nodeId: "node-001"}    |
        |<--------------------------------------------------|
        |                          |                          |
        | Step 3: Run bootstrap script (WinRM)              |
        | Invoke-Command -ComputerName TARGET                |
        | -ScriptBlock {                                    |
        |   # Download agent EXE from orchestrator          |
        |   Invoke-WebRequest -Uri "$ORCH/url/agent/download"|
        |   -OutFile "C:\ProgramData\DI\Agent.exe"          |
        |                                                    |
        |   # Create Windows Service                        |
        |   sc.exe create "DistributedInstallerAgent"       |
        |     binPath= "C:\ProgramData\DI\Agent.exe"        |
        |     DisplayName= "Distributed Installer Agent"    |
        |                                                    |
        |   # Configure with enrollment token                |
        |   Set-Content "C:\ProgramData\DI\appsettings.json" |
        |     '{\"orchestratorUrl\": \"...\",               |
        |       \"enrollmentToken\": \"enr_a1b2c3...\",       |
        |       \"nodeId\": \"node-001\"}'                    |
        |                                                    |
        |   # Start service                                 |
        |   Start-Service "DistributedInstallerAgent"        |
        | }                                                  |
        |------------------------->                         |
        |                          |                          |
        | Step 4: Verify registration                       |
        | GET /api/nodes/node-001                           |
        |-------------------------------------------------->|
        | {status: "online", agentVersion: "1.0.0"}         |
        |<--------------------------------------------------|
```

### 2.3 Agent Bootstrap Verification

| Step | Verification | Expected Result |
|------|--------------|-----------------|
| Connectivity | `Test-NetConnection -Port 5985` | TCP success |
| Service | `Get-Service "DistributedInstallerAgent"` | Running |
| Registration | `GET /api/nodes/{nodeId}` | Status: online |
| SignalR | Agent connects to hub | Hub logs: Agent registered |
| Heartbeat | Wait 15-30 seconds | LeaseHeartbeat received |

### 2.4 Agent Bootstrap (Manual Script Alternative)

For PoC, the following simplified flow is acceptable:

```powershell
# On target machine (run as Administrator):
$orchUrl = "https://orchestrator.internal:5000"
$token = "enr_a1b2c3d4..."  # From orchestrator UI

# Download agent
Invoke-WebRequest "$orchUrl/agent/download" -OutFile "$env:TEMP\Agent.exe"

# Install service
sc.exe create "DI Agent" binPath= "$env:TEMP\Agent.exe" start= auto
sc.exe config "DI Agent" obj= "NT AUTHORITY\LocalService"

# Configure
@{
    OrchestratorUrl = $orchUrl
    EnrollmentToken = $token
} | ConvertTo-Json | Set-Content "$env:ProgramData\DistributedInstaller\config.json"

# Start
Start-Service "DI Agent"

# Verify
Get-Service "DI Agent"
```

---

## 3. Software Installation (Fresh Install via Orchestrator)

### 3.1 Package Source Decision

| Challenge | Solution |
|-----------|----------|
| No external NuGet/Chocolatey | **Orchestrator serves as package source** |
| Agents need packages | Agents download from orchestrator endpoint |
| Large packages | Chunked download via `AcquireArtifact` step |

**Architecture**: Orchestrator hosts `/api/artifacts/{packageId}/{version}` endpoint.

### 3.2 Package Upload Flow (Admin)

```
Admin                    Orchestrator API              Artifact Storage
    |                              |                          |
    | POST /api/artifacts/upload   |                          |
    | Content-Type: multipart/form  |                          |
    | {packageId, version, file}   |                          |
    |----------------------------->|                          |
    |                              | Validate package metadata |
    |                              | Generate integrity hash   |
    |                              | Store artifact            |
    |                              |--------------------------->|
    |                              |                          |
    |                              | [Manifest created]        |
    |                              | {packageId, version,      |
    |                              |  integrity.hash,          |
    |                              |  signature}               |
    |<------------------------------|                          |
    | 201: Package uploaded        |                          |
```

### 3.3 Package Manifest Structure

```json
{
  "packageId": "nodejs",
  "targetVersion": "22.14.0",
  "displayName": "Node.js 22.x LTS",
  "description": "JavaScript runtime for build agents",
  "artifact": {
    "source": "/api/artifacts/nodejs/22.14.0",
    "type": "zip",
    "sizeBytes": 34567890,
    "integrity": {
      "algorithm": "sha256",
      "hash": "a1b2c3d4e5f6..."
    },
    "signature": "RSA-4096:CN=Emerson-Package-Signing"
  },
  "installAdapter": {
    "type": "executable",
    "command": "node-installer.exe",
    "arguments": "/quiet /install",
    "expectedExitCodes": [0, 3010],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "fileVersion",
    "path": "C:\\Program Files\\NodeJS\\node.exe",
    "expectedVersion": ">=22.0.0"
  },
  "rollback": {
    "supported": true,
    "method": "uninstall",
    "expectedExitCodes": [0]
  },
  "retryPolicy": {
    "retryable": true,
    "maxAttempts": 3,
    "backoffSeconds": [30, 60, 120],
    "retryableReasons": ["network_timeout", "installer_crashed"],
    "nonRetryableReasons": ["disk_full", "insufficient_privileges"]
  },
  "idempotency": {
    "mode": "detect",
    "detectionKey": "nodejs-installed",
    "behavior": "skip_if_present"
  }
}
```

### 3.4 Job Submission Flow

```
Sysadmin                  React UI                  Orchestrator API
    |                          |                          |
    | 1. Select target nodes   |                          |
    | 2. Select package        |                          |
    | 3. Set version          |                          |
    | 4. Submit job           |                          |
    |------------------------->|                          |
    |                          | POST /api/jobs          |
    |                          | {targets: ["node-001"],  |
    |                          |  manifest: {...}}       |
    |                          |------------------------->|
    |                          |                          |
    |                          | [Validate manifest]      |
    |                          | [Create job record]       |
    |                          | [Enqueue to Hangfire]     |
    |                          |<-------------------------|
    |                          | {jobId: "job-123"}       |
    | 5. Redirect to job view  |                          |
    |<-------------------------|                          |
```

### 3.5 Full Installation Flow (With Verification)

```
Sysadmin     UI      Orch     Hangfire   SignalR    Agent      ChildProc   Artifact
  |          |        |          |         |          |            |           |
  | submit   |        |          |         |          |            |           |
  |--------->|        |          |         |          |            |           |
  |          | POST   |          |         |          |            |           |
  |          |------->|          |         |          |            |           |
  |          |        | validate |         |          |            |           |
  |          |        |------|   |         |          |            |           |
  |          |        |      |   |         |          |            |           |
  |          |        | persist  |         |          |            |           |
  |          |        |<---|-----|         |          |            |           |
  |          |        |    | enqueue      |         |            |           |
  |          |        |    |------------->|         |            |           |
  |          |        |    |             |          |            |           |
  |          |        |    | dispatch    |         |            |           |
  |          |        |    |------------>|          |            |           |
  |          |        |    |             | AssignJob|            |           |
  |          |        |    |             |--------->|            |           |
  |          |        |    |             |          | enqueue    |           |
  |          |        |    |             |          |----------->|           |
  |          |        |    |             |          |            |           |
  |          |        |    |             |          | spawn      |           |
  |          |        |    |             |          |----------->|           |
  |          |        |    |             |          |            |           |
  |          |        |    |             |          | 1.PreCheck |           |
  |          |        |    |             |          | [Verify env]           |
  |          |        |    |             |          |            |<-----------|
  |          |        |    |             |          | 2.Acquire  |           |
  |          |        |    |             |          | GET artifact           |
  |          |        |    |             |          |------------------------>|
  |          |        |    |             |          |            |           |
  |          |        |    |             |          | 3.Validate |           |
  |          |        |    |             |          | [Check sig]            |
  |          |        |    |             |          |<-----------|           |
  |          |        |    |             |          |            |           |
  |          |        |    |             |          | 4.Detect   |           |
  |          |        |    |             |          | [Idempot. check]        |
  |          |        |    |             |          |<-----------|           |
  |          |        |    |             |          |            |           |
  |          |        |    |             |          | 5.Install  |           |
  |          |        |    |             |          | [Run EXE]  |           |
  |          |        |    |             |          |----------->|           |
  |          |        |    |             |          |            |           |
  |          |        |    |             |          | 6.Verify  |           |
  |          |        |    |             |          | [Check ver]|           |
  |          |        |    |             |          |<-----------|           |
  |          |        |    |             |          |            |           |
  |          |        |    |             | StepStatus           |           |
  |          |        |    |             |<---------|            |           |
  | view      |        |    |             |          |            |           |
  |<---------|        |    |             |          |            |           |
  |          | push  |    |             |          |            |           |
  |          |<------|    |             |          |            |           |
```

---

## 4. Update Installation Flow

### 4.1 Workflow: Upgrade Existing Package

```
Sysadmin                  Orchestrator API              Target Agent
    |                              |                          |
    | PUT /api/jobs (upgrade)      |                          |
    | {packageId: "nodejs",        |                          |
    |  targetVersion: "24.0.0"}    |                          |
    |----------------------------->|                          |
    |                              | [Detect current version]  |
    |                              | [Create job with mode: upgrade]|
    |                              |                          |
    |                              | AssignJob(manifest)      |
    |                              |-------------------------->|
    |                              |                          |
    |                              | [Execute upgrade pipeline]|
    |                              | [Snapshot config]        |
    |                              | [Run installer]          |
    |                              | [Verify]                 |
    |                              |<--------------------------|
    | Job completed: v22->v24      |                          |
    |<-----------------------------|                          |
```

### 4.2 Version Change: Node 22 → 24 (Example)

```
Package Manifest (Node 22 → 24):
{
  "packageId": "nodejs",
  "executionMode": "upgrade",
  "sourceVersion": ">=22.0.0",
  "targetVersion": "24.0.0",
  "artifact": {...},
  "upgradePath": {
    "from": "22.x",
    "to": "24.x",
    "steps": [
      "uninstall-22",
      "install-24"
    ]
  },
  "rollback": {
    "restoreFromSnapshot": true
  }
}
```

### 4.3 Version Downgrade Handling

Downgrades are **non-retryable by default** (high risk operation):

```json
{
  "downgradeAllowed": false,
  "forceDowngradeRequires": "explicit_approval",
  "riskLevel": "high",
  "rollbackGuaranteed": true
}
```

**Flow for forced downgrade:**
1. Sysadmin must explicitly acknowledge risk
2. Full config snapshot mandatory
3. Job marked as non-retryable
4. Manual verification required post-install

---

## 5. Modify Workload Flow

### 5.1 Orchestrator Self-Modification (Update)

```
Sysadmin                  Current Orchestrator           Artifact Storage
    |                              |                          |
    | POST /api/admin/self-update  |                          |
    | {version: "1.2.0",           |                          |
    |  reason: "security_patch"}   |                          |
    |----------------------------->|                          |
    |                              | [Download new version]    |
    |                              |-------------------------->|
    |                              |<--------------------------|
    |                              | [Verify signature]        |
    |                              | [Backup current binary]   |
    |                              |                          |
    |                              | [Stage update]           |
    |                              |                          |
    <------------------------------|                          |
    | 200: Restart required        |                          |
    |                              |                          |
    | [Restart orchestrator]       |                          |
    |                              | [Swap binary]           |
    |                              | [Start new version]      |
    |                              |                          |
    | Health check                 |                          |
    | GET /health                  |                          |
    |----------------------------->|                          |
    | {"version": "1.2.0"}         |                          |
    |<-----------------------------|                          |
```

### 5.2 Remote Machine Workload Modification

```
Sysadmin                  Orchestrator                   Agent
    |                          |                          |
    | POST /api/jobs           |                          |
    | {targets: ["node-001"],  |                          |
    |  manifest: {             |                          |
    |    mode: "modify",       |                          |
    |    packageId: "worker",   |                          |
    |    configChanges: {...}   |                          |
    |  }}                      |                          |
    |-------------------------->|                          |
    |                          | AssignJob(manifest)      |
    |                          |-------------------------->|
    |                          |                          |
    |                          | [Execute modification]    |
    |                          | [Apply config changes]    |
    |                          | [Restart affected service]|
    |                          |<--------------------------|
    |                          |                          |
    | Job completed            |                          |
    |<--------------------------|                          |
```

---

## 6. Package Management Without External Sources

### 6.1 Orchestrator as Package Source

```
Orchestrator Packages Endpoint:
GET  /api/artifacts/{packageId}/{version}     - Download artifact
HEAD /api/artifacts/{packageId}/{version}     - Check existence
POST /api/artifacts/upload                    - Upload new package
```

### 6.2 Package Upload Flow (Internal Only)

```
Admin                    Orchestrator
    |                        |
    | POST /api/artifacts/upload
    | Content-Type: multipart/form
    | Package: node-v22.14.0-win-x64.zip
    | Metadata: {packageId, version, ...}
    |------------------------>|
    |                         |
    | [Generate SHA-256 hash]
    | [Store artifact file]
    | [Create manifest entry]
    |                         |
    |<------------------------|
    | 201 Created
    | {manifestId: "..."}
```

### 6.3 Agent Package Acquisition

```
Agent                         Orchestrator Artifact Endpoint
   |                                       |
   | 1. Receive AssignJob(manifest)        |
   |                                       |
   | 2. AcquireArtifact step:              |
   |    GET /api/artifacts/{pkgId}/{ver}   |
   |-------------------------------------->|
   |                                       |
   |    [Stream artifact to local cache]  |
   |    [Verify hash match]                |
   |<--------------------------------------|
   |                                       |
   | 3. Proceed with installation          |
```

### 6.4 Chunked Download (Large Packages)

SignalR message size limit: ~1MB  
Large artifacts (>100MB): Use HTTP range requests

```
Agent                         Orchestrator
   |                                |
   | GET /api/artifacts/{pkg}/large.zip
   |   Range: bytes=0-10485759      |
   |------------------------------->|
   |                                |
   | [Return chunk 1]               |
   |<-------------------------------|
   |                                |
   | GET /api/artifacts/{pkg}/large.zip
   |   Range: bytes=10485760-20971519
   |------------------------------->|
   |                                |
   | [Return chunk 2]               |
   |<-------------------------------|
   |                                |
   | ... repeat until complete ...  |
   |                                |
   | [Verify combined hash]        |
```

---

## 7. Job Retry and Idempotency

### 7.1 Retry Policy Manifest Fields

```json
{
  "retryPolicy": {
    "retryable": true,
    "maxAttempts": 3,
    "backoffSeconds": [30, 60, 120],
    "retryableExitCodes": [0, 3010],
    "nonRetryableExitCodes": [1, 1602],
    "retryableErrorPatterns": [
      "network_timeout",
      "connection_refused",
      "temp_file_locked"
    ],
    "nonRetryableErrorPatterns": [
      "disk_full",
      "insufficient_privileges",
      "system_not_supported"
    ]
  }
}
```

### 7.2 Retry Flow

```
JobExecution                 Agent Pipeline
    |                           |
    | Execute step              |
    |--------------------------->|
    |                           |
    | Exit code: 1 (unknown)    |
    |<---------------------------|
    |                           |
    | Is retryable?             |
    | [Check exit code]         |
    |                           |
    | [YES]                     |
    | Wait 30s (backoff)        |
    | Retry step                |
    |--------------------------->|
    |                           |
    | [NO]                      |
    | Mark job as Failed        |
    | Emit failure event        |
    |                           |
```

### 7.3 Idempotency Patterns

```json
{
  "idempotency": {
    "mode": "detect",
    "detectionKey": "nodejs-v22.14.0",
    "behavior": "skip_if_present",
    "forceReinstall": false
  }
}
```

| Mode | Behavior | Use Case |
|------|----------|----------|
| `detect` | Skip if already satisfied | Standard installs |
| `always` | Always execute | Configuration changes |
| `never` | Fail if exists | Mutually exclusive installs |
| `version_check` | Upgrade/downgrade if version differs | Versioned packages |

### 7.4 Idempotency Flow

```
Agent Pipeline                      Detection
    |                                 |
    | Step: DetectCurrentState        |
    |---------------------------->    |
    |                                 |
    | Check: file exists?             |
    | Check: version matches?         |
    | Check: detection key matches?    |
    |                                 |
    | [Already satisfied]             |
    |<---- "SKIP"                     |
    |                                 |
    | [Not satisfied]                 |
    |<---- "PROCEED"                  |
    |                                 |
    | Emit StepStatus(PrecheckPassed) |
```

---

## 8. Security Flows

### 8.1 mTLS Certificate Flow

```
Enrollment (One-time)                Steady-state
       |                                    |
       | 1. Generate enrollment token       |
       | POST /api/nodes/enroll            |
       |---------------------------------->|
       |                                    |
       | {token: "enr_...", nodeId: "n1"}  |
       |<----------------------------------|
       |                                    |
       | 2. Agent connects with token       |
       | SignalR: Connect(auth: token)      |
       |---------------------------------->|
       |                                    |
       | 3. Orchestrator validates token     |
       | 4. Generate agent certificate       |
       |    CN: node-{nodeId}               |
       | 5. Return cert to agent            |
       |<----------------------------------|
       |                                    |
       | 6. Agent stores cert in store      |
       |    (Windows Cert Store)            |
       |                                    |
       | 7. Disconnect                      |
       |                                    |
       | 8. Reconnect with mTLS             |
       | SignalR: Connect(clientCert)       |
       |---------------------------------->|
       |                                    |
       | 9. Orchestrator validates cert     |
       |    - IssuedBy trusted CA?         |
       |    - CN matches nodeId?            |
       |    - Not expired?                  |
       |    - Not revoked?                  |
       |                                    |
       | 10. Connection accepted            |
       |<----------------------------------|
```

### 8.2 Ping vs LeaseHeartbeat

```
Ping (Orchestrator → Agent)         LeaseHeartbeat (Agent → Orchestrator)
         |                                    |
         | Periodic liveness check            | Periodic lease renewal
         | (every 60s)                        | (every 15s, PoC default)
         |                                     |
         | No response = agent offline         | No heartbeat = lease expired
         |                                     |
         | Used for:                           | Used for:
         | - Dashboard node status             | - Job ownership validation
         | - Connection health                | - Stale detection
         |                                     | - Reassignment trigger
```

### 8.3 Child Process Security

```
Agent Service                    Child Process (Job)
     |                                  |
     | Spawn with constrained token     |
     |---------------------------->     |
     |                                  |
     | [Reduced privileges]              |
     | [Job-specific user]              |
     | [Resource limits]                |
     |                                  |
     | Monitor for anomalies            |
     |<---- stdout/stderr stream        |
     |                                  |
     | Kill on timeout/cancellation     |
     |---------------------------->     |
     |                                  |
     | Collect exit code                |
     |<---- exit(exitCode)              |
```

**Security controls on child process:**
- Run as non-elevated user where possible
- Restricted token with minimal privileges
- No network access (outbound blocked)
- CPU/memory limits via Job Object
- Timeout hard-kill after grace period
- Arguments sanitized before invocation

### 8.4 Package Signature Verification Flow

```
Agent Pipeline
     |
     | Step: ValidateSignatureAndHash
     |------------------|
     |
     | 1. Download artifact
     | 2. Compute SHA-256 hash
     | 3. Compare with manifest hash
     |
     | [MISMATCH] → FAIL: artifact_tampered
     |
     | 4. Verify Authenticode signature
     |    - Check publisher cert chain
     |    - Verify not revoked
     |
     | [INVALID SIG] → FAIL: signature_invalid
     |
     | 5. Emit trust evidence
     |    - signed_by: "CN=Emerson-Signing"
     |    - signed_at: timestamp
     |
     | [PASS] → Proceed to installation
```

---

## 9. SQLite for Persistence + OTel

### 9.1 SQLite as Primary Database

**Rationale**: SQLite is simpler for PoC and air-gapped deployments.

```
Orchestrator
   |
   +-- appsettings.json
   |   {
   |     "Database": {
   |       "Provider": "sqlite",
   |       "ConnectionString": "Data Source=orchestrator.db"
   |     }
   |   }
   |
   +-- orchestrator.db (SQLite file)
       |
       +-- Jobs
       +-- Nodes
       +-- AssignmentLeases
       +-- ConfigSnapshots
       +-- AuditEvents
```

### 9.2 SQLite for OTel Logs

**Question**: Does SQLite have a plugin for storing logs?

**Answer**: SQLite can store logs directly via EF Core, but for OTel we recommend:

| Option | Description | PoC Choice |
|--------|-------------|------------|
| SQLite via OTel exporter | Write to SQLite table | **Supported** |
| File-based (OTLP JSON) | Write to rotating log files | **Recommended** |
| PostgreSQL | External database | Deferred |
| OTel Collector | Forward to collector | Supported |

**Recommended OTel Storage for PoC:**

```json
{
  "Otel": {
    "Exporter": "otlp-file",
    "FilePath": "C:\\ProgramData\\DistributedInstaller\\otel\\logs",
    "Rotation": {
      "MaxSizeMB": 100,
      "MaxFiles": 10
    }
  }
}
```

### 9.3 SQLite Schema for OTel (Optional)

```sql
CREATE TABLE OtelLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc TEXT NOT NULL,
    Level TEXT NOT NULL,
    Service TEXT NOT NULL,
    JobId TEXT,
    NodeId TEXT,
    StepName TEXT,
    Message TEXT,
    TraceId TEXT,
    SpanId TEXT
);

CREATE INDEX idx_logs_timestamp ON OtelLogs(TimestampUtc);
CREATE INDEX idx_logs_jobid ON OtelLogs(JobId);
```

---

## 10. Trust Boundary Annotations (Updated)

### 10.1 Trust Boundary Definitions

| ID | Boundary Name | Components | Data Flow | Threats | Mitigations |
|----|---------------|------------|-----------|---------|-------------|
| TB-01 | Admin → Orchestrator | Sysadmin/UI → REST API | Job commands, auth context | Spoofing, privilege abuse | RBAC, audit logging |
| TB-02 | Agent → Orchestrator Hub | Agent → SignalR Hub | Assign/Ack/Lease/Status | Agent spoofing, replay | mTLS, enrollment token, sequence |
| TB-03 | Orchestrator → Artifact Store | API → Local storage | Package upload/download | Tampering, substitution | Signature verification, hash check |
| TB-04 | Agent → Orchestrator (Packages) | Agent → Artifact endpoint | Artifact download | MITM, tampering | HTTPS, hash verification |
| TB-05 | Orchestrator → SQLite | DB operations | Job state, audit events | Data tampering | File ACLs, SQLite integrity |
| TB-06 | Agent → Child Process | Job execution | Process spawn, IPC | Privilege escalation | Constrained token, Job Object |

### 10.2 Trust Boundary Diagram (Extended)

```text
+-------------------------------------------------------------------+
|                        TRUSTED ZONE                                |
|                                                                    |
|  +------------------+     +----------------------------------+     |
|  |   Sysadmin UI    |     |        Orchestrator              |     |
|  |  (Administrator) |     |  +-----------+  +--------------+ |     |
|  +--------+---------+     |  | REST API  |  | SignalR Hub  | |     |
|           |               |  +-----+-----+  +------+------+ |     |
|           | HTTPS/RBAC    |        |              |          |     |
+-----------|---------------+--------|--------------|----------+-----+
            |                       |              |          |
TB-01       | TB-02                 | TB-05        | TB-04    |
            |                       |              |          |
            v                       v              v          v
+---------------------------+  +-----------------------------------+
|      UNTRUSTED ZONE       |  |         UNTRUSTED ZONE            |
|                           |  |                                   |
|  +------------------+      |  |  +---------------------------+    |
|  |  Agent Service   |      |  |  |    Artifact Storage       |    |
|  |  (Remote Node)   |      |  |  |    (Local filesystem)     |    |
|  +--------+---------+      |  |  +---------------------------+    |
|           |                 |  |                                   |
|           | TB-06           |  |                                   |
|           v                 |  |                                   |
|  +------------------+      |  |                                   |
|  | Child Process    |      |  |                                   |
|  | (Job Execution)  |      |  |                                   |
|  +------------------+      |  |                                   |
|                           |  |                                   |
+---------------------------+  +-----------------------------------+
```

---

## 11. Windows to Linux (Future)

### 11.1 Cross-Platform Storyboard (Placeholder)

```
Phase 1 (PoC): Windows only
  - Orchestrator: Windows
  - Agents: Windows

Phase 2 (Future):
  - Orchestrator: Windows (with .NET cross-platform)
  - Agents: Windows + Linux

Cross-platform considerations:
  - Adapter abstraction (Windows installers vs Linux packages)
  - Manifest per-OS with conditional steps
  - SignalR works on both platforms (WebSocket)
  - mTLS cert generation must support Linux key stores
```

---

## 12. Verification Checklist

### Orchestrator Deployment
- [ ] EXE launches without .NET installed
- [ ] Health endpoint returns 200
- [ ] UI accessible at root URL
- [ ] SQLite database created
- [ ] Enrollment token generated

### Agent Bootstrap
- [ ] WinRM connectivity confirmed
- [ ] Service installed and running
- [ ] Agent registered in orchestrator
- [ ] SignalR connection established
- [ ] LeaseHeartbeat received

### Installation Job
- [ ] Package uploaded to orchestrator
- [ ] Job submitted via API
- [ ] Job queued and dispatched
- [ ] Agent receives AssignJob
- [ ] Pipeline executes all steps
- [ ] StepStatus updates received
- [ ] Job reaches terminal state
- [ ] SQLite records match job state

### Update Job
- [ ] Version change detected
- [ ] Config snapshot created
- [ ] Upgrade executes
- [ ] Verification passes
- [ ] New version confirmed on node

### Security
- [ ] mTLS certificate validated
- [ ] Enrollment token single-use
- [ ] Package signature verified
- [ ] Hash mismatch blocked
- [ ] Unauthorized role denied
- [ ] Audit events emitted

---

## 13. Open Questions (For Team Review)

1. **Chunked download threshold**: What size triggers range requests vs single GET?
2. **SQLite concurrent writes**: Single orchestrator = single writer. Acceptable?
3. **OTel retention**: How long to keep logs in PoC? (File rotation policy)
4. **Agent self-update**: Same flow as orchestrator, or different mechanism?
5. **Cross-platform priority**: Is Linux agent needed in PoC scope?

---

## 14. Related Documents

- Architecture: `03-architecture-and-design.md`
- Security Pack: `09-security-pack.md`
- Core Contracts: `10-core-contracts-pack.md`
- Bootstrap: `04-agent-bootstrap-and-communication.md`
- Install Sequence Diagram: `diagrams/install-sequence.ascii.md`
- Architecture Diagram: `diagrams/architecture.ascii.md`
