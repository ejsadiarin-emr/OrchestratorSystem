# Installation and Operational Storyboards

Date: 2026-04-14
Status: Canonical for PoC Phase 1
Scope: Windows-first, single-orchestrator distributed installer

---

## Purpose

Step-by-step flows for installation and operations:

- Fresh orchestrator deployment
- Fresh agent deployment
- Agent bootstrap via WinRM
- Software install/update/modify/rollback via orchestrator
- Package management (internal-only source)
- Job retry/idempotency patterns

---

## Hard Constraints

| Constraint | Value |
|---|---|
| Platform | Windows-first |
| Orchestrators | Single (no HA) |
| Package source | Internal-only (orchestrator artifact store) |
| SignalR | Control/status only (no artifact bytes) |
| Artifact transfer | HTTP (range/chunk for large files) |
| Orchestrator package | Self-contained single EXE with embedded UI |
| Runtime surface | API/CLI only (scripts are provisioning-only) |

**Canonical runtime sequence**: `Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

---

## 1. Orchestrator Packaging and Fresh Install

### Packaging Decision

| Option | Description | PoC Choice |
|--------|-------------|------------|
| Self-contained EXE | Clean-host startup | **Selected** |
| ZIP bundle | Easy transfer | Supported |
| ISO media | Offline distro | Deferred |

### Build and Sign Flow

```
DevOps CI         Signing Service     Artifact Repo     Operator
   |                   |                |              |
   | build/test        |                |              |
   | dotnet publish --self-contained   |              |
   |----------------->|                |              |
   |                  | sign + checksums + manifest   |
   |<----------------|                |              |
   | publish media ------------------------->|  EXE/ZIP
   |                  |                |              |
   |                  |                | download+verify
```

### Fresh Orchestrator Install Flow

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

### Orchestrator Self-Update Flow

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

### Startup Mockup

```
+---------------------------------------------------------------+
| Distributed Installer Orchestrator                            |
| Version: 1.0.x                                                |
|---------------------------------------------------------------|
| Service Status   : RUNNING                                    |
| API Endpoint     : https://localhost:5000                     |
| Hub Endpoint     : /runtime-hub                              |
| SQLite           : C:\ProgramData\DI\orchestrator.db         |
| Artifact Path    : C:\ProgramData\DI\artifacts               |
| Telemetry Export : file (rotation enabled)                    |
|---------------------------------------------------------------|
| Health Checks                                                  |
| [PASS] API host                                               |
| [PASS] SQLite connectivity                                    |
| [PASS] Artifact path ACL write/read                          |
| [PASS] Embedded UI assets                                    |
+---------------------------------------------------------------+
```

### Verification

- [ ] `GET /health` returns healthy
- [ ] Embedded UI loads
- [ ] `GET /api/nodes` works
- [ ] SQLite initializes
- [ ] Artifact path is writable

Traceability: FR-001, NFR-005, AC-001, AC-105

---

## 2. Agent Bootstrap (Token -> mTLS)

### Bootstrap Decision

| Method | Benefit | Phase 1 |
|--------|---------|---------|
| PowerShell script | Fast for PoC | **Selected** |
| WinRM remoting | Remote convenience | Supported |
| GPO/SCCM | Fleet scale | Considered, not selected |

### Bootstrap Flow

```
Operator WS        Target Machine (WinRM)     Agent Service     Orchestrator
    |                    |                     |                  |
    | POST /api/nodes/enroll -------------------------->|
    |<------------------------- token,nodeId,ttl -----|
    | Invoke-Command (WinRM)                                    |
    |  - download Agent.exe                                    |
    |  - write config (orchUrl, token, nodeId)                 |
    |  - sc.exe create service                                |
    |  - Start-Service                                        |
    |--------------------->| install/start -------->|         |
    |                      |                     | Connect(token) ->|
    |                      |                     |<-- bind cert   |
    |                      |                     | reconnect(mTLS) ->|
    |                      |                     |<-- accepted     |
    | GET /api/nodes/{nodeId} -------------------------->|
    |<------------------------- status=online,auth=mtls ---|
```

### Bootstrap Script

```powershell
# .\bootstrap-agent.ps1 -OrchestratorUrl "https://orch:5000" -Token "enr_..."

param(
    [Parameter(Mandatory=$true)] [string]$OrchestratorUrl,
    [Parameter(Mandatory=$true)] [string]$Token,
    [Parameter(Mandatory=$false)] [string]$NodeId = $env:COMPUTERNAME
)

$ErrorActionPreference = "Stop"
$AgentPath = "$env:ProgramData\DistributedInstaller\Agent"
$ConfigPath = "$AgentPath\config.json"
$AgentExe = "$AgentPath\Agent.exe"

function Invoke-Rollback {
    $svc = Get-Service -Name "DistributedInstallerAgent" -EA SilentlyContinue
    if ($svc.Status -eq 'Running') { Stop-Service $svc -Force }
    $def = Get-WmiObject -Class Win32_Service -Filter "Name='DistributedInstallerAgent'" -EA SilentlyContinue
    if ($def) { $def.Delete() }
    if (Test-Path $AgentPath) { Remove-Item $AgentPath -Recurse -Force }
    Write-Host "Rollback complete"
}

try {
    # 1. Verify connectivity
    Invoke-WebRequest "$OrchestratorUrl/health" -UseBasicParsing -TimeoutSec 30 | Out-Null

    # 2. Create install dir
    if (-not (Test-Path $AgentPath)) { New-Item -ItemType Directory $AgentPath -Force | Out-Null }

    # 3. Download agent
    Invoke-WebRequest "$OrchestratorUrl/agent/download" -OutFile $AgentExe -TimeoutSec 300

    # 4. Write config
    @{
        orchestratorUrl = $OrchestratorUrl
        enrollmentToken = $Token
        nodeId = $NodeId
        installTimeUtc = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json | Set-Content $ConfigPath -Encoding UTF8

    # 5. Create service
    $existing = Get-WmiObject -Class Win32_Service -Filter "Name='DistributedInstallerAgent'" -EA SilentlyContinue
    if ($existing) { $existing.Delete() }
    New-Service -Name "DistributedInstallerAgent" -BinaryPathName $AgentExe `
        -DisplayName "Distributed Installer Agent" -StartupType Automatic

    # 6. Start
    Start-Service "DistributedInstallerAgent"
    Start-Sleep 5

    # 7. Verify
    $svc = Get-Service -Name "DistributedInstallerAgent"
    if ($svc.Status -ne 'Running') { throw "Service not running" }

    Write-Host "SUCCESS: Node registered"
    exit 0
} catch {
    Write-Host "ERROR: $_"
    Invoke-Rollback
    exit 1
}
```

### Failure Cleanup

```
Install files -> Create service -> Write config -> Start service
       |
       +-- failure at any step
               |
               v
     Reverse cleanup: Stop -> Remove service -> Delete files
                    -> Delete config -> Invalidate token -> Audit
```

### Verification

- [ ] Windows service running
- [ ] Node appears online
- [ ] Lease heartbeat observed
- [ ] Token cannot be reused
- [ ] Invalid cert reconnect rejected
- [ ] Cleanup leaves no residue

Traceability: FR-004, NFR-002, AC-005, AC-102

---

## 3. Package Ingestion (Internal-Only Source)

### Policy

- Agents don't fetch from external sources at runtime
- Upstream binaries ingested into orchestrator artifact store first
- Artifact records are immutable and hash-bound

### Ingestion Flow

```
Admin                API                  Artifact Store         Audit
  |                  |                         |                  |
  | POST /api/artifacts/upload                |                  |
  |----------------->|                         |                  |
  |                  | write artifact -------->|                  |
  |                  | compute digest         |                  |
  |                  | verify trust metadata |                  |
  |                  | create immutable record ---------------->|
  |                  | emit ingest event ------------------------>|
  |<-----------------| 201 Created            |                  |
```

### Package Manifest

```json
{
    "packageId": "nodejs",
    "displayName": "Node.js 24 LTS",
    "version": "24.0.0",
    "channel": "stable",
    "artifact": {
        "source": "/api/artifacts/nodejs/24.0.0",
        "path": "artifacts/nodejs/24.0.0/nodejs-win-x64.zip",
        "fileName": "nodejs-win-x64.zip",
        "type": "zip",
        "sizeBytes": 34567890,
        "digest": {
            "algorithm": "sha256",
            "value": "<immutable-content-hash>"
        },
        "signature": {
            "type": "authenticode-or-detached",
            "publisher": "CN=VendorOrInternalSigning",
            "verification": "pass|warn|fail"
        }
    },
    "installAdapter": {
        "type": "exe|msi|scripted",
        "command": "node-installer.exe",
        "arguments": "/quiet /install",
        "expectedExitCodes": [0, 3010],
        "timeoutSeconds": 900
    },
    "detection": {
        "type": "fileVersion|registry|custom",
        "path": "C:\\Program Files\\nodejs\\node.exe",
        "expectedVersion": ">=24.0.0"
    },
    "rollback": {
        "supported": true,
        "method": "uninstall|restore_snapshot",
        "expectedExitCodes": [0]
    },
    "retryPolicy": {
        "maxAttempts": 3,
        "backoffSeconds": [30, 60, 120],
        "retryableReasons": ["network_timeout", "connection_reset"],
        "nonRetryableReasons": ["disk_full", "insufficient_privileges"]
    },
    "idempotency": {
        "mode": "version_check",
        "behavior": "skip_if_present"
    },
    "originMetadata": {
        "source": "vendor-repo-or-internal-mirror",
        "publisher": "vendor-if-known",
        "ingestedBy": "operator-or-process-id",
        "ingestedAtUtc": "timestamp",
        "verificationResult": "pass|warn|fail"
    },
    "policyTags": {
        "retryabilityClass": "transient_only",
        "idempotencyMode": "version_check",
        "riskLevel": "medium",
        "approvalRequired": false
    }
}
```

### Verification

- [ ] Ingest audit event exists with origin metadata
- [ ] Digest is immutable for version record
- [ ] Channel is `stable|canary|test`
- [ ] Invalid trust evidence blocked

Traceability: FR-001, FR-005, NFR-002, AC-006, AC-102

---

## 4. Artifact Delivery (HTTP, Not SignalR)

### Transport Boundary

| Transport | Purpose | Used for artifacts? |
|-----------|---------|---------------------|
| SignalR | Control/status messages | No |
| HTTP GET/HEAD/RANGE | Artifact bytes | Yes |

**Key**: SignalR carries the assignment message with artifact reference (URL), but bytes flow over HTTP.

### Assignment Message (SignalR)

```json
{
  "jobId": "...",
  "artifactReference": {
    "url": "/api/artifacts/nodejs/24.0.0",
    "digest": "sha256:..."
  }
}
```

### Artifact Bytes (HTTP)

```
GET /api/artifacts/nodejs/24.0.0
-> Binary payload via HTTPS
```

### Range/Chunk Sequence

```
Agent                    Artifact API
  |                           |
  | HEAD /api/artifacts/pkg   |
  |------------------------->|
  | <-- 200 + Content-Length |
  |                           |
  | GET range bytes=0-10MB   |
  |------------------------->|
  | <-- 206 chunk#1          |
  |                           |
  | GET range bytes=10-20MB  |
  |------------------------->|
  | <-- 206 chunk#2          |
  |                           |
  | ... repeat until done     |
  |                           |
  | validate assembled digest |
```

### Failure Branch

```
Chunk fetch fails -> classify
      |
      +-- transient -> bounded retry with backoff
      |
      +-- non-transient -> fail step with reason code
```

### Verification

- [ ] Artifact bytes never flow via SignalR
- [ ] Range requests work for large artifacts
- [ ] Digest verification blocks corrupted downloads

Traceability: FR-005, NFR-002, AC-006, AC-102

---

## 5. Job Submission and Runtime Protocol

### Operator Flow

1. Select target nodes
2. Select package + version/channel
3. Confirm operation (`install|update|modify`)
4. `POST /api/jobs` with manifest
5. Orchestrator validates, persists, enqueues
6. Hub sends `AssignJob`; agent returns `AckClaim`
7. View live timeline

### Submission and Dispatch

```
Sysadmin      UI/CLI       Orch API      SQLite     Hangfire   SignalR Hub    Agent
   |             |             |            |           |            |            |
   | select + submit          |            |           |            |            |
   |------------>| POST /jobs ->| validate   |           |            |            |
   |             |             | persist job -->|           |            |            |
   |             |             | persist assn -->|          |            |            |
   |             |             | enqueue dispatch --------->|            |            |
   |             |<------------| 202 jobId  |           |            |            |
   |             |             |            |          | dequeue     |            |
   |             |             |            |          |----------->| AssignJob -->|
   |             |             |            |          |            |<-- AckClaim --|
   |             |             | update lease -->|          |            |            |
   |             |             |            |          |            |<-- StepStatus |
   | timeline <----------------------------- update step/job     |            |            |
```

### Canonical Protocol Sequence

```
Agent                SignalR Hub         Orchestrator
  |                      |                   |
  | Connect ----------->|                   |
  | Register/Auth ----->|                   |
  |<-- AssignJob -------|                   |
  | AckClaim ---------->|                   |
  | LeaseHeartbeat ---->| update lease -->| |
  | StepStatus(seq=1) ->| upsert step -->| |
  | StepStatus(seq=2) ->| upsert step -->| |
  | StepStatus(seq=n) ->| upsert step -->| |
  | Complete/Fail ----->| terminal ----->| |
  | LeaseClose -------->| close lease -->| |
```

### Idempotency Rules

- Upsert key: `(jobId, stepId, sequence)`
- Same key + same payload = safe replay
- Same key + different payload = reject + audit `sequence_payload_conflict`

### Reconnect Behavior

```
Agent           Hub         StepStatus Ingest      SQLite
  |              |                |                |
  | disconnect   |                |                |
  |------------->|                |                |
  |              | read last ack ->|                |
  |              |<-- K=2 --------|<---------------| 
  | Connect ---->|                |                |
  | Register --->|                |                |
  |<-- resume(K) |                |                |
  |              |                |                |
  | resend seq=3 ->| upsert ---->|                |
  | resend seq=4 ->| upsert ---->|                |
  | stale seq=2 -->| reject + audit                |
```

**Resume rule**: Agent resumes strictly from `lastAcknowledgedSequence + 1`

### Verification

- [ ] Request rejected if policy invalid/missing
- [ ] Assignment after persistence success
- [ ] AckClaim has assignment/lease identifiers
- [ ] Replay with equal payload is safe
- [ ] Payload conflict rejected and audited

Traceability: FR-001, FR-002, AC-001, AC-003, AC-101

---

## 6. Lease and Liveness

### Semantic Split

| Signal | Direction | Meaning | Used for |
|--------|-----------|---------|----------|
| Ping | Orch -> Agent | connectivity probe | dashboard liveness |
| LeaseHeartbeat | Agent -> Orch | lease renewal | stale/reassign |

### Lease Policy Defaults

| Parameter | Value |
|-----------|-------|
| Lease TTL | 90s |
| Heartbeat interval | 15s |
| Stale threshold | 3 missed heartbeats |
| Auto-fail bound | 2 reassignments OR 15m stale |

### Lease State Flow

```
Assigned
  -> Active
     -> MissedHeartbeat(1)
     -> MissedHeartbeat(2)
     -> MissedHeartbeat(3)
     -> AssignedStale
        -> Reassigned (attempt < bound)
        -> AutoFail (bound reached)
```

### Sequence

```
Orchestrator    Hub       LeaseMgr    Hangfire    Agent
    |           |           |           |          |
    | Ping ---->|           |           |          |
    | (no resp) | mark node posture only          |
    |           |           |           |          |
    |           |<---------- LeaseHeartbeat       |
    |           |           | renew ---->|          |
    |           |           |           |          |
    |           |           | missed HB #1       |
    |           |           |---------->|          |
    |           |           | missed HB #2       |
    |           |           |---------->|          |
    |           |           | missed HB #3       |
    |           |           | AssignedStale      |
    |           |           |---------->| reassign |
    |           |<--------------------------------|
    |           |<-------------------------------- AckClaim
    |           |<-------------------------------- LeaseHeartbeat
    |           |           |           |          |
    |           |           | bound reached?     |
    |           |           |---------->| AutoFail |
    |           |<-------------------------------- Fail
    |           |<-------------------------------- LeaseClose
```

**Guardrail**: Ping loss = dashboard only; LeaseHeartbeat loss = stale/reassign/auto-fail

### Verification

- [ ] Missing Ping updates node posture only
- [ ] Missing LeaseHeartbeat drives stale transitions
- [ ] Reassignment/auto-fail follows bounded policy

Traceability: NFR-001, AC-101

---

## 7. Agent Local Typed Pipeline

### Contract

Agent executes full pipeline locally; orchestrator owns job-level policy/state.

### Pipeline Steps (Ordered)

1. `PreConditionCheck`
2. `AcquireArtifact`
3. `ValidateSignatureAndHash`
4. `DetectCurrentState`
5. `InstallOrUpgrade`
6. `PostInstallVerify`
7. `EmitFinalization`

### Pipeline Sequence

```
Assignment accepted
  -> PreConditionCheck
  -> AcquireArtifact (HTTP)
  -> ValidateSignatureAndHash
  -> DetectCurrentState (idempotency check)
  -> InstallOrUpgrade
  -> PostInstallVerify
  -> EmitFinalization
  -> terminal Complete/Fail
```

### Detailed Execution

```
SignalR       Channel        BG Worker      ChildProc    Artifact API
  |              |               |              |            |
  | AssignJob -->| enqueue       |              |            |
  |              |-------------->| dequeue       |            |
  |              |               | PreCheck ------------------------>|
  |              |               |-------------------------------> StepStatus(seq=1)
  |              |               |                               |
  |              |               | AcquireArtifact ------------->| GET
  |              |               |<------------------------------ bytes
  |              |               |-------------------------------> StepStatus(seq=2)
  |              |               |                               |
  |              |               | ValidateSignatureAndHash      |
  |              |               |-------------------------------> StepStatus(seq=3)
  |              |               |                               |
  |              |               | DetectCurrentState            |
  |              |               | (skip/proceed decision)       |
  |              |               |-------------------------------> StepStatus(seq=4)
  |              |               |                               |
  |              |               | InstallOrUpgrade -> spawn --->|
  |              |               |<------------------------------ exit code
  |              |               |-------------------------------> StepStatus(seq=5)
  |              |               |                               |
  |              |               | PostInstallVerify              |
  |              |               |-------------------------------> StepStatus(seq=6)
  |              |               |                               |
  |              |               | EmitFinalization               |
  | Complete/Fail ------------------------------------------>|
  | LeaseClose ------------------------------------------>|
```

### Adapter Normalization

| Adapter | Raw | Normalized |
|---------|-----|------------|
| MSI | 0 | success |
| MSI | 3010 | success_reboot_required |
| MSI | 1602 | cancelled_by_user_or_policy |
| EXE | vendor-specific | mapped via adapter rules |

### Verification

- [ ] Step order preserved
- [ ] Each step emits status with correlation
- [ ] Adapter outputs normalized

Traceability: FR-003, FR-005, AC-004, AC-006

---

## 8. Update and Modify

### Update Flow (Example: 22 -> 24)

```
UI/CLI        Orch API       Agent Pipeline      Snapshot Store    Installer    Audit
  |              |                |                 |              |          |
  | submit (22->24)              |                 |              |          |
  |------------->| assign job ---->| AckClaim+lease  |              |          |
  |              |                | DetectCurrentState(22.x)        |          |
  |              |                | AcquireArtifact |              |          |
  |              |                |<------------------------------| bytes    |
  |              |                | ValidateSignatureAndHash       |          |
  |              |                |---- fail? -------->|           | fail     |
  |              |                | create snapshot -->|              |          |
  |              |                |<------------------| snapshotId |          |
  |              |                | StepStatus(snapshot,id) -------->|          |
  |              |                | InstallOrUpgrade ------------------------>| run 24  |
  |              |                |<-------------------------------| exit     |
  |              |                | PostInstallVerify(expect 24.x)  |          |
  |              |                |-------------------------------> Complete  |
  |              |<---------------| final status + linkage         |          |
```

### Modify and Downgrade

**Downgrade is high-risk by default.**

```
Modify request
      |
      v
Evaluate policy tags
      |
      +-- standard (low/medium) -> execute normal
      |
      +-- downgrade/high-risk
             |
             +-- no approval path -> reject
             |
             +-- approval present
                    -> explicit approval event
                    -> snapshot readiness
                    -> strict retry posture
```

### Failure Branch

```
Update failure
   |
   +-- A) Trust gate failure (pre-mutation)
   |       reason: signature_invalid, hash_mismatch, artifact_untrusted
   |       no mutation occurred
   |       terminal: Failed
   |
   +-- B) Mutation failure (post-verify)
           configSnapshotId exists
           execute restore using snapshotId
           record restoreOutcome (restored | restore_failed)
           terminal: FailedWithRestore | FailedRestoreFailed
           audit chain: failureEventId -> snapshotId -> restoreAttemptId
```

### Verification

- [ ] Snapshot created before mutation
- [ ] `migration_path_missing` is explicit failure reason
- [ ] Restore attempt/outcome auditable
- [ ] Approval gate exists for high-risk path

Traceability: FR-006, AC-002, AC-007, AC-101

---

## 9. Retry and Idempotency

### Retry Policy

```json
{
    "retryPolicy": {
        "maxAttempts": 3,
        "backoffSeconds": [30, 60, 120],
        "retryableReasons": ["network_timeout", "connection_reset"],
        "nonRetryableReasons": ["disk_full", "insufficient_privileges"]
    }
}
```

**Rules**: Retry is bounded and transient-only. High-risk/non-idempotent never blind auto-retries.

### Retry Flow

```
Agent          Policy Eval      Step
  |                |            |
  | run step ----->|            |
  |                | classify   |
  |                |----- transient? -----|
  |                |    high-risk/non-idempotent?    |
  |                |            |
  | <-- retry allowed (bounded) |
  | backoff t1/t2/t3            |
  | rerun step    |            |
  | ... until success or exhausted           |
  |                |            |
  | <-- retry denied           |
  | Fail(non_retryable_or_exhausted)       |
```

### Idempotency Modes

| Mode | Behavior | Use case |
|------|----------|----------|
| `detect` | Skip if satisfied | Standard installs |
| `always` | Always execute | Config changes |
| `never` | Fail if exists | Mutually exclusive |
| `version_check` | Upgrade/downgrade if differs | Versioned packages |

### Idempotency Check Flow

```
Agent Pipeline      Detection
    |                  |
    | DetectCurrentState
    |---------------->|
    |                  |
    | file exists?     |
    | version matches?  |
    | key matches?      |
    |                  |
    | <-- "SKIP" (satisfied)
    |     "PROCEED" (not satisfied)
    |                  |
    | StepStatus(PrecheckPassed)
```

### Verification

- [ ] Retry count follows bounds
- [ ] Replay with equal payload safe
- [ ] Mismatched payload rejected and audited

Traceability: FR-002, NFR-001, AC-003, AC-101

---

## 10. Cancel and Rollback

### Cancel Flow

```
Operator      API/Orch         Agent         ChildProc
  |              |               |              |
  | cancel ---->| mark intent    |              |
  |              | signal ----->|              |
  |              |              | graceful stop ->|
  |              |              | force kill (timeout)
  |              |              |<-------------|
  |              |<-------------| emit final     |
```

### Rollback Semantics

- Rollback contract exists: execute restore path
- No rollback contract: terminal failure with explicit reason

### Verification

- [ ] Cancel transition auditable
- [ ] Child process termination policy followed
- [ ] Reason distinguishes cancel vs failure

Traceability: FR-001, AC-002, NFR-002

---

## 11. Security Control Perimeters

Replaces "Trust boundaries" with clearer terminology.

### Perimeter Map

```
+-------------------------------------------------------------------+
|                        TRUSTED ZONE                                |
|                                                                    |
|  +------------------+     +----------------------------------+     |
|  |   Admin UI      |     |        Orchestrator              |     |
|  +--------+---------+     |  +-----------+  +--------------+ |     |
|           |               |  | REST API  |  | SignalR Hub  | |     |
|           | HTTPS/RBAC    |  +-----+-----+  +------+------+ |     |
+-----------|---------------+--------|--------------|----------+-----+
            |                       |              |          |
 SCP-01     | SCP-02               | SCP-05       | SCP-04  |
            v                       v              v          v
+---------------------------+  +-----------------------------------+
|      UNTRUSTED ZONE       |  |         UNTRUSTED ZONE            |
|                           |  |                                   |
|  +------------------+      |  |  +---------------------------+    |
|  |  Agent Service   |      |  |  |    Artifact Storage       |    |
|  |  (Remote Node)  |      |  |  |    (Local filesystem)     |    |
|  +--------+---------+      |  |  +---------------------------+    |
|           | SCP-06           |  |                                   |
|           v                  |  |                                   |
|  +------------------+      |  |                                   |
|  | Child Process    |      |  |                                   |
|  | (Job Execution)  |      |  |                                   |
|  +------------------+      |  |                                   |
+---------------------------+  +-----------------------------------+
```

### Perimeter Definitions

| ID | From | To | Risk | Controls |
|----|------|-----|-----|---------|
| SCP-01 | Admin | Orch API | Privilege abuse | RBAC, authZ, audit |
| SCP-02 | Agent | SignalR Hub | Replay/spoof | mTLS, sequence checks |
| SCP-03 | Orch API | Artifact store | Tamper | Immutable digest, ACL |
| SCP-04 | Agent | Artifact API | MITM | TLS, hash+sig validation |
| SCP-05 | Orch | SQLite | State integrity | App validation, host ACL |
| SCP-06 | Agent service | Child process | Escalation | Constrained spawn |

### Global Invariants

- Artifact trust validation before execution (fail closed)
- Runtime sequence strict; stale/out-of-order rejected
- Idempotent key: `(jobId, stepId, sequence)`
- Same-key payload mismatch = reject + audit
- Reconnect resumes from `lastAcknowledgedSequence + 1`

Traceability: FR-002, FR-003, NFR-001, NFR-002, AC-003, AC-101, AC-102

---

## 12. Child Process Security

### Spawn Policy

```
Agent step wants installer
   -> build allowed executable + args
   -> sanitize/validate arguments
   -> launch with constrained token
   -> enforce timeout + limits
   -> capture stdout/stderr + exit code
   -> map to normalized reason/status
```

### Security Controls

- Run as non-elevated user where possible
- Restricted token with minimal privileges
- No network access (outbound blocked)
- CPU/memory limits via Job Object
- Timeout hard-kill after grace period
- Arguments sanitized before invocation

### Verification

- [ ] Disallowed command/arg blocked
- [ ] Timeout/limit violations visible
- [ ] Exit mapping deterministic

Traceability: NFR-002, AC-102

---

## 13. Artifact Trust Validation

### Validation Sequence

```
Acquire artifact
    -> digest check
       -> mismatch? fail artifact_tampered
    -> signature/trust check
       -> invalid? fail signature_invalid
    -> emit trust evidence
    -> continue pipeline
```

### Verification

- [ ] Digest mismatch blocks execution
- [ ] Invalid signature blocks execution
- [ ] Trust evidence recorded

Traceability: NFR-002, AC-102

---

## 14. Identity Lifecycle (Token -> mTLS)

### Phases

| Phase | Auth | Purpose |
|-------|------|---------|
| Enrollment | one-time token | first bind only |
| Steady-state | mTLS cert | all reconnect/ops |

### Lifecycle

```
Enroll token issue
   -> agent connect with token
   -> validate + bind identity
   -> issue/bind cert
   -> invalidate token
   -> reconnect via mTLS only
```

### Rejection

```
Reconnect attempt
   -> no cert / invalid cert / unbound cert
      -> reject connection
      -> emit auth failure audit
```

### Verification

- [ ] Token cannot be reused
- [ ] mTLS required for reconnect
- [ ] Invalid cert rejected

Traceability: FR-004, NFR-002, AC-005, AC-102

---

## 15. Observability

### Telemetry Contract

Every job has:
- Root span
- Step-level spans
- Correlation fields: `jobId`, `nodeId`, `step`, `reasonCode`, `sequence`, `leaseId`

### Event Emission

```
Agent Worker      SignalR       StepStatus Ingest    Audit/OTel
    |                |                |              |
    | StepStatus --->|--------------->| validate    |
    |                |                | write event ->|
    |                |                | emit span ----->|
    |                |                |              |
    | Complete/Fail ->|--------------->| set terminal ->|
    | LeaseClose ---->|--------------->| close event ->|
```

### OTel Baseline

Phase 1 default: file-based export with rotation/retention and redaction controls.

### Verification

- [ ] Correlation fields on each step
- [ ] Sensitive fields redacted
- [ ] Rotation/retention enforced

Traceability: NFR-003, NFR-002, AC-103, AC-102

---

## 16. Operator Surfaces

### Surface Matrix

| Surface | Purpose | Runtime status |
|---------|---------|----------------|
| REST API | Canonical control | Required |
| Embedded UI | Visibility + actions | Required |
| CLI | Automation over API | Required |
| Scripts | Provisioning/bootstrap only | Exception |

### UI Sequence

```
Sysadmin        UI              Orch API         Runtime
   |              |                |               |
   | open Install |                |               |
   | select pkg   |                |               |
   | submit job -->| POST /jobs --->| validate      |
   |<-------------| 202 jobId       |               |
   | open timeline| GET /jobs/{id}/steps --------->|
   |<-------------| live statuses   |               |
   | cancel? ---->| POST /jobs/{id}/cancel -------->|
```

### CLI Commands

```bash
di jobs create --manifest .\node24.json --targets node-001
di jobs status --job-id job-20260414-001
di jobs cancel --job-id job-20260414-001 --reason "operator_request"
di nodes list
di artifacts upload --file .\nodejs.zip --manifest .\nodejs.json
```

### Verification

- [ ] Runtime ops without script orchestration
- [ ] UI timeline reflects live status
- [ ] CLI maps 1:1 to API

Traceability: NFR-004, AC-104, FR-001, AC-001, AC-002

---

## 17. Orchestrator Self-Update (Staged Swap)

### Pattern

Naive in-place overwrite is unsafe. Phase 1 pattern:

1. Download candidate package
2. Validate trust + compatibility
3. Stage beside current binary
4. Supervisor stops old, starts candidate
5. Health gate verification
6. Rollback on failure

### State Machine

```
Idle
  -> DownloadingCandidate
  -> VerifyingCandidateTrustAndCompatibility
  -> StagedCandidateReady
  -> HandoffToSupervisor
  -> SwitchingProcess
  -> ProbingStartupHealth
      -> HealthyNewActive
      -> HealthGateFailed
           -> RollbackToPrevious
                -> PreviousHealthyRestored
                -> RollbackFailedTerminal
```

### Verification

- [ ] Candidate sig/hash passes before switch
- [ ] Startup health required for completion
- [ ] Failed startup triggers rollback with audit

Traceability: NFR-005, AC-105

---

## 18. Persistence (SQLite)

### Canonical Entities

- `Job`
- `Node`
- `AssignmentLease`
- `ConfigSnapshot`

### Persistence Rules

```
API request
   -> validate input
   -> transaction begin
   -> write job + assignment rows
   -> commit
   -> dispatch assignment
```

**Invariant**: Do not dispatch before durable persistence.

### Verification

- [ ] Runtime state not in-memory-only
- [ ] Entity transitions queryable for audit

Traceability: FR-001, FR-006, NFR-001, AC-001, AC-007, AC-101

---

## 19. DevOps and Deployment Boundary

### Policy

- Pipeline may build/sign/package/deploy orchestrator
- Pipeline must NOT directly execute workstation runtime install/upgrade/rollback
- Runtime node actions via orchestrator API/CLI only

### CI/CD Flow

```
CI -> Build/Test -> Publish self-contained -> Sign
   -> Packaging validation (clean host launch)
   -> Deploy orchestrator
   -> Integration/E2E gates
```

### Verification

- [ ] Pipeline deploys orchestrator only
- [ ] Runtime boundary enforced
- [ ] Audit trail from orchestrator surfaces

Traceability: NFR-004, NFR-005, AC-104, AC-105

---

## 20. Fault Injection

### Fault Matrix

| Fault | Behavior | Evidence |
|-------|----------|----------|
| Checksum mismatch | fail closed | trust failure event |
| Network interruption | bounded retry if transient | retry timeline |
| Agent disconnect | lease stale policy | AssignedStale transitions |
| Retry exhaustion | terminal fail | final reason + attempts |
| Invalid cert reconnect | reject | auth audit |

### Verification

- [ ] Fault outcomes match policy
- [ ] No silent failures
- [ ] Evidence reconstructable

Traceability: AC-101, AC-102, AC-103

---

## 21. Verification Checklist

### Orchestrator Install
- [ ] Self-contained EXE launches clean
- [ ] `/health` healthy
- [ ] Embedded UI reachable
- [ ] SQLite + artifact path initialized

### Agent Bootstrap
- [ ] One-time token issued/consumed
- [ ] Service running
- [ ] mTLS reconnect succeeds
- [ ] Invalid cert rejected

### Artifact Lifecycle
- [ ] Upload stores immutable digest
- [ ] Agent fetches from internal API only
- [ ] Range/chunk works
- [ ] Trust mismatch blocks

### Runtime Protocol
- [ ] Canonical sequence observed
- [ ] Idempotency correct
- [ ] Stale timeout enforced

### Pipeline
- [ ] Full typed pipeline in order
- [ ] Adapter normalization consistent
- [ ] Terminal state deterministic

### Modify/Update
- [ ] Snapshot before mutate
- [ ] Restore path executed
- [ ] Downgrade approval enforced

### Surfaces
- [ ] API/UI/CLI runtime ops work
- [ ] Script surface not required

---

## 22. Traceability Matrix

| Storyboard | Primary AC |
|------------|-----------|
| Packaging/clean-host | AC-105 |
| Orchestrator install | AC-001, AC-105 |
| Agent bootstrap | AC-005, AC-102 |
| Artifact ingestion/delivery | AC-006, AC-102 |
| Runtime protocol | AC-003 |
| Lease/stale | AC-101 |
| Local pipeline | AC-004, AC-006 |
| Update/modify | AC-001, AC-002, AC-007 |
| Observability | AC-103 |
| API/CLI surfaces | AC-104 |

---

## 23. Contradiction Resolutions

1. **SignalR payload**: Final = SignalR control/status only; HTTP for bytes
2. **Self-update**: Final = staged swap + supervisor; no naive overwrite
3. **Retry**: Final = policy-driven bounded; high-risk/no-blind-auto-retry
4. **Package source**: Final = internal-only
5. **Ping vs Lease**: Final = explicit semantic separation
6. **Token lifecycle**: Final = one-time token; mTLS steady-state
7. **Execution ownership**: Final = agent local pipeline; orch job-level policy
8. **Provenance terminology**: Final = "origin metadata"

---

## Related Documents

- `poc-phase1-prd-final.md` (canonical source of truth)
- `poc-phase1-prd-and-implementation-tracker.md`
- `08-requirements-contract.md` (deprecated, content merged into PRD)
