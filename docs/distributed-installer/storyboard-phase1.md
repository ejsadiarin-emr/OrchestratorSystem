# Installation and Operational Storyboards (PoC Phase 1)

Date: 2026-04-15
Status: Canonical execution storyboards for PoC Phase 1
Scope: Windows-first, single-orchestrator distributed installer runtime

---

## Purpose

This document defines end-to-end operational behavior for PoC Phase 1.

It specifies storyboard flows for:

- DevOps Pipeline to build and package the Orchestrator binaries

---

## Source-of-Truth Precedence

When documents conflict, use this order:

1. `docs/distributed-installer/poc-phase1-prd-final.md`
2. `docs/distributed-installer/storyboard-phase1.md` (this document)
3. `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
4. `docs/distributed-installer/08-requirements-contract.md`
5. Historical storyboard drafts for context only

---

## Inputs and Hard Constraints

### PoC Phase 1 constraints

- Windows-first only
- Single orchestrator only (no HA/multi-orchestrator commitments)
- Runtime package source is internal-only (orchestrator artifact store)
- SignalR is control/status channel only
- Artifact payload transfer is HTTP endpoint based (supports range/chunk)
- Orchestrator distribution is self-contained single executable with embedded UI
- Runtime actions are API/UI/CLI driven
- Scripts are provisioning/bootstrap only

### Canonical runtime protocol sequence

`Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

---

## Global Architecture and Trust Storyboard

### Logical system map

```text
                           +-----------------------------------+
                           |            System Admin           |
                           |      UI (embedded) / CLI / API   |
                           +----------------+------------------+
                                            |
                                            | HTTPS + RBAC
                                            v
  +-----------------------------------------------------------------------------------+
  |                                  Orchestrator Node                                |
  |                                                                                   |
  |  +--------------------+    +----------------------+    +-----------------------+  |
  |  | REST API           |    | SignalR Runtime Hub  |    | Hangfire Queue        |  |
  |  | jobs/nodes/artifact|    | Assign/Ack/Lease/... |    | enqueue/dispatch/retry|  |
  |  +----------+---------+    +----------+-----------+    +-----------+-----------+  |
  |             |                         |                            |              |
  |             +-------------------------+----------------------------+              |
  |                                       |                                           |
  |                      +----------------v----------------+                           |
  |                      | Runtime Protocol + Lease Policy |                           |
  |                      +----------------+----------------+                           |
  |                                       |                                            |
  |               +-----------------------+----------------------+                     |
  |               |                                              |                     |
  |      +--------v---------+                         +----------v----------------+    |
  |      | SQLite           |                         | Artifact Store (local FS) |    |
  |      | Job/Node/Lease/  |                         | immutable digest records  |    |
  |      | ConfigSnapshot   |                         +---------------------------+    |
  |      +------------------+                                                          |
  +-------------------------------+------------------------------------+---------------+
                                  |                                    |
                       SignalR control/status                   HTTPS artifact
                       (no payload bytes)                       GET/HEAD + Range
                                  |                                    |
                     +------------v---------------+          +---------v---------+
                     | Agent Win Service          |<---------| Artifact Endpoint |
                     | (SignalR + mTLS)           |          +-------------------+
                     |                            |
                     | JobChannelService          |
                     | Channel<T> + BackgroundSvc |
                     +------------+---------------+
                                  |
                                  | constrained spawn
                                  v
                     +-----------------------------+
                     | Child Process (MSI/EXE/etc) |
                     +-----------------------------+
```

### Security per flow

| From          | To                 | Primary risk           | Required controls                 |
| ------------- | ------------------ | ---------------------- | --------------------------------- |
| Admin         | Orchestrator API   | Privilege abuse/spoof  | RBAC, authN/authZ, audit          |
| Agent         | SignalR Hub (Orch) | Replay/spoofing        | enrollment->mTLS, sequence checks |
| Orch API      | Artifact store     | tamper/substitution    | immutable digest metadata, ACL    |
| Agent         | Artifact API       | MITM/tamper            | TLS, hash+signature validation    |
| Orch          | SQLite             | state integrity        | app-level validation + host ACL   |
| Agent service | Child process      | escalation/unsafe args | constrained spawn policy          |

---

## Core Storyboard Map

| Storyboard                 | Purpose                                           |
| -------------------------- | ------------------------------------------------- |
| Media packaging            | Build/sign/publish orchestrator package           |
| Fresh orchestrator install | Bring up API/UI/Hub/persistence deterministically |
| Agent install via WinRM    | Enroll node and bind identity (`token -> mTLS`)   |
| Package lifecycle          | Ingest -> submit -> assign -> execute -> observe  |
| Job submission             | fill here                                         |
| Modify workload            | Update/downgrade/self-update with policy gates    |

---

## Media Packaging Storyboard

### Packaging posture

| Option             | What it gives                            | Phase 1 decision   |
| ------------------ | ---------------------------------------- | ------------------ |
| Self-contained EXE | Clean-host startup, simple operator path | Selected (primary) |
| ZIP bundle         | Easy transfer and scripted unpack        | Supported          |
| ISO media          | Offline distribution pattern             | Deferred           |

### Sequence

```text
DevOps CI             Signing Service         Artifact Repo         Operator
   |                         |                     |                   |
   | build/test              |                     |                   |
   | dotnet publish (single-file, self-contained)  |                   |
   |------------------------>|                     |                   |
   |                         | sign exe + checksums + manifest         |
   |<------------------------|                     |                   |
   | publish media --------------------------------------------------> |
   |  - Orchestrator.exe      |                    |                   |
   |  - Orchestrator.zip      |                    |                   |
   |                          |                    | operator download |
```

### Verification gates

- Signer/hash verification succeeds.
- Package launches on clean host without .NET/IIS preinstall.

---

## Fresh Orchestrator Install Storyboard

### Step-by-step flow

1. Admin stages `Orchestrator.exe` (or extracts ZIP).
2. Admin runs initialization (interactive or scripted config).
3. Config captures:
    - listen URL/port (default :5000)
    - initial admin credentials
    - SQLite database path
    - artifact storage path (local UNC or folder)
    - OTel exporter endpoint/export mode
4. Orchestrator starts API, Hub, persistence, embedded UI host.
5. Startup checks run; bootstrap audit event is emitted.

### Sequence

```text
Admin                     Orchestrator Process                 Host Resources
  |                               |                                  |
  | launch EXE                    |                                  |
  |------------------------------>|                                  |
  |                               | initialize config                |
  |                               |------------------------------->  |
  |                               | create/open sqlite               |
  |                               |------------------------------->  |
  |                               | verify artifact path             |
  |                               |------------------------------->  |
  |                               | start API/UI/Hub                 |
  | GET /health                   |                                  |
  |------------------------------>|                                  |
  |<------------------------------| 200 healthy                      |
```

- example:

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

### Verification gates

- `GET /health` returns healthy.
- Embedded UI loads from orchestrator host.
- `GET /api/nodes` returns valid schema.
- SQLite file/schema initialize.
- Artifact path is writable and access-controlled.

### Orchestrator self-update

**Modeled after Desktop apps (ex. Discord) but simpler**

- Self-checks itself (version) then auto-updates when needed/instructed

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

---

## Package/Artifact Ingestion Storyboard (from Orchestrator)

**Upload packages/artifacts to artifact store from Orchestrator**

Installer media file types:

- `.exe` (Windows installer/bootstrapper)
- `.msi` (Windows Installer package)
- `.zip` (portable/pre-expanded bundle)
- `.tar.gz` (allowed for mirrored upstream assets; execution still follows Windows adapter policy)

- Examples:

| Package family                        | What to upload as installer media                                                            |
| ------------------------------------- | -------------------------------------------------------------------------------------------- |
| .NET runtime / ASP.NET hosting bundle | Vendor installer `.exe` for target runtime/version                                           |
| PostgreSQL                            | Vendor Windows installer `.exe` (or approved bundled installer)                              |
| SQL Server (future realism target)    | SQL Server setup installer media package (vendor setup executable/bundle staged as artifact) |

### Upload/Ingestion flow

1. Admin uploads installer media file (or requests vendor import).
2. Orchestrator fetches vendor metadata (if available) and runs binary analyzer.
3. Orchestrator returns prefilled manifest suggestions to UI/API client.
4. Admin reviews suggested values and fills required metadata gaps.
5. Admin submits final manifest (+ optional company signature file).
6. Orchestrator checks file hash/signature/metadata trust evidence.
7. Orchestrator stores file, locks immutable version record, and sends ingest audit event.

```text
System Admin/UI          Orchestrator API       Vendor Source           Artifact Store      Policy/Audit (orchestrator)
  |                            |                     |                        |                    |
  | 1) upload media OR import  |                     |                        |                    |
  |--------------------------->| 2) get vendor metadata --------------------->|                    |
  |                            |<---------------------------------------------|                    |
  |                            | 2) run binary analyzer                       |                    |
  |<---------------------------| 3) return prefilled metadata                 |                    |
  | 4) fill gaps + confirm     |                                              |                    |
  |--------------------------->| 5) submit final manifest (+ optional company signature)           |
  |                            | 6) check hash/signature/metadata             |                    |
  |                            | 7) store file + lock record ---------------->|                    |
  |                            | 7) send ingest audit event -------------------------------------->|
  |<---------------------------| 201 created                                                       |
```

### API request/response shape

Use multipart upload with binary bytes and manifest metadata:

- Endpoint: `POST /api/artifacts`
- Content-Type: `multipart/form-data`
- Required part `file`: installer media binary bytes
- Required part `manifest`: JSON manifest/policy metadata
- Optional part `detachedSignature`: separate company signature file (not vendor signature), used for manual/automated verification
- Upload is one `POST /api/artifacts` request with a multipart body that contains these parts (not two separate API requests).

- System admin/client request shape to send:

> [!NOTE]
> Manifest/Metadata fields may be prefilled from vendor metadata and/or binary analysis service, but admin review is still required and orchestrator remains final verifier

### Admin-entered metadata fields

Fields the system admin must provide/confirm before ingest:

- `manifest.packageId`
- `manifest.displayName`
- `manifest.version`
- `manifest.channel` (`stable|canary|test`)
- `manifest.artifactType`
- `manifest.installAdapter.type`
- `manifest.installAdapter.command`
- `manifest.installAdapter.arguments`
- `manifest.installAdapter.expectedExitCodes`
- `manifest.installAdapter.timeoutSeconds`
- `manifest.detection.type`
- `manifest.detection.path`
- `manifest.detection.expectedVersion`
- `manifest.originMetadata.source`
- `manifest.originMetadata.publisher`
- `manifest.policyTags.retryabilityClass`
- `manifest.policyTags.idempotencyMode`
- `manifest.policyTags.riskLevel`
- `manifest.policyTags.approvalRequired`

```text
POST /api/artifacts
Content-Type: multipart/form-data

Part 1 (required): file
- binary installer media bytes

Part 2 (required): manifest (application/json)
{
  "packageId": "dotnet-runtime",
  "displayName": "Microsoft .NET Runtime 8.0.4",
  "version": "8.0.4",
  "channel": "stable",
  "artifactType": "exe",
  "installAdapter": {
    "type": "exe",
    "command": "artifact.bin",
    "arguments": "/quiet /norestart",
    "expectedExitCodes": [0, 3010],
    "timeoutSeconds": 1800
  },
  "detection": {
    "type": "registry",
    "path": "HKLM\\SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64\\sharedfx\\Microsoft.NETCore.App",
    "expectedVersion": ">=8.0.4"
  },
  "originMetadata": {
    "source": "vendor-mirror",
    "publisher": "Microsoft"
  },
  "policyTags": {
    "retryabilityClass": "transient_only",
    "idempotencyMode": "version_check",
    "riskLevel": "medium",
    "approvalRequired": false
  }
}

Part 3 (optional): detachedSignature
- company signature bytes
```

### UI upload

**Support package upload through embedded UI (`Orchestrator.exe` -> React UI) with drag-and-drop.**

- UI should call the same `POST /api/artifacts` endpoint as CLI/API clients.
- Keep API as source of truth; UI is an operator convenience surface.
- UI should expose progress, digest/verification outcome, and final artifact URL.

### Stored manifest record (post-ingest)

**What is stored for each package/artifact**

```json
{
    "artifact": {
        "source": "/api/artifacts/nodejs/24.0.0",
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

### Policy

- Agents - only get artifacts from orchestrator API (own artifact store), not internet.
- Orchestrator - upstream binaries are ingested into artifact store.
- Artifact version records are immutable/locked and checked.

### Verification gates

- Ingest audit event exists with origin metadata fields.
- Digest value is stored and immutable for version record.
- `manifest.channel` is one of `stable|canary|test`.
- Package with invalid trust evidence is blocked.

---

## Agent Installation Storyboard (Token -> mTLS)

**Installation of agent on remote machines**

- Agent binary is uploaded on the Orchestrator's artifact store
    - is then downloaded on remote machines via the bootstrap script

### Bootstrap options

| Method                           | Benefit            | Limitations               | Phase 1                      |
| -------------------------------- | ------------------ | ------------------------- | ---------------------------- |
| Manual PowerShell script         | Fast and practical | Operator variance         | Selected                     |
| WinRM remoting script            | Remote convenience | Environment prerequisites | Supported                    |
| GPO/SCCM enterprise distribution | Fleet scale        | Outside PoC scope         | Considered, but not selected |

### Main flow

1. Admin requests enrollment token for target node.
2. Admin runs bootstrap script on target machine.
3. Script installs agent executable/service config.
4. Agent connects with one-time token.
5. Orchestrator validates token and binds node identity.
6. Certificate material is issued for steady-state mTLS.
7. Agent reconnects with bound certificate.
8. Enrollment token is invalidated.

### Sequence diagram

```text
System Admin              Target Machine (Remote)         Agent Service            Orchestrator
    |                            |                            |                         |
    | Step 1: connectivity check |                            |                         |
    | Test-NetConnection -Port 5985                           |                         |
    |<-------------------------->|                            |                         |
    |                            |                            |                         |
    | Step 2: request enrollment token                                                  |
    | POST /api/nodes/enroll ---------------------------------------------------------->|
    | {hostname,nodeMetadata}                                                           |
    |<------------------------------------------------------- {token,nodeId,ttl} -------|
    |                                                                                   |
    | Step 3: Run bootstrap script (WinRM)                                              |
    | Invoke-Command ------------------------------------------------------------------>|
    |  - download Agent.exe from orchestrator                                           |
    |  - write config with orchestratorUrl + token + nodeId                             |
    |  - sc.exe create service                                                          |
    |  - Start-Service                                                                  |
    |--------------------------->| install files/config/service                         |
    |                            |--------------------------->|                         |
    |                            |                            | Connect(token) -------->|
    |                            |                            |                         |
    |                            |                            |<--------- token valid / bind identity
    |                            |                            |<--------- cert material issued
    |                            |                            | disconnect               |
    |                            |                            | Reconnect(mTLS cert) --->|
    |                            |                            |<--------------- accepted |
    |                            |                            | LeaseHeartbeat --------->|
    |                            |                            |                          |
    | Step 4: verify registration/status                                                 |
    | GET /api/nodes/{nodeId} ---------------------------------------------------------->|
    |<----------------------------------------------------- {status:"online",auth:"mtls"}|
```

### Bootstrap failure cleanup

Cleanup order:

1. Stop service if started
2. Remove service registration
3. Remove config file
4. Remove installed binaries
5. Revoke/expire token state
6. Emit cleanup audit event

```text
Install files -> Create service -> Write config -> Start service
        |
        +-- failure at any step
                |
                v
      Reverse cleanup: Stop -> Remove service -> Delete config
                       -> Delete files -> Invalidate token -> Audit
```

### Verification gates

- Windows service exists/running.
- Node appears online.
- Lease heartbeat observed.
- Token cannot be reused.
- Invalid/unbound cert reconnect is rejected.
- Cleanup branch leaves no partial state

---

## Artifact Delivery Storyboard (HTTP + Range/Chunk)

**How Agents install artifacts**

- From receiving job assignment via SignalR (from Orchestrator)

### Flow

1. Agent receives assignment with artifact reference via SignalR.
2. Agent requests artifact bytes via HTTPS endpoint.
3. For large payloads, agent uses range requests.
4. Agent assembles local cache file and validates digest.
5. Pipeline proceeds only on pass.

### Transport decision

- **SignalR payload (from Orchestrator)** - only has **artifactReference**:

```text
Assignment message (SignalR):
{
  "jobId": "...",
  "artifactReference": {
    "url": "/api/artifacts/nodejs/24.0.0",
    "digest": "sha256:..."
  }
}
```

- **HTTP GET/HEAD with chunking for large payloads** - actual artifact payload

```
Artifact bytes (HTTP):
GET /api/artifacts/nodejs/24.0.0
-> Binary payload via HTTP
```

### Range/chunk sequence

```text
Agent                                               Artifact API
  |                                                        |
  | HEAD /api/artifacts/nodejs/24.0.0                      |
  |------------------------------------------------------->|
  | <--- 200 + Content-Length + ETag ----------------------|
  | GET range bytes=0-10485759                             |
  |------------------------------------------------------->|
  | <--- 206 chunk#1 --------------------------------------|
  | GET range bytes=10485760-20971519                      |
  |------------------------------------------------------->|
  | <--- 206 chunk#2 --------------------------------------|
  | ...repeat until complete...                            |
  | validate assembled digest                              |
```

### Verification gates

- Artifact bytes never flow over SignalR.
- Range requests are accepted for large payload retrieval.
- Digest verification blocks corrupted/incomplete downloads.

---

## Job Submission and Assignment Storyboard

### Operator intent flow

1. Sysadmin selects targets.
2. Sysadmin selects package + version/channel + operation (`install|update|modify`).
3. UI/CLI submits `POST /api/jobs` with manifest/policy fields.
4. API validates authorization, schema, and policy.
5. Orchestrator persists job + assignments.
6. Orchestrator enqueues dispatch.
7. Hub sends `AssignJob`, agent returns `AckClaim`, lease tracking starts.
8. UI/CLI opens timeline view.

### End-to-end sequence diagram

```text
System Admin     UI/CLI        Orchestrator API      SQLite DB       Hangfire Queue      SignalR Hub      Agent Service      JobChannel/Worker     Child Process      Artifact API
    |              |                  |                   |                 |                  |                |                   |                  |                |
    | submit job   |                  |                   |                 |                  |                |                   |                  |                |
    |------------->| POST /api/jobs   |                   |                 |                  |                |                   |                  |                |
    |              |----------------->| validate auth/schema/policy         |                  |                |                   |                  |                |
    |              |                  | persist Job ------>|                |                  |                |                   |                  |                |
    |              |                  | persist Assignment>|                |                  |                |                   |                  |                |
    |              |                  | enqueue dispatch ------------------>|                  |                |                   |                  |                |
    |              |<-----------------| 202 Accepted + jobId                |                  |                |                   |                  |                |
    | open timeline| GET /api/jobs/{id}, /steps                             |                  |                |                   |                  |                |
    |<-------------|<-----------------| read timeline ---->|                |                  |                |                   |                  |                |
    |              |                  |                   |                 | dequeue dispatch |                |                   |                  |                |
    |              |                  |                   |                 |----------------->| AssignJob      |                   |                  |                |
    |              |                  |                   |                 |                  |--------------->| AckClaim          |                  |                |
    |              |                  | update lease row ->|                |                  |<---------------|                   |                  |                |
    |              |                  |                   |                 |                  |<---------------| LeaseHeartbeat    |                  |                |
    |              |                  |                   |                 |                  |                | enqueue assignment|                  |                |
    |              |                  |                   |                 |                  |                |------------------>| run pipeline     |                |
    |              |                  |                   |                 |                  |                |                   | spawn ---------->|                |
    |              |                  |                   |                 |                  |                |                   | GET/HEAD(+Range) ---------------->|
    |              |                  |                   |                 |                  |                |                   |<----------------------------------|
    |              |                  |                   |                 |                  |<---------------| StepStatus(seq*)  |                  |                |
    |              |                  | upsert step row -->|                |                  |                |                   |                  |                |
    |              |                  |                   |                 |                  |<---------------| Complete/Fail     |                  |                |
    |              |                  | set terminal row ->|                |                  |                |                   |                  |                |
    |              |                  |                   |                 |                  |<---------------| LeaseClose        |                  |                |
```

> [!IMPORTANT]
> Orchestrator persists `Job` and `Assignment` rows before enqueueing dispatch.
> SignalR carries control/status messages only (AssignJob, AckClaim, LeaseHeartbeat, StepStatus, Complete/Fail, LeaseClose).
> Artifact payload bytes are transferred only through HTTP artifact endpoints.

### Verification gates

- Request rejected for invalid/missing policy fields.
- Assignment emitted only after persistence success.
- `AckClaim` includes assignment/lease identifiers.

---

## Fresh Install Execution Storyboard (End-to-End)

### Flow

1. Submit install job.
2. Validate and assign.
3. Agent executes full local pipeline.
4. Orchestrator updates timeline and job state.
5. Job reaches deterministic terminal state.

### Verification gates

- Ordered step timeline is visible.
- Job terminal state matches final step outcome.
- Reasons are auditable.

---

## Update Storyboard (Example: Node 22 -> 24)

### Policy and safety requirements

- Source/target version intent must be explicit.
- Pre-mutation snapshot is required where applicable.
- Failure path must include restore attempt and linked audit chain.

### Flow

1. Operator submits update intent (`22 -> 24`).
2. Agent detects current state/version.
3. Snapshot service captures pre-mutation state.
4. Update step runs target package.
5. Post-install verification checks target version.
6. On mutation-stage failure, restore path executes.

### Verification gates

- Snapshot exists before mutation.
- Missing migration path is explicit (`migration_path_missing`).
- Restore attempt/outcome is auditable and linked.

---

## Modify and Downgrade Storyboard

### Policy classes

- `retryabilityClass`: `none | transient_only | bounded`
- `idempotencyMode`: `detect | always | never | version_check`
- `riskLevel`: `low | medium | high`
- `approvalRequired`: `true | false`

### Downgrade baseline

- Downgrade is high risk by default.
- Default posture: disallow unless explicit approved path exists.
- High-risk/non-idempotent operations are not blind auto-retried.

### Decision tree

```text
Modify request received
      |
      v
Evaluate policy tags + requested direction
      |
      +-- standard modify (low/medium risk) -> execute with normal policy
      |
      +-- downgrade or high risk
             |
             +-- no approval path -> reject
             |
             +-- approval path present
                    -> require explicit approval event
                    -> require snapshot readiness
                    -> execute with strict retry posture
```

### Verification gates

- Approval event exists for high-risk path.
- Rejection reason is explicit when approval is missing.
- Snapshot readiness checked before high-risk mutation.

---

## Retry, Idempotency, and Replay Storyboard

### Policy principles

- Retry is bounded (max 3 retries) and transient-focused (only for temporary failures like network timeout, etc.).
- High-risk/non-idempotent actions are never blindly auto-retried.
- Idempotency prevents duplicate side effects on replay.

### Verification gates

- Retry counts/intervals follow policy bounds.
- Replay with equal payload is safe.
- Replay with mismatched payload is rejected and audited.

---

## Cancel and Rollback Storyboard

### Cancel semantics

1. Orchestrator marks cancellation intent.
2. Agent receives cancellation signal at safe interruption point.
3. Agent stops child process via graceful stop then force-kill on timeout.
4. Job transitions to cancelled/failure terminal state with explicit reason.

### Rollback semantics

- If rollback/snapshot contract exists: execute restore path.
- If no rollback contract: terminal failure with explicit reason.

### Verification gates

- Cancel transition is auditable.
- Child-process termination policy is followed.
- Reason codes distinguish cancel from execution failure.
