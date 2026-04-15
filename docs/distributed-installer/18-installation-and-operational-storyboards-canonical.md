# Installation and Operational Storyboards (Canonical, Final)

Date: 2026-04-14
Status: Canonical source for PoC Phase 1 execution storyboards
Scope: Windows-first, single-orchestrator distributed installer PoC

Derived and consolidated from:

- `docs/distributed-installer/15-installation-and-operational-storyboards.md`
- `docs/distributed-installer/16-installation-and-operational-storyboards-independent.md`
- `docs/distributed-installer/poc-phase1-prd-final.md`
- `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
- `docs/distributed-installer/08-requirements-contract.md`
- `docs/distributed-installer/13-poc-phase1-definition-of-done.md`

---

## Purpose

This is the final, implementation-aligned storyboard for PoC Phase 1.

It defines:

- The end-to-end operational flows (install, update, modify, cancel, rollback, self-update)
- Runtime message and lease semantics
- Trust boundaries and security control points
- Artifact ingestion and internal-only package source behavior
- Verification gates and evidence expectations per flow
- Operator surface behavior in UI and CLI

This document is normative for flow behavior. If a previous storyboard draft differs, this document wins.

---

## Source-of-Truth Precedence

When documents conflict, use this order:

1. `docs/distributed-installer/poc-phase1-prd-final.md`
2. `docs/distributed-installer/18-installation-and-operational-storyboards-canonical.md` (this document)
3. `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
4. `docs/distributed-installer/08-requirements-contract.md`
5. Earlier storyboard drafts (`15`, `16`) for historical context only

---

## Inputs and Hard Constraints

### PoC hard constraints

- Windows-first in Phase 1
- Single orchestrator only (no HA/multi-orchestrator commitments)
- Runtime package source is internal-only (orchestrator artifact store)
- SignalR is control/status channel only
- Artifact payload transfer is HTTP endpoint based (range/chunk for large files)
- Orchestrator package is self-contained single executable with embedded UI
- Runtime actions are API/CLI driven (scripts are provisioning-only)

### Canonical runtime sequence

`Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

---

## Reader Guide

Each storyboard contains:

- Decision context
- Detailed flow (step-by-step)
- Sequence diagram (ASCII)
- Verification gates
- Failure/rollback branches
- Traceability tags (FR/NFR/AC)

Legend:

- `Orch` = orchestrator
- `Agent` = remote node service
- `API` = orchestrator REST APIs
- `Hub` = SignalR control/status channel
- `Artifact API` = HTTP artifact endpoints

---

## 1) Global Architecture and Trust Storyboard

### 1.1 Logical system map

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

Architecture note:

- `docs/distributed-installer/diagrams/architecture.ascii.md` is partially stale against current Phase 1 baseline.
- Stale points: SQL Server as primary persistence, external artifact source as runtime default.
- Still-valid points: Hangfire on orchestrator, `Channel<T> + BackgroundService` on agent, runtime channel split.

### 1.2 Trust boundaries (Phase 1)

| Boundary | From          | To               | Primary risk           | Required controls                 |
| -------- | ------------- | ---------------- | ---------------------- | --------------------------------- |
| TB-01    | Admin         | Orchestrator API | Privilege abuse/spoof  | RBAC, authN/authZ, audit          |
| TB-02    | Agent         | SignalR Hub      | Replay/spoofing        | enrollment->mTLS, sequence checks |
| TB-03    | Orch API      | Artifact store   | tamper/substitution    | immutable digest metadata, ACL    |
| TB-04    | Agent         | Artifact API     | MITM/tamper            | TLS, hash+signature validation    |
| TB-05    | Orch          | SQLite           | state integrity        | app-level validation + host ACL   |
| TB-06    | Agent service | Child process    | escalation/unsafe args | constrained spawn policy          |

### 1.3 Global invariants

- Artifact trust validation occurs before execution and fails closed.
- Runtime sequence validation is strict; stale/out-of-order updates are rejected.
- Idempotent ingest key is `(jobId, stepId, sequence)`.
- Same-key payload mismatch is rejected and audited.
- Reconnect resumes from `lastAcknowledgedSequence + 1`.

### 1.4 Queue model (implementation baseline)

Queue model is implementation-level (not transport contract):

- Orchestrator dispatch queue: Hangfire (enqueue, scheduling, retry orchestration)
- Agent execution queue: `Channel<T>` consumed by BackgroundService/worker loop

```text
UI/CLI/API -> REST create job
          -> persist Job/Assignment
          -> enqueue dispatch work (Hangfire)
          -> dequeue + send AssignJob via SignalR
          -> agent AckClaim
          -> agent enqueue assignment in Channel<T>
          -> BackgroundService dequeues
          -> execute full local pipeline
```

Implementation-vs-contract boundary notes:

- Runtime contract is transport/sequence/state semantics, not queue-library semantics.
- Hangfire is current orchestrator implementation choice for dispatch/scheduling/retry orchestration in Phase 1.
- `Channel<T> + BackgroundService` is current agent implementation choice for local assignment buffering/execution.
- Queue internals are non-contractual as long as externally observable behavior and AC criteria remain unchanged.
- Orchestrator remains source of truth for job/assignment/lease state; queue systems are execution mechanics.
- Agent-side channel is intentionally in-memory/ephemeral; restart recovery depends on orchestrator reconciliation and resume rules.
- SignalR remains control/status only; artifact bytes remain HTTP artifact endpoint traffic regardless of queue implementation.
- Future queue substitutions are acceptable if canonical runtime behavior, auditability, and policy gates are preserved.

Traceability: FR-002, FR-003, NFR-001, NFR-002, AC-003, AC-101, AC-102

---

## 2) Core Operational Storyboards (Skimmable First Pass)

This section front-loads the five Phase 1 core storyboards so operators can scan end-to-end behavior quickly.
Detailed canonical variants remain in later sections.

### 2.1 Core storyboard map

| Core storyboard | Quick purpose | Detailed sections |
| --- | --- | --- |
| Media packaging (EXE/ZIP/ISO posture) | Build and trust-pack orchestrator media | 2.2, 3, 21, 23 |
| Fresh orchestrator install | Bring up main node deterministically | 3 |
| Sub-node/agent remote install via WinRM | Enroll and bind node identity (token -> mTLS) | 4, 18 |
| Software package install lifecycle | Ingest -> submit -> assign -> execute -> observe | 5, 6, 7, 10, 11, 12 |
| Workload modification | Orchestrator self-update + remote modify/update/downgrade | 12, 13, 21 |

### 2.2 Core sequence #1: media packaging (EXE/ZIP/ISO posture, DevOps pipeline)

Decision baseline:

| Option | What it gives | Cost/risk | Phase 1 decision |
| --- | --- | --- | --- |
| Self-contained EXE | Clean-host startup, simple operator path | Packaging pipeline complexity | Selected (primary) |
| ZIP bundle | Easy transfer and scripted unpack | Extra operator steps | Supported |
| ISO media | Full offline distro pattern | Higher build and ops complexity | Deferred (still documented) |

```text
DevOps CI             Signing Service         Artifact Repo         Operator
   |                         |                    |                  |
   | build/test              |                    |                  |
   | dotnet publish (single-file, self-contained) |                  |
   |------------------------>|                    |                  |
   |                         | sign exe + checksums + manifest         |
   |<------------------------|                    |                  |
   | publish media -----------------------------------------------> |
   |  - Orchestrator.exe      |                    |                  |
   |  - Orchestrator.zip      |                    |                  |
   |  - (optional) Orchestrator.iso metadata marker               |
   |                         |                    |                  |
   |                         |                    | operator download |
   |                         |                    |-----------------> |
   |                         |                    |                  |
   |                         |                    | verify signer/hash|
```

### 2.3 Core sequence #2: fresh install (main node/orchestrator)

```text
Admin                    Orchestrator Process                 Host Resources
  |                             |                                   |
  | stage media (EXE/ZIP)       |                                   |
  |---------------------------->|                                   |
  | run --init                  |                                   |
  |---------------------------->| write appsettings --------------> |
  |                             | create/open sqlite -------------> |
  |                             | verify artifact path -----------> |
  |                             | start API + Hub + UI             |
  | GET /health                 |                                   |
  |---------------------------->|                                   |
  |<----------------------------| 200 healthy                       |
```

### 2.4 Core sequence #3: sub node install (remote agent install via WinRM)

```text
Operator WS            Target Machine (WinRM)          Agent Service           Orchestrator
    |                           |                           |                        |
    | POST /api/nodes/enroll ------------------------------------------------------->|
    |<---------------------------------------------------- token,nodeId,ttl -----------|
    | Invoke-Command (download agent, write config, create/start service) ----------->|
    |--------------------------->| install/start ------------>|                        |
    |                           |                           | Connect(token) --------->|
    |                           |                           |<------- bind cert -------|
    |                           |                           | reconnect(mTLS) -------->|
    |                           |                           |<------- accepted --------|
    | GET /api/nodes/{nodeId} ------------------------------------------------------->|
    |<--------------------------------------------- status=online, auth=mtls ---------|
```

### 2.5 Core sequence #4: software package install lifecycle

```text
Admin/UI/CLI       API           Artifact Store      Hangfire/Hub         Agent Pipeline      Audit/Telemetry
    |               |                  |                  |                    |                    |
    | upload pkg -->| digest/signature |                  |                    |                    |
    |               | immutable record->|                  |                    |                    |
    | create job -->| validate policy   | persist job      | enqueue/assign ---->|                    |
    |               |------------------>|                  |                    | Acquire/Validate   |
    |               |                  |                  |                    | Install/Verify     |
    |               |                  |                  |<----- StepStatus ----|                    |
    |               | timeline/state update ------------------------------------>| emit evidence ----->|
```

### 2.6 Core sequence #5: modify workload

```text
Sysadmin            Orchestrator API              Target Agent/Node            Supervisor/Wrapper
   |                       |                             |                             |
   | A) Self-update ------>| stage candidate             |                             |
   |                       | handoff --------------------|---------------------------->|
   |                       | health gate + rollback path |                             |
   |<----------------------| result                       |                             |
   |                       |                             |                             |
   | B) Remote modify ---->| create modify/update job    | run pipeline                |
   | (ex: node 22->24 or 24->22 with approval policy)    | detect/snapshot/mutate/verify
   |                       |                             | restore on qualifying fail   |
   |<----------------------| terminal + audit linkage    |                             |
```

### 2.7 Core verification gates

- Packaging trust chain and clean-host launch pass.
- Orchestrator install yields healthy API/UI/SQLite/artifact path.
- WinRM bootstrap reaches token->mTLS steady-state.
- Artifact lifecycle stays internal-only and hash/signature gated.
- Modify flows enforce risk/approval/snapshot/restore policy.

Traceability: FR-001, FR-004, FR-005, FR-006, NFR-004, NFR-005, AC-001, AC-005, AC-006, AC-104, AC-105

---

## 3) Fresh Orchestrator Install Storyboard

### 3.1 What this flow proves

- A clean host can run orchestrator package
- Initial runtime configuration is deterministic
- API/UI/health/storage are operational

### 3.2 Step-by-step flow

1. Admin stages `Orchestrator.exe` (or extracts ZIP bundle).
2. Admin runs first-time init (interactive or scripted config file).
3. Config captures:
    - listen URL/port
    - admin bootstrap auth settings
    - SQLite path
    - artifact storage path
    - telemetry export mode
4. Orchestrator starts API, hub, persistence, embedded UI host.
5. System performs startup checks and emits bootstrap audit event.

### 3.3 Sequence diagram

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
  |                               |                                  |
  | GET /health                   |                                  |
  |------------------------------>|                                  |
  | 200 healthy                   |                                  |
  |<------------------------------|                                  |
```

### 3.4 Startup mockup (operator view)

```text
+---------------------------------------------------------------+
| Distributed Installer Orchestrator                            |
| Version: 1.0.x                                                |
|---------------------------------------------------------------|
| Service Status   : RUNNING                                    |
| API Endpoint     : https://localhost:5000                     |
| Hub Endpoint     : /runtime-hub                               |
| SQLite           : C:\ProgramData\DI\orchestrator.db          |
| Artifact Path    : C:\ProgramData\DI\artifacts                |
| Telemetry Export : file (rotation enabled)                    |
|---------------------------------------------------------------|
| Health Checks                                                 |
| [PASS] API host                                               |
| [PASS] SQLite connectivity                                    |
| [PASS] Artifact path ACL write/read                           |
| [PASS] Embedded UI assets                                     |
+---------------------------------------------------------------+
```

### 3.5 Verification gates

- `GET /health` returns healthy.
- Embedded UI loads from orchestrator host.
- `GET /api/nodes` works and returns valid response schema.
- SQLite file and schema initialize.
- Artifact path is writable and access-controlled.

Traceability: FR-001, NFR-005, AC-001, AC-105

---

## 4) Agent Installation Storyboard (Token -> mTLS)

### 4.1 Why this matters

Bootstrap must be practical for PoC but still secure enough to prove identity lifecycle.

Phase 1 uses manual/scripted bootstrap as provisioning-only path.

### 4.2 Bootstrap alternatives comparison

| Method                                      | Benefit                    | Risk/limitations  | Phase 1   |
| ------------------------------------------- | -------------------------- | ----------------- | --------- |
| Manual PowerShell script                    | Fast and realistic for PoC | operator variance | Selected  |
| WinRM remoting script                       | remote convenience         | env prerequisites | Supported |
| Enterprise software distribution (GPO/SCCM) | scalable fleet ops         | outside PoC scope | Deferred  |

### 4.3 Main flow

1. Admin requests enrollment token for target node.
2. Admin runs bootstrap script on target machine.
3. Script installs agent executable and service config.
4. Agent starts and connects with one-time token.
5. Orchestrator validates token and binds node identity.
6. Certificate material issued/bound for steady-state mTLS.
7. Agent reconnects using bound cert identity.
8. Enrollment token is invalidated.

### 4.4 Sequence diagram

```text
Operator WS              Target Machine (Remote)         Agent Service            Orchestrator
    |                            |                            |                         |
    | Step 1: connectivity check |                            |                         |
    | Test-NetConnection :5985   |                            |                         |
    |<-------------------------->|                            |                         |
    |                            |                            |                         |
    | Step 2: request enrollment token                                                  |
    | POST /api/nodes/enroll ---------------------------------------------------------->|
    | {hostname,nodeMetadata}                                                           |
    |<------------------------------------------------------- {token,nodeId,ttl} -------|
    |                            |                            |                         |
    | Step 3: bootstrap over WinRM                                                      |
    | Invoke-Command ------------------------------------------------------------------>|
    |  - download Agent.exe from orchestrator                                           |
    |  - write config with orchestratorUrl + token + nodeId                             |
    |  - sc.exe create service                                                          |
    |  - Start-Service                                                                  |
    |--------------------------->| install files/config/service |                       |
    |                            |----------------------------->|                       |
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

### 4.5 Bootstrap failure and transactional cleanup

Failure cleanup order (reverse application order):

1. Stop service if started
2. Remove service registration
3. Remove config file
4. Remove installed binaries
5. Revoke/expire token state
6. Emit cleanup audit event

### 4.6 Cleanup branch diagram

```text
Install files -> Create service -> Write config -> Start service
        |
        +-- failure at any step
                |
                v
      Reverse cleanup: Stop -> Remove service -> Delete config
                       -> Delete files -> Invalidate token -> Audit
```

### 4.7 Verification gates

- Windows service exists and is running.
- Node appears online.
- Lease heartbeat is observed.
- Token cannot be reused.
- Invalid/unbound cert reconnect is rejected.
- Cleanup flow leaves no partial bootstrap residue.

Traceability: FR-004, NFR-002, AC-005, AC-102

---

## 5) Artifact Ingestion Storyboard (Internal-Only Source)

### 5.1 Policy baseline

- Agents do not fetch runtime artifacts from internet/vendor at execution time.
- Upstream binaries are ingested into orchestrator artifact store first.
- Artifact version records are immutable and hash-bound.

### 5.2 Ingestion flow

1. Admin uploads package/bundle + metadata.
2. Orchestrator computes canonical digest.
3. Orchestrator validates available trust evidence (signature/checksum metadata).
4. Orchestrator writes immutable package version record.
5. Optional org attestation signs metadata envelope (not vendor binary).

### 5.3 Sequence diagram

```text
Admin                    API                    Artifact Store           Policy/Audit
  |                      |                           |                        |
  | upload package ----->|                           |                        |
  |                      | write artifact ---------->|                        |
  |                      | compute digest            |                        |
  |                      | verify trust metadata     |                        |
  |                      | create immutable record -------------------------->|
  |                      | emit ingest audit event -------------------------->|
  |<---------------------| 201 created                                        |
```

### 5.4 Artifact manifest model (Phase 1 minimum)

```json
{
    "packageId": "nodejs",
    "displayName": "Node.js 24 LTS",
    "description": "JavaScript runtime for build and service workloads",
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
    "provenance": {
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

### 5.5 Verification gates

- Ingest audit event exists with provenance fields.
- Digest value is stored and immutable for version record.
- Channel is one of `stable|canary|test`.
- Package with invalid trust evidence is blocked.

Traceability: FR-001, FR-005, NFR-002, AC-006, AC-102

---

## 6) Artifact Delivery Storyboard (HTTP + Range/Chunk)

### 6.1 Transport decision

| Path                             | Purpose                   | Selected |
| -------------------------------- | ------------------------- | -------- |
| SignalR payload transfer         | artifact bytes            | No       |
| HTTP GET/HEAD artifact endpoints | artifact bytes            | Yes      |
| Range/chunk retrieval            | large payload reliability | Yes      |

### 6.2 Flow

1. Agent receives assignment manifest with artifact reference.
2. Agent requests artifact via HTTPS endpoint.
3. For large payloads, agent uses range requests.
4. Agent assembles local cache file and validates digest.
5. Pipeline proceeds only on pass.

### 6.3 Range/chunk sequence

```text
Agent                                               Artifact API
  |                                                        |
  | HEAD /api/artifacts/nodejs/24.0.0                      |
  |------------------------------------------------------->|
  | <--- 200 + Content-Length + ETag ----------------------|
  |                                                        |
  | GET range bytes=0-10485759                             |
  |------------------------------------------------------->|
  | <--- 206 chunk#1 --------------------------------------|
  |                                                        |
  | GET range bytes=10485760-20971519                      |
  |------------------------------------------------------->|
  | <--- 206 chunk#2 --------------------------------------|
  |                                                        |
  | ...repeat until complete...                            |
  |                                                        |
  | validate assembled digest                              |
```

### 6.4 Failure branch

```text
Chunk fetch fails -> classify error
      |
      +-- transient network -> bounded retry with backoff
      |
      +-- non-transient -> fail step with reason code
```

### 6.5 Verification gates

- Artifact payload bytes never flow via SignalR.
- Range requests are accepted for large package retrieval.
- Digest verification blocks corrupted/incomplete download.

Traceability: FR-005, NFR-002, AC-006, AC-102

---

## 7) Job Submission and Assignment Storyboard

### 7.1 Operator intent flow

1. Sysadmin opens Install/Update action in UI (or runs CLI create command).
2. Sysadmin selects target nodes.
3. Sysadmin selects package + version/channel and confirms operation (`install|update|modify`).
4. UI/CLI submits `POST /api/jobs` with manifest/policy fields.
5. API validates schema, policy tags, and authorization.
6. Orchestrator persists job + assignments, then enqueues dispatch.
7. Hub sends `AssignJob`; agent returns `AckClaim`; lease tracking begins.
8. UI/CLI navigates to job details/timeline for live progress.

### 7.2 Submission sequence

```text
Sysadmin          UI/CLI Client         Orchestrator API      SQLite State      Hangfire      SignalR Hub       Agent
   |                    |                      |                   |                 |              |               |
   | choose nodes/pkg/version                |                   |                 |              |               |
   |------------------->| build request       |                   |                 |              |               |
   | review+submit      | POST /api/jobs ---->| validate auth/schema/policy        |              |               |
   |                    |                      | persist job ----->|                |              |               |
   |                    |                      | persist assignments>|               |              |               |
   |                    |                      | enqueue dispatch ------------------>|              |               |
   |<-------------------| 202 + jobId/state   |                   |                 |              |               |
   | open job timeline  | GET /api/jobs/{id} ->| read ----------- >|               |              |               |
   |                    |<---------------------| details            |               |              |               |
   |                    |                      |                   |                | dequeue      |               |
   |                    |                      |                   |                |------------->| AssignJob --->|
   |                    |                      |                   |                |              |<-- AckClaim --|
   |                    |                      | update lease ---->|                |              |               |
   |                    |                      |                   |                |              |<-- StepStatus |
   |                    | timeline poll/stream | update step/job ->|                |              |               |
```

### 7.3 API response mockup

```json
{
    "jobId": "job-20260414-001",
    "state": "Queued",
    "targets": ["node-001", "node-002"],
    "requestedOperation": "install",
    "submittedAtUtc": "2026-04-14T12:00:00Z"
}
```

### 7.4 Verification gates

- Request rejected if policy fields are invalid/missing.
- Assignment emitted only after persistence success.
- AckClaim contains assignment/lease identifiers.

Traceability: FR-001, FR-002, AC-001, AC-003

---

## 8) Runtime Control-Plane Protocol Storyboard (Canonical Sequencing)

This section defines the canonical control-plane contract only.
It is not the full install business flow; full install execution remains in Section 11.

### 8.1 Canonical message lifecycle

```text
Connect
  -> Register/Authenticate
      -> AssignJob
          -> AckClaim
              -> LeaseHeartbeat
                  -> StepStatus*
                      -> Complete/Fail
                          -> LeaseClose
```

### 8.2 Protocol sequence diagram

```text
Sysadmin/UI/API     Orchestrator API      SQLite         Hangfire        SignalR Hub        Agent SignalR      Agent JobChannel    Agent Worker
      |                   |                 |               |                |                   |                  |                 |
      | create job ------>| validate        |               |                |                   |                  |                 |
      |                   | persist job --->|               |                |                   |                  |                 |
      |                   | persist assignment ------------>| enqueue        |                   |                  |                 |
      |                   |                 |               |                |                   |                  |                 |
      |                   |                 |               | dequeue ------>|                   |                  |                 |
      |                   |                 |               |                |                   | Connect -------->|                 |
      |                   |                 |               |                |<------------------| Register/Auth    |                 |
      |                   |                 | update node -->|               |                   |                  |                 |
      |                   |                 |               |                | AssignJob ------->|                  |                 |
      |                   |                 |               |                |<------------------| AckClaim         |                 |
      |                   | update lease -->|               |                |                   | enqueue claim -->|                 |
      |                   |                 |               |                |                   |                  | dequeue ------->|
      |                   |                 |               |                |<------------------| LeaseHeartbeat   |                 |
      |                   | update lease -->|               |                |                   |                  |                 |
      |                   |                 |               |                |<------------------| StepStatus(seq=1)|                 |
      |                   | upsert step --->|               |                |                   |                  |                 |
      |                   |                 |               |                |<------------------| StepStatus(seq=2)|                 |
      |                   | upsert step --->|               |                |                   |                  |                 |
      |                   |                 |               |                |<------------------| StepStatus(seq=n)|                 |
      |                   | set terminal -->|               |                |                   |                  |                 |
      |                   |                 |               |                |<------------------| Complete/Fail    |                 |
      |                   | close lease --->|               |                |                   |                  |                 |
      |                   |                 |               |                |<------------------| LeaseClose       |                 |

Notes:
- Canonical order is strict: Connect -> Register/Auth -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose.
- SignalR carries control/status only; artifact bytes remain on HTTP artifact endpoints.
```

### 8.3 Sequence validation rules

- Reject stale sequence (`<= last acknowledged`).
- Reject out-of-order gaps beyond tolerated replay model.
- Idempotent upsert key: `(jobId, stepId, sequence)`.
- Same-key payload mismatch => reject + audit `sequence_payload_conflict`.

### 8.4 Reconnect behavior

```text
Agent SignalR          SignalR Hub          StepStatus Ingest          SQLite              Agent Worker
    |                      |                       |                      |                     |
    | (connection loss)    |                       |                      |                     |
    |---- disconnect ----->|                       |                      |                     |
    |                      | mark transient offline|                      |                     |
    |                      |---------------------->|                      |                     |
    |                      |                       | read last ack ---->  |                     |
    |                      |                       |<--- seq=K ---------  |                     |
    | Connect -----------> |                       |                      |                     |
    | Register/Auth -----> |                       |                      |                     |
    |<-- ResumeCursor(K) --|                       |                      |                     |
    |                      |                       |                      |                     |
    | resend StepStatus(K+1) --------------------->| idempotent upsert -> |                     |
    | resend StepStatus(K+2) --------------------->| idempotent upsert -> |                     |
    | ...                                            ...                                        |
    | resend stale StepStatus(<=K) --------------->| reject stale + audit |                     |
    | resend same key, diff payload -------------->| reject conflict + audit sequence_payload_conflict
    |                      |                       |                      |                     |
    | LeaseHeartbeat ----->|                       |                      |                     |
    | StepStatus* -------->|---------------------->| upsert               |                     |
    | Complete/Fail ------>|                       |                      |                     |
    | LeaseClose --------->|                       |                      |                     |

Resume rule:
- Agent resumes strictly from `lastAcknowledgedSequence + 1`.
- Orchestrator is authoritative for acknowledged cursor and conflict detection.
```

### 8.5 Verification gates

- Replay message does not duplicate side effects.
- Payload conflict is explicitly rejected and audited.
- Resume cursor behavior is deterministic.

Traceability: FR-002, NFR-001, AC-003, AC-101

---

## 9) Lease and Liveness Storyboard (Ping vs LeaseHeartbeat)

### 9.1 Semantic split

| Signal         | Direction             | Meaning                 | Used for                     |
| -------------- | --------------------- | ----------------------- | ---------------------------- |
| Ping           | Orchestrator -> Agent | connectivity probe      | dashboard liveness posture   |
| LeaseHeartbeat | Agent -> Orchestrator | lease ownership renewal | stale detection/reassignment |

### 9.2 Lease policy defaults (Phase 1)

- Lease TTL: `90s`
- Heartbeat interval: `15s`
- Stale threshold: `3` missed heartbeats
- Auto-fail bound: `2` reassignment attempts OR `15m` total stale duration

### 9.3 Lease state flow

```text
Assigned
  -> Active
     -> MissedHeartbeat(1)
     -> MissedHeartbeat(2)
     -> MissedHeartbeat(3)
     -> AssignedStale
        -> Reassigned (attempt < bound)
        -> AutoFail (attempt/time bound reached)
```

### 9.4 Sequence diagram

```text
Orchestrator PingSvc    SignalR Hub       Lease Manager        SQLite          Hangfire         Agent SignalR      Agent Worker
        |                   |                  |                 |                |                  |                 |
        | Ping -----------> | ---------------->|                 |                |                  |                 |
        | (no response)     |                  | mark node conn posture only      |                  |                 |
        |                   |                  |---------------> |                 |                  |                 |
        |                   |                  |                 |                 |                  |                 |
        |                   | <----------------| LeaseHeartbeat  |                 |<-----------------|                 |
        |                   |                  | renew lease ---->|                |                  |                 |
        |                   |                  |                 |                 |                  |                 |
        |                   |                  | missed HB #1    |                |                  |                 |
        |                   |                  |--------------->|                 |                  |                 |
        |                   |                  | missed HB #2     |                |                  |                 |
        |                   |                  |--------------->|                 |                  |                 |
        |                   |                  | missed HB #3 => AssignedStale     |                  |                 |
        |                   |                  |--------------->|                 |                  |                 |
        |                   |                  | evaluate bounds |                |                  |                 |
        |                   |                  | attempts < 2 && stale < 15m ?     |                  |                 |
        |                   |                  |------ yes ----------------------->| enqueue reassign |
        |                   |                  |                                   | dequeue -------->|
        |                   | <----------------------------------------------------| AssignJob        |
        |                   | <---------------- AckClaim --------------------------|                  |
        |                   | <---------------- LeaseHeartbeat --------------------|                  |
        |                   |                  |                                   |                  |
        |                   |                  |------ no (bound reached) -------->| mark terminal fail|
        |                   |                  |                                   |                  |
        |                   | <---------------- Fail ------------------------------|                  |
        |                   | <---------------- LeaseClose ------------------------|                  |

Semantics guardrail:
- Ping loss affects node connectivity posture/dashboard only.
- LeaseHeartbeat loss drives assignment stale/reassign/auto-fail policy.
```

### 9.5 Verification gates

- Missing Ping updates node connectivity posture only.
- Missing LeaseHeartbeat drives assignment stale transitions.
- Reassignment/fail follows configured bounded policy.

Traceability: NFR-001, AC-101

---

## 10) Agent Local Typed Pipeline Storyboard

### 10.1 Pipeline contract

Agent executes full per-job pipeline locally; orchestrator tracks job-level policy/state only.

Ordered steps:

1. `PreConditionCheck`
2. `AcquireArtifact`
3. `ValidateSignatureAndHash`
4. `DetectCurrentState`
5. `InstallOrUpgrade`
6. `PostInstallVerify`
7. `EmitFinalization`

### 10.2 Pipeline sequence

```text
Assignment accepted
  -> PreConditionCheck
  -> AcquireArtifact
  -> ValidateSignatureAndHash
  -> DetectCurrentState
  -> InstallOrUpgrade
  -> PostInstallVerify
  -> EmitFinalization
  -> terminal Complete/Fail
```

### 10.3 Detailed execution diagram

```text
SignalR Client     JobChannelService    BG Worker Loop    Local Cache    Child Proc    Orch Hub
    |                    |                   |               |              |            |
    | AssignJob -------->| enqueue           |               |              |            |
    |                    |------------------>| dequeue       |              |            |
    |                    |                   |-------------->|              |            |
    |                    |                   | Step1 PreConditionCheck      |            |
    |                    |                   |------------------------------>|            |
    |                    |                   | StepStatus(seq=1,ok) -------------------->|
    |                    |                   |                                           |
    |                    |                   | Step2 AcquireArtifact (HTTP) ---> GET --->|
    |                    |                   |<-------------------------- bytes/chunks ---|
    |                    |                   | write cache file -> verify length         |
    |                    |                   | StepStatus(seq=2,ok) -------------------->|
    |                    |                   |                                           |
    |                    |                   | Step3 ValidateSignatureAndHash             |
    |                    |                   | digest + signature checks                  |
    |                    |                   | StepStatus(seq=3,ok/fail) --------------->|
    |                    |                   |                                           |
    |                    |                   | Step4 DetectCurrentState                  |
    |                    |                   | idempotency decision (skip/proceed)       |
    |                    |                   | StepStatus(seq=4,decision) -------------->|
    |                    |                   |                                           |
    |                    |                   | Step5 InstallOrUpgrade -> spawn --------->|
    |                    |                   |                              run installer |
    |                    |                   |<----------------------------- exit code ---|
    |                    |                   | StepStatus(seq=5,normalized) ------------>|
    |                    |                   |                                           |
    |                    |                   | Step6 PostInstallVerify                   |
    |                    |                   | StepStatus(seq=6,ok/fail) --------------->|
    |                    |                   |                                           |
    |                    |                   | Step7 EmitFinalization                    |
    |                    |                   | Complete/Fail + LeaseClose -------------->|
```

### 10.4 Adapter normalization comparison

| Adapter type | Raw exit/behavior    | Normalized reason/status    |
| ------------ | -------------------- | --------------------------- |
| MSI          | 0                    | success                     |
| MSI          | 3010                 | success_reboot_required     |
| MSI          | 1602                 | cancelled_by_user_or_policy |
| EXE          | vendor-specific code | mapped via adapter rules    |

### 10.5 Verification gates

- Step order is preserved.
- Each step emits status with required correlation.
- Adapter outputs are normalized.

Traceability: FR-003, FR-005, AC-004, AC-006

---

## 11) Fresh Install Execution Storyboard (Job + Pipeline End-to-End)

### 11.1 Flow

This section composes Sections 7 + 8 + 10 into one operator-visible execution storyline.

1. Submit install job.
2. Validate and assign.
3. Agent executes full pipeline.
4. Orchestrator updates timeline and job state.
5. Job reaches deterministic terminal state.

### 11.2 End-to-end sequence (detailed)

```text
Sysadmin     UI/API      Orch API      SQLite      Hangfire   Hub/SignalR   AgentSvc   Channel/BG   ChildProc   Artifact
   |          |             |            |            |           |             |          |           |          |
   | submit   |             |            |            |           |             |          |           |          |
   |--------->| POST /jobs   |            |            |           |             |          |           |          |
   |          |------------->| validate    |            |           |             |          |           |          |
   |          |              | create job->|            |           |             |          |           |          |
   |          |              | assignment->|            |           |             |          |           |          |
   |          |              | enqueue ----------------->|           |             |          |           |          |
   |          |<-------------| 202 jobId   |            |           |             |          |           |          |
   |          |              |            |            | dequeue    |             |          |           |          |
   |          |              |            |            |----------->| AssignJob -->|          |           |          |
   |          |              |            |            |           |             | enqueue  |           |          |
   |          |              |            |            |           |<-- AckClaim --|--------->|           |          |
   |          |              | lease row->|            |           |             |          |           |          |
   |          |              |            |            |           |<-- LeaseHB ---|          |           |          |
   |          |              |            |            |           |             | run step | spawn ---->|          |
   |          |              |            |            |           |             |          |           | GET ----->|
   |          |              |            |            |           |<-- StepStatus(seq*) ---|           |<---------|
   |          |              | update --->|            |           |             |          |           |          |
   | view     | timeline pull|            |            |           |             |          |           |          |
   |<---------|<-------------|            |            |           |             |          |           |          |
   |          |              | terminal -->|           |           |<-- Complete/Fail -----|           |          |
```

### 11.3 UI timeline mockup

```text
+--------------------------------------------------------------------------------+
| Job: job-20260414-001   Operation: Install   Target: node-001   State: Running |
|--------------------------------------------------------------------------------|
| Seq | Step                      | Status      | Started      | Duration | Reason |
|-----|---------------------------|-------------|--------------|----------|--------|
| 1   | PreConditionCheck         | Succeeded   | 12:00:01Z    | 00:01    | -      |
| 2   | AcquireArtifact           | Succeeded   | 12:00:02Z    | 00:09    | -      |
| 3   | ValidateSignatureAndHash  | Succeeded   | 12:00:11Z    | 00:02    | -      |
| 4   | DetectCurrentState        | Succeeded   | 12:00:13Z    | 00:01    | absent |
| 5   | InstallOrUpgrade          | Running     | 12:00:14Z    | 00:15    | -      |
| 6   | PostInstallVerify         | Pending     | -            | -        | -      |
| 7   | EmitFinalization          | Pending     | -            | -        | -      |
+--------------------------------------------------------------------------------+
```

### 11.4 Verification gates

- Full step timeline visible and ordered.
- Job terminal state aligns with final step outcome.
- State transitions and reasons are auditable.

Traceability: FR-001, FR-003, AC-001, AC-002, AC-004

---

## 12) Update Storyboard (Example: Node 22 -> 24)

### 12.1 Policy and safety requirements

- Update must capture pre-mutation snapshot where applicable.
- Version intent must be explicit.
- Failure must support restore path and linked audit event.

### 12.2 Flow

1. Operator submits update intent (`sourceVersion` -> `targetVersion`).
2. Agent detects current state/version.
3. Snapshot service captures pre-mutation config/state.
4. Install/upgrade step runs target package.
5. Post-install verifies target version.
6. If failure, restore using snapshot contract and emit failure linkage.

### 12.3 Sequence diagram

```text
UI/CLI/API         Orchestrator API        Agent Pipeline          Artifact API        Snapshot Store         Child Installer        Audit/Event Store
    |                    |                       |                      |                    |                      |                     |
    | submit update ---->| validate intent       |                      |                    |                      |                     |
    | (22 -> 24)         | source/target explicit|                      |                    |                      |                     |
    |                    | assign job ---------->| AckClaim + lease     |                    |                      |                     |
    |                    |                       | DetectCurrentState    |                    |                      |                     |
    |                    |                       | (version=22.x)        |                    |                      |                     |
    |                    |                       | AcquireArtifact ----->| HEAD/GET(+Range)   |                      |                     |
    |                    |                       |<----------------------| bytes               |                      |                     |
    |                    |                       | ValidateSignatureAndHash (sha256 + signature)                     |                     |
    |                    |                       |---- fail? ----------->|                    |                      | StepFail(trust) --->|
    |                    |                       | (no mutation path)    |                    |                      |                     |
    |                    |                       | create snapshot ---------------------------->|                      |                     |
    |                    |                       |<--------------------------------------------| configSnapshotId      |                     |
    |                    |                       | StepStatus(snapshot_created, configSnapshotId) ------------------->|                     |
    |                    |                       | InstallOrUpgrade -------------------------------------------------->| run target 24        |
    |                    |                       |<--------------------------------------------------------------------| exit code            |
    |                    |                       | PostInstallVerify (expect 24.x)                                   |                     |
    |                    |                       |---- pass ---------------------------------------------------------->| Complete ----------->|
    |                    |                       |---- fail after mutation?                                           |                     |
    |                    |                       | restore using configSnapshotId --------------->| apply restore      |                     |
    |                    |                       |<-----------------------------------------------| restore outcome     |                     |
    |                    |<----------------------| final status + linkage (configSnapshotId, restoreAttemptId, linkedFailureEventId)        |
```

### 12.4 Failure branch detail

```text
Update execution failure
   |
   +--> A) Trust gate failure before mutation
   |       - Reason examples: signature_invalid, hash_mismatch, artifact_untrusted
   |       - Snapshot may exist or not, but no mutation occurred
   |       - Terminal: Failed (no restore execution required)
   |       - Audit linkage: failureEventId, trustEvidenceRef
   |
   +--> B) Mutation-stage failure (install/post-verify)
           - Precondition: configSnapshotId exists (created before mutation)
           - Classify reason:
               * migration_path_missing
               * install_failed
               * post_verify_failed
           - Execute restore using configSnapshotId
           - Record restoreAttemptId + restoreOutcome (restored | restore_failed)
           - Terminal:
               * FailedWithRestore (restore succeeded but update failed)
               * FailedRestoreFailed (both update and restore failed)
           - Persist linked audit chain:
               failureEventId -> configSnapshotId -> restoreAttemptId -> terminalEventId
```

### 12.5 Verification gates

- Snapshot created before mutation.
- Missing migration path is explicit failure reason (`migration_path_missing`).
- Restore attempt/outcome is auditable.

Traceability: FR-006, AC-007

---

## 13) Modify and Downgrade Storyboard

### 13.1 Risk-aware policy classes

- `retryabilityClass`: `none | transient_only | bounded`
- `idempotencyMode`: `detect | always | never | version_check`
- `riskLevel`: `low | medium | high`
- `approvalRequired`: `true | false`

### 13.2 Downgrade policy baseline

- Downgrade is high-risk by default.
- Default posture is disallow unless explicit approved path exists.
- High-risk/non-idempotent operation is not blind auto-retry.

### 13.3 Decision tree diagram

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

### 13.4 Verification gates

- Approval gate event exists for high-risk path.
- Rejection reason is explicit when approval missing.
- Snapshot contract checked before high-risk mutate.

Traceability: FR-001, FR-006, NFR-001, AC-002, AC-007, AC-101

---

## 14) Retry, Idempotency, and Replay Storyboard

### 14.1 Policy principles

- Retry is bounded and transient-focused.
- Non-idempotent/high-risk actions are never blindly auto-retried.
- Idempotency prevents duplicate side effects on replay.

### 14.2 Retry flow

```text
SignalR Hub        Agent SignalR      Agent JobChannel      Agent Worker      Policy Eval      Step Adapter        SQLite (Orch)
    |                   |                   |                   |                 |                |                   |
    | AssignJob ------->| enqueue --------->| dequeue --------->|                 |                |                   |
    |<------------------| AckClaim          |                   |                 |                |                   |
    |<------------------| LeaseHeartbeat    |                   |                 |                |                   |
    |                   |                   |                   | run step ------>| classify error |                   |
    |                   |                   |                   |                 |-- transient? -->|                   |
    |                   |                   |                   |                 |   high-risk/non-idempotent?        |
    |                   |                   |                   |                 |                |                   |
    |                   |                   |                   |<-- retry allowed (bounded) -------|                 |
    |                   |                   |                   | backoff t1/t2/t3 |                |                   |
    |<------------------| StepStatus(seq=s, state=Retrying, attempt=1/2/3)                           |
    | upsert step ----------------------------------------------------------------------------------->|
    |                   |                   |                   | rerun step ------>|                |                   |
    |                   |                   |                   | ... until success or attempts exhausted            |
    |<------------------| StepStatus(seq=s+1, state=Succeeded)                                       |
    | upsert step ----------------------------------------------------------------------------------->|
    |<------------------| Complete                                                                   |
    |<------------------| LeaseClose                                                                 |
    |                   |                   |                   |                 |                |                   |
    |                   |                   |                   |<-- retry denied/exhausted ---------|                 |
    |<------------------| Fail(reason=non_retryable_or_retry_exhausted)                              |
    | set terminal --------------------------------------------------------------------------------->|
    |<------------------| LeaseClose                                                                 |

Policy rules:
- Retry is bounded and transient-focused.
- High-risk/non-idempotent actions are not blindly auto-retried.
```

### 14.3 Idempotency flow

```text
Agent Worker          Agent SignalR         SignalR Hub         StepStatus Ingest        SQLite              Audit
    |                      |                    |                     |                    |                  |
    | StepStatus(seq=N,payload=P) ------------->|------------------->| build key          |                  |
    |                      |                    |                     | (jobId,stepId,N)   |                  |
    |                      |                    |                     | lookup ----------->|                  |
    |                      |                    |                     |<-- not found ------|                  |
    |                      |                    |                     | insert/upsert ---->|                  |
    |                      |<-------------------| ack accepted        |                    |                  |
    |                      |                    |                     |                    |                  |
    | replay StepStatus(seq=N,payload=P) ------>|------------------->| lookup ----------->|                  |
    |                      |                    |                     |<-- found ----------|                  |
    |                      |                    |                     | payload equal => idempotent no-op       |
    |                      |<-------------------| ack replay accepted |                    |                  |
    |                      |                    |                     |                    |                  |
    | replay StepStatus(seq=N,payload=Q!=P) --->|------------------->| lookup ----------->|                  |
    |                      |                    |                     |<-- found ----------|                  |
    |                      |                    |                     | reject conflict     |                  |
    |                      |<-------------------| nack conflict       |------------------->| sequence_payload_conflict
    |                      |                    |                     |                    |                  |

Idempotency contract:
- Idempotent ingest key is `(jobId, stepId, sequence)`.
- Same key + same payload => safe replay no-op.
- Same key + different payload => reject and audit.
```

### 14.4 Replay and reconnect flow

```text
Agent SignalR         SignalR Hub         StepStatus Ingest        SQLite             Agent Worker
    |                     |                     |                    |                    |
    | Connect ----------->|                     |                    |                    |
    | Register/Auth ----->|                     |                    |                    |
    |<---- AssignJob -----|                     |                    |                    |
    | AckClaim ---------->|                     |                    |                    |
    | LeaseHeartbeat ---->|                     |                    |                    |
    | StepStatus(seq=1) -->-------------------->| upsert ----------->|                    |
    | StepStatus(seq=2) -->-------------------->| upsert ----------->|                    |
    | (disconnect)        |                     |                    |                    |
    |---- drop ---------->|                     |                    |                    |
    |                     | read last ack ----->|                    |                    |
    |                     |<---- K=2 ----------|<-------------------|                    |
    | Connect ----------->|                     |                    |                    |
    | Register/Auth ----->|                     |                    |                    |
    |<-- resumeCursor=2 --|                     |                    |                    |
    | resend seq=3 ------>|-------------------->| upsert ----------->|                    |
    | resend seq=4 ------>|-------------------->| upsert ----------->|                    |
    | stale resend seq=2 ->|-------------------->| reject stale + audit                    |
    | StepStatus* -------->|-------------------->| idempotent ingest  |                    |
    | Complete/Fail ------>|                     |                    |                    |
    | LeaseClose --------->|                     |                    |                    |

Replay guardrails:
- Resume starts at `lastAcknowledgedSequence + 1`.
- Stale (`<= lastAck`) and out-of-window updates are rejected.
- Control/status replay is allowed; payload transfer remains HTTP artifact channel only.
```

### 14.5 Verification gates

- Retry count and intervals follow policy bounds.
- Replay with equal payload is safe.
- Replay with mismatched payload is rejected and audited.

Traceability: FR-002, NFR-001, AC-003, AC-101

---

## 15) Cancel and Rollback Storyboard

### 15.1 Cancel semantics

Cancel requests are runtime actions exposed via API/CLI.

Expected behavior:

1. Orchestrator marks cancellation intent.
2. Agent receives cancellation signal at safe interruption point.
3. Agent stops/terminates child process according to policy.
4. Job transitions to cancelled/failure state with reason.

### 15.2 Rollback semantics

Rollback behavior depends on operation and available rollback contract:

- If rollback/snapshot contract exists: execute restore path.
- If no rollback contract: mark terminal failure with explicit reason.

### 15.3 Sequence diagram

```text
Operator              API/Orch                Agent                 Child Proc
  |                     |                        |                        |
  | cancel request ---->| mark cancel intent     |                        |
  |                     | signal cancel -------->|                        |
  |                     |                        | graceful stop -------->|
  |                     |                        | force kill (if timeout)|
  |                     |                        |<-----------------------|
  |                     |<-----------------------| emit final state       |
```

### 15.4 Verification gates

- Cancel state transition is auditable.
- Child process termination policy is followed.
- Final reason code clearly distinguishes cancel vs execution failure.

Traceability: FR-001, AC-002, NFR-002

---

## 16) Child Process Execution Security Storyboard

### 16.1 Security objectives

- Prevent unintended privilege escalation.
- Bound process runtime/resources.
- Keep command invocation auditable and sanitized.

### 16.2 Spawn policy flow

```text
Agent step wants installer execution
   -> build allowed executable + args
   -> sanitize/validate arguments
   -> launch with constrained token/profile
   -> enforce timeout + resource limits
   -> capture stdout/stderr + exit code
   -> map to normalized reason/status
```

### 16.3 Constrained execution diagram

```text
Agent Runtime
   |
   +-- Policy Guard
   |     - allowlist executable path
   |     - sanitize args
   |     - timeout budget
   |     - cpu/mem bounds
   |
   +-- Spawn child process (constrained)
   |
   +-- Monitor + terminate on violation
```

### 16.4 Verification gates

- Disallowed command/arg pattern is blocked.
- Timeout/limit violations are visible in telemetry.
- Exit reason mapping is deterministic and auditable.

Traceability: NFR-002, AC-102

---

## 17) Artifact Trust Validation Storyboard

### 17.1 Validation sequence

1. Acquire artifact bytes from internal source.
2. Compute digest.
3. Compare to immutable manifest digest.
4. Validate signature/trust evidence as configured.
5. Proceed only on pass.

### 17.2 Trust check diagram

```text
Acquire artifact
    -> digest check
       -> mismatch? fail artifact_tampered
    -> signature/trust check
       -> invalid? fail signature_invalid
    -> emit trust evidence fields
    -> continue pipeline
```

### 17.3 Verification gates

- Digest mismatch always blocks execution.
- Invalid signature evidence always blocks execution.
- Trust evidence recorded in audit/log context.

Traceability: NFR-002, AC-102

---

## 18) Security and Identity Lifecycle Storyboard

### 18.1 Identity phases

| Phase        | Auth mechanism               | Allowed purpose           |
| ------------ | ---------------------------- | ------------------------- |
| Enrollment   | one-time token               | first bind only           |
| Steady-state | mTLS per-agent cert identity | all runtime reconnect/ops |

### 18.2 Lifecycle sequence

```text
Enroll token issue
   -> agent first connect with token
   -> token validation + identity bind
   -> cert material issuance/binding
   -> token invalidation
   -> steady-state reconnect via mTLS only
```

### 18.3 Rejection branch

```text
Reconnect attempt
   -> no cert / invalid cert / unbound cert
      -> reject connection
      -> emit auth failure audit
```

### 18.4 Verification gates

- Token cannot be reused post enrollment.
- mTLS binding is required for reconnect.
- Invalid cert reconnect is rejected.

Traceability: FR-004, NFR-002, AC-005, AC-102

---

## 19) Observability and Audit Storyboard

### 19.1 Core telemetry contract

Every job must have:

- Root span
- Step-level spans
- Correlation fields on status/events: `jobId`, `nodeId`, `step`, `reasonCode`, `sequence`, `leaseId`

### 19.2 Runtime emission sequence (control/status -> evidence)

```text
Agent Worker          Agent SignalR         SignalR Hub         StepStatus Ingest        Audit/Event Store        OTel Export
    |                      |                    |                     |                       |                    |
    | StepStatus(seq=1) -->|------------------->|-------------------->| validate + upsert      |                    |
    |                      |                    |                     | write step event ----->|                    |
    |                      |                    |                     | emit span/log ------------------------------->|
    | StepStatus(seq=2..n)->|------------------->|-------------------->| repeat correlation      |                    |
    | Complete/Fail ------>|------------------->|-------------------->| set terminal ---------->|                    |
    | LeaseClose --------->|------------------->|-------------------->| close assignment event->|                    |
    |                      |                    |                     | emit final span/log ------------------------->|
```

### 19.3 Operator investigation sequence (UI/CLI)

```text
Operator UI/CLI        Orchestrator API       SQLite/Audit Store       OTel File Store
     |                      |                        |                       |
     | open job details --->| read job/step timeline |                       |
     |                      |----------------------->|                       |
     |                      |<-----------------------|                       |
     | request evidence ---->| resolve audit refs ---->|                      |
     |                      | read telemetry pointers ----------------------->|
     |                      |<-----------------------------------------------|
     |<---------------------| timeline + reasons + linked evidence bundle    |
```

### 19.4 OTel file export baseline

Phase 1 default is file-based export with rotation/retention and redaction controls.

### 19.5 Redaction/export flow

```text
Runtime events -> Telemetry pipeline -> Redaction policy -> File exporter
                                               |
                                               +-> restricted access policy
```

### 19.6 Verification gates

- Correlation fields present for each step event.
- Sensitive fields redacted by policy.
- Rotation/retention settings enforced.

Traceability: NFR-003, NFR-002, AC-103, AC-102

---

## 20) API/UI/CLI Runtime Surfaces Storyboard

### 20.1 Surface split

| Surface     | Purpose                           | Runtime status       |
| ----------- | --------------------------------- | -------------------- |
| REST API    | canonical runtime control surface | Required             |
| Embedded UI | operator visibility + actions     | Required             |
| CLI         | automation over API               | Required             |
| Scripts     | provisioning/bootstrap only       | Allowed as exception |

### 20.2 UI operation sequence (sysadmin)

```text
Sysadmin            Embedded UI             Orchestrator API         Runtime/Audit
   |                    |                         |                       |
   | open Install page ->|                         |                       |
   | choose targets/pkg  |                         |                       |
   | submit job -------->| POST /api/jobs -------->| validate/persist/queue |
   |<--------------------| 202 jobId               |                       |
   | open timeline ----->| GET /api/jobs/{id}/steps ----------------------->|
   |<--------------------| live statuses + reasons  |                       |
   | cancel if needed -->| POST /api/jobs/{id}/cancel --------------------->|
   |<--------------------| cancel accepted/denied   |                       |
```

### 20.3 CLI operation sequence (automation)

```text
Automation/Operator         CLI Client               Orchestrator API
       |                       |                           |
       | di jobs create ------>| POST /api/jobs ---------->|
       |<----------------------| jobId/state               |
       | di jobs status ------>| GET /api/jobs/{id} ------>|
       |<----------------------| state/summary             |
       | di jobs watch ------->| stream/poll steps ------->|
       |<----------------------| seq + step + reason       |
       | di jobs cancel ------>| POST /api/jobs/{id}/cancel|
       |<----------------------| cancel result             |
```

### 20.4 UI information architecture snapshot

```text
+--------------------------------------------------------------------------------+
| Distributed Installer                                                          |
|--------------------------------------------------------------------------------|
| [Dashboard] [Jobs] [Nodes] [Artifacts] [Policies] [Audit]                    |
|--------------------------------------------------------------------------------|
| Quick Actions: [Create Job] [Cancel Job] [Download Evidence]                  |
+--------------------------------------------------------------------------------+
```

### 20.5 CLI surface snapshot

```text
di jobs create --manifest .\node24.json --targets node-001,node-002
di jobs status --job-id job-20260414-001
di jobs cancel --job-id job-20260414-001 --reason "operator_request"
di nodes list
di artifacts upload --file .\nodejs.zip --manifest .\nodejs.manifest.json
```

### 20.6 UI vs CLI comparison

| Dimension                   | UI     | CLI    |
| --------------------------- | ------ | ------ |
| Fast situational visibility | Strong | Medium |
| Batch automation            | Medium | Strong |
| Interactive approvals       | Strong | Medium |
| CI/script integration       | Medium | Strong |

### 20.7 Verification gates

- Runtime operations are possible without script orchestration.
- UI timeline reflects live status transitions.
- CLI maps 1:1 to API behavior.

Traceability: NFR-004, AC-104, FR-001, AC-001, AC-002

---

## 21) Orchestrator Self-Update Storyboard (Staged Swap + Supervisor)

### 21.1 Why this pattern

Naive in-place overwrite is unsafe for running process updates.

Phase 1 normative pattern:

- staged candidate placement
- supervisor/wrapper mediated process handoff
- startup health gate and rollback path

### 21.2 High-level flow

1. Admin triggers self-update intent.
2. Orchestrator downloads candidate package.
3. Validate candidate trust and compatibility rules.
4. Stage candidate beside current binary.
5. Supervisor stops old process and starts candidate.
6. Health gate verifies startup.
7. On failure, rollback to previous binary.

### 21.3 Sequence diagram

```text
Admin/API          Current Orchestrator         Artifact Source/API        Supervisor/Wrapper        Candidate Orchestrator        Health Probe        Audit/Event Store
   |                       |                            |                         |                            |                      |                  |
   | self-update request ->| validate auth/rbac         |                         |                            |                      |                  |
   |                       | download candidate ------->|                         |                            |                      |                  |
   |                       |<---------------------------| package bytes            |                            |                      |                  |
   |                       | verify candidate trust (signature + sha256 hash)     |                            |                      |                  |
   |                       | verify compatibility (phase1 constraints, config/schema)                           |                      |                  |
   |                       | stage candidate beside current binary                 |                            |                      |                  |
   |                       | write staged-swap manifest -------------------------> |                            |                      |                  |
   |                       | handoff + graceful stop ----------------------------> |                            |                      |                  |
   |                       | (process exits)                                      | start candidate ---------->| startup              |                  |
   |                       |                                                     |---------------------------->|                      |                  |
   |                       |                                                     | probe /health --------------------------------------->|
   |                       |                                                     |<------------------------------------------------------| pass/fail          |
   |                       |                                                     | if pass: commit switch + mark previous as fallback   |                  |
   |                       |                                                     | if fail: stop candidate, restart previous ---------->|                  |
   |                       |                                                     | emit terminal outcome ----------------------------------------------------->|
   |<----------------------| update result (Succeeded | RolledBack | Failed)     |                            |                      |                  |
```

### 21.4 State machine

```text
Idle
  -> DownloadingCandidate
  -> VerifyingCandidateTrustAndCompatibility
      (signature/hash/compatibility must pass)
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

### 21.5 Verification gates

- Candidate signature/hash passes before switch.
- Startup health is required for completion.
- Failed startup triggers rollback with audit chain.

Traceability: PRD self-update decision, NFR-005, AC-105

---

## 22) Persistence Storyboard (SQLite Canonical Entities)

### 22.1 Entity baseline

Canonical Phase 1 entities:

- `Job`
- `Node`
- `AssignmentLease`
- `ConfigSnapshot`

### 22.2 Persistence interaction flow

```text
API request
   -> validate input
   -> transaction begin
   -> write job + assignment rows
   -> commit
   -> dispatch assignment
```

Ordering invariant:

- Do not dispatch assignment before durable persistence success.

### 22.3 Lease and status persistence flow

```text
Incoming LeaseHeartbeat/StepStatus
   -> protocol validation
   -> idempotency/replay checks
   -> persist state transition
   -> emit audit/telemetry
```

### 22.4 Verification gates

- Runtime state not dependent on in-memory-only stores.
- Entity transitions remain queryable for evidence/audit reconstruction.

Traceability: FR-001, FR-006, NFR-001, AC-001, AC-007, AC-101

---

## 23) DevOps and Deployment Boundary Storyboard

### 23.1 Policy baseline

- Pipeline may build/sign/package/deploy orchestrator.
- Pipeline must not directly execute workstation runtime install/upgrade/rollback.
- Runtime node actions occur only via orchestrator API/CLI path.

### 23.2 CI/CD flow

```text
CI -> Build/Test -> Publish self-contained orchestrator -> Sign artifacts
   -> Packaging validation (clean host launch)
   -> Deploy orchestrator
   -> Integration/E2E gates
```

### 23.3 Boundary enforcement diagram

```text
Azure Pipeline
   |
   +-- allowed: orchestrator package + deploy
   |
   +-- forbidden: direct node runtime install tasks
                     (must route through API/CLI at runtime)
```

### 23.4 Verification gates

- Pipeline definitions show orchestrator-only runtime boundary.
- Clean-host launch validation passes.
- Runtime node actions are auditable from orchestrator surfaces.

Traceability: NFR-004, NFR-005, AC-104, AC-105

---

## 24) End-to-End Multi-Node Storyboard

### 24.1 Scenario

Install package to two nodes in parallel with bounded orchestration concurrency.

### 24.2 Timeline

```text
T0  submit job targeting node-001,node-002
T1  assignments dispatched to both nodes
T2  node-001 acquire/validate/install
T2  node-002 acquire/validate/install
T3  node-001 complete
T4  node-002 complete
T5  orchestrator marks job terminal success
```

### 24.3 Parallelism diagram

```text
              +--> node-001 pipeline --> done
job assign ---|
              +--> node-002 pipeline --> done

job state = terminal when all target assignment states terminal
```

### 24.4 Partial failure branch

```text
node-001 success, node-002 failure
   -> job terminal = partial failure/failure (policy-defined)
   -> audit includes per-node final reason
```

### 24.5 Verification gates

- Per-node states are independently visible.
- Aggregated job outcome logic is deterministic and auditable.

Traceability: FR-001, AC-001, AC-002, NFR-003

---

## 25) Fault Injection Storyboard

### 25.1 Fault set

- Checksum mismatch
- Network interruption during artifact download
- Agent disconnect mid-job
- Retry exhaustion
- Invalid cert reconnect

### 25.2 Fault handling matrix

| Fault                  | Expected system behavior     | Evidence                              |
| ---------------------- | ---------------------------- | ------------------------------------- |
| Checksum mismatch      | fail closed before execution | trust failure event + terminal reason |
| Network interruption   | bounded retry if transient   | retry timeline + counts               |
| Agent disconnect       | lease stale policy execution | AssignedStale transitions             |
| Retry exhaustion       | terminal fail with reason    | final reason + attempts               |
| Invalid cert reconnect | reject connection            | auth reject audit                     |

### 25.3 Fault timeline diagram

```text
Normal step
   -> injected fault
   -> policy evaluation
   -> branch to retry or fail/restore
   -> emit final auditable state
```

### 25.4 Verification gates

- Fault outcomes match policy model.
- No silent failures.
- Evidence is reconstructable from logs/events/state.

Traceability: Testing decisions in PRD, AC-101, AC-102, AC-103

---

## 26) Verification Checklist by Storyboard

### 26.1 Orchestrator install

- [ ] Self-contained EXE launches on clean Windows host.
- [ ] `/health` is healthy.
- [ ] Embedded UI is reachable.
- [ ] SQLite + artifact path initialized correctly.

### 26.2 Agent bootstrap

- [ ] One-time token issued and consumed.
- [ ] Service installed/running.
- [ ] mTLS reconnect succeeds.
- [ ] invalid-cert reconnect fails.

### 26.3 Artifact ingestion/delivery

- [ ] Upload stores immutable digest-bound record.
- [ ] Agent fetches only from internal artifact API.
- [ ] Range/chunk works for large artifacts.
- [ ] hash/signature mismatch blocks execution.

### 26.4 Runtime protocol and lease

- [ ] Canonical sequence observed.
- [ ] StepStatus ingest idempotency behavior correct.
- [ ] Payload conflict rejected and audited.
- [ ] stale timeout/reassignment bounds enforced.

### 26.5 Pipeline execution

- [ ] Full local typed pipeline executes in order.
- [ ] Adapter normalization is consistent.
- [ ] terminal state and reasons are deterministic.

### 26.6 Update/modify/rollback

- [ ] snapshot created before mutate.
- [ ] restore path executed on qualifying failure.
- [ ] downgrade/high-risk approval gates enforced.

### 26.7 Operator surfaces

- [ ] API/UI/CLI can execute runtime actions.
- [ ] script surface not required for runtime operations.
- [ ] step timeline visible and correlated.

### 26.8 Packaging/devops

- [ ] orchestrator-only deployment boundary enforced.
- [ ] no direct workstation deployment from pipeline.

---

## 27) Traceability Matrix (Storyboard -> AC)

| Storyboard area                 | Primary AC coverage    |
| ------------------------------- | ---------------------- |
| Packaging and clean-host launch | AC-105                 |
| Fresh orchestrator install      | AC-001, AC-105         |
| Agent bootstrap and identity    | AC-005, AC-102         |
| Artifact ingestion and trust    | AC-006, AC-102         |
| HTTP artifact delivery          | AC-006                 |
| Runtime sequence + idempotency  | AC-003                 |
| Lease/stale policy              | AC-101                 |
| Local typed pipeline            | AC-004, AC-006         |
| Install/update/modify flows     | AC-001, AC-002, AC-007 |
| Observability/timeline          | AC-103                 |
| API/CLI runtime surface         | AC-104                 |
| Security overlays end-to-end    | AC-102                 |

---

## 28) Consolidated Contradiction Resolutions from Earlier Drafts

This final canonical document resolves prior draft inconsistencies as follows:

1. SignalR payload ambiguity
    - Final: SignalR is control/status only; artifacts use HTTP.

2. Self-update mechanism ambiguity
    - Final: staged swap + supervisor/wrapper; no naive in-place overwrite semantics.

3. Retry overgeneralization
    - Final: retry is policy-driven and bounded; high-risk/non-idempotent no blind auto-retry.

4. Package source ambiguity
    - Final: internal-only runtime source from orchestrator artifact store.

5. Ping vs lease ambiguity
    - Final: explicit semantic separation and state impact boundaries.

6. Token lifecycle ambiguity
    - Final: one-time enrollment token; steady-state mTLS identity required.

7. Runtime execution ownership ambiguity
    - Final: agent executes local step pipeline; orchestrator controls job-level state/policy.

---

## 29) Deferred Items (Explicitly Out of Phase 1)

- Linux agent implementation
- Multi-orchestrator HA and disaster recovery semantics
- Advanced key rotation operations cadence and incident forensics depth
- Extended observability indexing/retention operations beyond PoC defaults
- Rollout ring automation and broad environment matrix hardening

---

## 30) Appendix A - Detailed Sequence Cards

### A.1 Install sequence card

```text
Card: Install-01
Trigger: create install job
Preconditions:
  - target node online
  - package manifest exists in artifact store
  - caller authorized

Main path:
  1) create job
  2) assign
  3) ack claim
  4) lease heartbeat
  5) step pipeline (7 steps)
  6) complete
  7) lease close

Failure paths:
  - auth denied
  - artifact trust fail
  - installer failure (policy retry branch)
  - lease stale timeout
```

### A.2 Update sequence card

```text
Card: Update-01
Trigger: update job with explicit target version
Preconditions:
  - current version detected
  - migration/restore contract available where required

Main path:
  1) snapshot pre-mutation
  2) execute update
  3) verify target version
  4) complete

Failure path:
  - restore from snapshot
  - emit linked audit
  - terminal fail if restore incomplete
```

### A.3 Cancel sequence card

```text
Card: Cancel-01
Trigger: operator cancel request
Preconditions:
  - job in cancellable state

Main path:
  1) mark cancel requested
  2) notify agent
  3) stop child process (grace + force if needed)
  4) emit final state/reason
```

### A.4 Bootstrap sequence card

```text
Card: Bootstrap-01
Trigger: enroll new node
Preconditions:
  - admin authorization

Main path:
  1) issue one-time token
  2) install/start agent service
  3) first connect with token
  4) bind cert identity
  5) invalidate token
  6) reconnect mTLS

Failure path:
  - reverse-order cleanup
```

---

## 31) Appendix B - UI Mockup Comparisons

### B.1 Timeline layout comparison

Option A (table-first):

```text
+---------------------------------------------------------------+
| Seq | Step | Status | Start | End | Duration | Reason         |
+---------------------------------------------------------------+
```

Option B (lane-first):

```text
+---------------------------------------------------------------+
| [PreCheck]---[Acquire]---[Validate]---[Install]---[Verify]    |
|             color-coded states + click for details             |
+---------------------------------------------------------------+
```

PoC recommendation:

- Use table-first as baseline (faster implementation, clearer evidence mapping).
- Optional lane visualization as secondary enhancement if time permits.

### B.2 Job details panel comparison

Option A:

```text
Left: metadata
Right: step timeline
Bottom: logs/events
```

Option B:

```text
Tabbed: Overview | Steps | Logs | Policy Decisions | Audit
```

PoC recommendation:

- Option B for better operator workflow and cleaner evidence navigation.

### B.3 Node page comparison

Option A summary-heavy:

```text
Node list only with health dots
```

Option B operational:

```text
Node card includes:
  - connectivity state
  - lease state
  - last job result
  - agent version
  - cert binding status
```

PoC recommendation:

- Option B; supports bootstrap + runtime troubleshooting directly.

---

## 32) Appendix C - CLI Interaction Mockups

### C.1 Create job

```text
> di jobs create --manifest .\node24.json --targets node-001
Job created: job-20260414-001
State      : Queued
Operation  : install
Targets    : node-001
```

### C.2 Stream status

```text
> di jobs watch --job-id job-20260414-001
[12:00:01] node-001 seq=1 PreConditionCheck Succeeded
[12:00:11] node-001 seq=2 AcquireArtifact Succeeded
[12:00:13] node-001 seq=3 ValidateSignatureAndHash Succeeded
[12:00:14] node-001 seq=4 DetectCurrentState Succeeded
[12:00:25] node-001 seq=5 InstallOrUpgrade Succeeded
[12:00:27] node-001 seq=6 PostInstallVerify Succeeded
[12:00:28] node-001 seq=7 EmitFinalization Succeeded
Final: Completed
```

### C.3 Cancel

```text
> di jobs cancel --job-id job-20260414-001 --reason "maintenance window"
Cancel requested for job-20260414-001
Current state: CancelRequested
```

---

## 33) Appendix D - Minimal API Contract Sketches for Storyboard Alignment

```text
POST /api/jobs
GET  /api/jobs/{jobId}
GET  /api/jobs/{jobId}/steps
POST /api/jobs/{jobId}/cancel
GET  /api/nodes
POST /api/nodes/enroll

POST /api/artifacts/upload
HEAD /api/artifacts/{packageId}/{version}
GET  /api/artifacts/{packageId}/{version}
```

Required runtime message envelope fields (control plane):

```text
assignmentId
leaseId
jobId
agentId
sequence
messageType
timestampUtc
```

---

## 34) Final Review Checklist for This Document

- [x] Aligns with PRD final decisions
- [x] Aligns with implementation tracker task boundaries
- [x] Includes detailed, diagram-heavy storyboards in style of draft `15`
- [x] Consolidates useful details from `16` and earlier canonical summary
- [x] Explicitly defines verification gates per major flow
- [x] Captures protocol/security/policy semantics without Phase 2 scope creep

---

## Related Documents

- `docs/distributed-installer/poc-phase1-prd-final.md`
- `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
- `docs/distributed-installer/08-requirements-contract.md`
- `docs/distributed-installer/09-security-pack.md`
- `docs/distributed-installer/10-core-contracts-pack.md`
- `docs/distributed-installer/11-config-persistence-contract.md`
- `docs/distributed-installer/12-devops-pipeline-design-pack.md`
- `docs/distributed-installer/13-poc-phase1-definition-of-done.md`
