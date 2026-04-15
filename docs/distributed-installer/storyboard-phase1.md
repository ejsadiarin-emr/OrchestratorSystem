# Installation and Operational Storyboards (PoC Phase 1)

Date: 2026-04-15
Status: Canonical execution storyboards for PoC Phase 1
Scope: Windows-first, single-orchestrator distributed installer runtime

Derived from:

- `docs/distributed-installer/poc-phase1-prd-final.md`
- `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
- `docs/distributed-installer/08-requirements-contract.md`
- `docs/distributed-installer/09-security-pack.md`
- `docs/distributed-installer/10-core-contracts-pack.md`
- `docs/distributed-installer/11-config-persistence-contract.md`
- `docs/distributed-installer/12-devops-pipeline-design-pack.md`
- `docs/distributed-installer/13-poc-phase1-definition-of-done.md`

---

## Purpose

This document defines end-to-end operational behavior for PoC Phase 1.

It specifies:

- Install, update, modify, cancel, rollback, and self-update flows
- Runtime protocol and lease semantics
- Security and trust boundary controls
- Internal-only artifact ingestion and delivery behavior
- API/UI/CLI runtime operation surfaces
- Verification gates and AC-oriented evidence requirements

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

### Trust boundaries

| Boundary | From          | To               | Primary risk           | Required controls                 |
| -------- | ------------- | ---------------- | ---------------------- | --------------------------------- |
| TB-01    | Admin         | Orchestrator API | Privilege abuse/spoof  | RBAC, authN/authZ, audit          |
| TB-02    | Agent         | SignalR Hub      | Replay/spoofing        | enrollment->mTLS, sequence checks |
| TB-03    | Orch API      | Artifact store   | tamper/substitution    | immutable digest metadata, ACL    |
| TB-04    | Agent         | Artifact API     | MITM/tamper            | TLS, hash+signature validation    |
| TB-05    | Orch          | SQLite           | state integrity        | app-level validation + host ACL   |
| TB-06    | Agent service | Child process    | escalation/unsafe args | constrained spawn policy          |

### Global invariants

- Artifact trust validation occurs before execution and fails closed.
- Runtime sequence validation is strict; stale/out-of-order updates are rejected.
- Idempotent ingest key is `(jobId, stepId, sequence)`.
- Same-key payload mismatch is rejected and audited.
- Reconnect resumes from `lastAcknowledgedSequence + 1`.

---

## Core Storyboard Map

| Storyboard | Purpose |
| --- | --- |
| Media packaging | Build/sign/publish orchestrator package |
| Fresh orchestrator install | Bring up API/UI/Hub/persistence deterministically |
| Agent install via WinRM | Enroll node and bind identity (`token -> mTLS`) |
| Package lifecycle | Ingest -> submit -> assign -> execute -> observe |
| Modify workload | Update/downgrade/self-update with policy gates |

---

## Media Packaging Storyboard

### Packaging posture

| Option | What it gives | Phase 1 decision |
| --- | --- | --- |
| Self-contained EXE | Clean-host startup, simple operator path | Selected (primary) |
| ZIP bundle | Easy transfer and scripted unpack | Supported |
| ISO media | Offline distribution pattern | Deferred |

### Sequence

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
   |                         |                    | operator download |
```

### Verification gates

- Signer/hash verification succeeds.
- Package launches on clean host without .NET/IIS preinstall.

Traceability: AC-105, NFR-005

---

## Fresh Orchestrator Install Storyboard

### Step-by-step flow

1. Admin stages `Orchestrator.exe` (or extracts ZIP).
2. Admin runs initialization (interactive or scripted config).
3. Config captures:
   - listen URL/port
   - admin bootstrap auth settings
   - SQLite path
   - artifact storage path
   - telemetry export mode
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
  |<------------------------------| 200 healthy                       |
```

### Verification gates

- `GET /health` returns healthy.
- Embedded UI loads from orchestrator host.
- `GET /api/nodes` returns valid schema.
- SQLite file/schema initialize.
- Artifact path is writable and access-controlled.

Traceability: FR-001, AC-001, AC-105

---

## Agent Installation Storyboard (Token -> mTLS)

### Bootstrap options

| Method | Benefit | Limitations | Phase 1 |
| --- | --- | --- | --- |
| Manual PowerShell script | Fast and practical | Operator variance | Selected |
| WinRM remoting script | Remote convenience | Environment prerequisites | Supported |
| GPO/SCCM enterprise distribution | Fleet scale | Outside PoC scope | Deferred |

### Main flow

1. Admin requests enrollment token for target node.
2. Admin runs bootstrap script on target machine.
3. Script installs agent executable/service config.
4. Agent connects with one-time token.
5. Orchestrator validates token and binds node identity.
6. Certificate material is issued for steady-state mTLS.
7. Agent reconnects with bound certificate.
8. Enrollment token is invalidated.

### Sequence

```text
Operator WS              Target Machine (Remote)         Agent Service            Orchestrator
    |                            |                            |                         |
    | POST /api/nodes/enroll ---------------------------------------------------------->|
    |<------------------------------------------------------- {token,nodeId,ttl} -------|
    | Invoke-Command ------------------------------------------------------------------>|
    |  - download Agent.exe from orchestrator                                           |
    |  - write config with orchestratorUrl + token + nodeId                             |
    |  - create/start service                                                            |
    |--------------------------->| install files/config/service |                       |
    |                            |----------------------------->| Connect(token) -------->|
    |                            |                            |<--------- bind cert -------
    |                            |                            | Reconnect(mTLS cert) ---->|
    |                            |                            |<--------------- accepted |
    | GET /api/nodes/{nodeId} ---------------------------------------------------------->|
    |<----------------------------------------------------- {status:"online",auth:"mtls"}|
```

### Bootstrap failure cleanup (transactional)

Cleanup order (reverse application order):

1. Stop service if started
2. Remove service registration
3. Remove config file
4. Remove installed binaries
5. Revoke/expire token state
6. Emit cleanup audit event

### Verification gates

- Windows service exists/running.
- Node appears online.
- Lease heartbeat observed.
- Token cannot be reused.
- Invalid/unbound cert reconnect is rejected.
- Cleanup branch leaves no partial residue.

Traceability: FR-004, AC-005, AC-102

---

## Artifact Ingestion Storyboard (Internal-Only Source)

### Policy baseline

- Agents do not fetch runtime artifacts from internet/vendor at execution time.
- Upstream binaries are ingested into orchestrator artifact store first.
- Artifact version records are immutable and digest-bound.

### Ingestion flow

1. Admin uploads package/bundle + metadata.
2. Orchestrator computes canonical digest.
3. Orchestrator validates trust evidence (signature/checksum metadata).
4. Orchestrator writes immutable package version record.
5. Optional org attestation signs metadata envelope (not vendor binary).

### Manifest model (Phase 1 minimum)

```json
{
  "packageId": "nodejs",
  "displayName": "Node.js 24 LTS",
  "version": "24.0.0",
  "channel": "stable",
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

### Verification gates

- Ingest audit event exists with provenance fields.
- Digest is stored and immutable for package version record.
- Channel is one of `stable|canary|test`.
- Invalid trust evidence is blocked.

Traceability: FR-005, AC-006, AC-102

---

## Artifact Delivery Storyboard (HTTP + Range/Chunk)

### Transport decision

| Path | Purpose | Selected |
| --- | --- | --- |
| SignalR payload transfer | artifact bytes | No |
| HTTP GET/HEAD artifact endpoints | artifact bytes | Yes |
| Range/chunk retrieval | large payload reliability | Yes |

### Rule

SignalR MUST NOT carry artifact payload bytes.

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

Traceability: FR-005, AC-006, AC-102

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
    |              |----------------->| validate auth/schema/policy          |                  |                |                   |                  |                |
    |              |                  | persist Job ------>|                 |                  |                |                   |                  |                |
    |              |                  | persist Assignment>|                 |                  |                |                   |                  |                |
    |              |                  | enqueue dispatch ------------------->|                  |                |                   |                  |                |
    |              |<-----------------| 202 Accepted + jobId                 |                  |                |                   |                  |                |
    | open timeline| GET /api/jobs/{id}, /steps                             |                  |                |                   |                  |                |
    |<-------------|<-----------------| read timeline ---->|                 |                  |                |                   |                  |                |
    |              |                  |                   |                  | dequeue dispatch  |                |                   |                  |                |
    |              |                  |                   |                  |----------------->| AssignJob      |                   |                  |                |
    |              |                  |                   |                  |                  |--------------->| AckClaim          |                  |                |
    |              |                  | update lease row ->|                 |                  |<---------------|                   |                  |                |
    |              |                  |                   |                  |                  |<---------------| LeaseHeartbeat    |                  |                |
    |              |                  |                   |                  |                  |                | enqueue assignment|                  |                |
    |              |                  |                   |                  |                  |                |------------------>| run pipeline      |                |
    |              |                  |                   |                  |                  |                |                   | spawn ----------->|                |
    |              |                  |                   |                  |                  |                |                   | GET/HEAD(+Range) ------------------->|
    |              |                  |                   |                  |                  |                |                   |<-------------------------------------|
    |              |                  |                   |                  |                  |<---------------| StepStatus(seq*)  |                  |                |
    |              |                  | upsert step row -->|                 |                  |                |                   |                  |                |
    |              |                  |                   |                  |                  |<---------------| Complete/Fail     |                  |                |
    |              |                  | set terminal row ->|                 |                  |                |                   |                  |                |
    |              |                  |                   |                  |                  |<---------------| LeaseClose        |                  |                |

Important:
- Orchestrator persists `Job` and `Assignment` rows before enqueueing dispatch.
- SignalR carries control/status messages only (AssignJob, AckClaim, LeaseHeartbeat, StepStatus, Complete/Fail, LeaseClose).
- Artifact payload bytes are transferred only through HTTP artifact endpoints.
```

### Verification gates

- Request rejected for invalid/missing policy fields.
- Assignment emitted only after persistence success.
- `AckClaim` includes assignment/lease identifiers.

Traceability: FR-001, FR-002, AC-001, AC-003

---

## Runtime Control-Plane Protocol Storyboard

### Canonical message lifecycle

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

### Sequence validation rules

- Reject stale sequence (`<= last acknowledged`).
- Reject out-of-order gaps beyond tolerated replay model.
- Idempotent upsert key: `(jobId, stepId, sequence)`.
- Same-key payload mismatch => reject + audit `sequence_payload_conflict`.

### Reconnect behavior

- Orchestrator returns resume cursor.
- Agent resumes from `lastAcknowledgedSequence + 1`.
- Replayed same-key same-payload is idempotent no-op.
- Replayed same-key different payload is rejected and audited.

### Verification gates

- Replay does not duplicate side effects.
- Payload conflict is rejected and audited.
- Resume behavior is deterministic.

Traceability: FR-002, NFR-001, AC-003, AC-101

---

## Lease and Liveness Storyboard

### Semantic split

| Signal | Direction | Meaning | Used for |
| --- | --- | --- | --- |
| Ping | Orchestrator -> Agent | connectivity probe | dashboard connectivity posture |
| LeaseHeartbeat | Agent -> Orchestrator | lease ownership renewal | stale detection/reassignment |

### Lease defaults (Phase 1)

- Lease TTL: `90s`
- Heartbeat interval: `15s`
- Stale threshold: `3` missed heartbeats
- Auto-fail bound: `2` reassignment attempts OR `15m` stale duration

### Lease state flow

```text
Assigned
  -> Active
     -> MissedHeartbeat(1)
     -> MissedHeartbeat(2)
     -> MissedHeartbeat(3)
     -> AssignedStale
        -> Reassigned (attempt < bound)
        -> AutoFail (bound reached)
```

### Verification gates

- Missing Ping updates connectivity posture only.
- Missing LeaseHeartbeat drives stale transitions.
- Reassign/fail behavior follows configured bounds.

Traceability: NFR-001, AC-101

---

## Agent Local Typed Pipeline Storyboard

### Pipeline contract

Agent executes full per-job pipeline locally.

Ordered steps:

1. `PreConditionCheck`
2. `AcquireArtifact`
3. `ValidateSignatureAndHash`
4. `DetectCurrentState`
5. `InstallOrUpgrade`
6. `PostInstallVerify`
7. `EmitFinalization`

### Adapter normalization

| Adapter type | Raw exit/behavior | Normalized reason/status |
| --- | --- | --- |
| MSI | 0 | success |
| MSI | 3010 | success_reboot_required |
| MSI | 1602 | cancelled_by_user_or_policy |
| EXE | vendor-specific code | mapped via adapter rules |

### Verification gates

- Step order is preserved.
- Each step emits status with required correlation fields.
- Adapter outputs are normalized consistently.

Traceability: FR-003, FR-005, AC-004, AC-006

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

Traceability: FR-001, FR-003, AC-001, AC-002, AC-004

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

### Failure taxonomy

```text
A) Trust gate failure before mutation
   - signature_invalid, hash_mismatch, artifact_untrusted
   - no mutation; terminal Failed

B) Mutation-stage failure
   - migration_path_missing
   - install_failed
   - post_verify_failed
   - execute restore via configSnapshotId
   - terminal FailedWithRestore or FailedRestoreFailed
```

### Verification gates

- Snapshot exists before mutation.
- Missing migration path is explicit (`migration_path_missing`).
- Restore attempt/outcome is auditable and linked.

Traceability: FR-006, AC-007

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

Traceability: FR-001, FR-006, AC-002, AC-007, AC-101

---

## Retry, Idempotency, and Replay Storyboard

### Policy principles

- Retry is bounded and transient-focused.
- High-risk/non-idempotent actions are never blindly auto-retried.
- Idempotency prevents duplicate side effects on replay.

### Verification gates

- Retry counts/intervals follow policy bounds.
- Replay with equal payload is safe.
- Replay with mismatched payload is rejected and audited.

Traceability: FR-002, NFR-001, AC-003, AC-101

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

Traceability: FR-001, AC-002, NFR-002

---

## Child Process Execution Security Storyboard

### Spawn policy

```text
Agent step requests installer execution
   -> build allowed executable + args
   -> sanitize/validate arguments
   -> launch with constrained token/profile
   -> enforce timeout + resource limits
   -> capture stdout/stderr + exit code
   -> map to normalized reason/status
```

### Verification gates

- Disallowed command/arg patterns are blocked.
- Timeout/resource limit violations are observable.
- Exit reason mapping is deterministic and auditable.

Traceability: NFR-002, AC-102

---

## Artifact Trust Validation Storyboard

### Validation sequence

1. Acquire artifact bytes from internal source.
2. Compute digest.
3. Compare with immutable manifest digest.
4. Validate signature/trust evidence.
5. Proceed only on pass.

### Verification gates

- Digest mismatch always blocks execution.
- Invalid signature evidence always blocks execution.
- Trust evidence is recorded in audit/log context.

Traceability: NFR-002, AC-102

---

## Identity Lifecycle Storyboard

### Identity phases

| Phase | Auth mechanism | Allowed purpose |
| --- | --- | --- |
| Enrollment | one-time token | first bind only |
| Steady-state | mTLS per-agent cert identity | all runtime reconnect/ops |

### Lifecycle

```text
Issue enrollment token
   -> first connect with token
   -> token validation + identity bind
   -> cert issuance/binding
   -> token invalidation
   -> steady-state reconnect via mTLS only
```

### Rejection branch

```text
Reconnect attempt
   -> no cert / invalid cert / unbound cert
      -> reject connection
      -> emit auth failure audit
```

### Verification gates

- Token cannot be reused after enrollment.
- mTLS binding is required for reconnect.
- Invalid cert reconnect is rejected.

Traceability: FR-004, AC-005, AC-102

---

## Observability and Audit Storyboard

### Required correlation fields

Each step/event must include:

- `jobId`
- `nodeId`
- `step`
- `reasonCode`
- `sequence`
- `leaseId`

### OTel baseline

Phase 1 default is file-based export with rotation, retention, and redaction controls.

### Redaction/export flow

```text
Runtime events -> Telemetry pipeline -> Redaction policy -> File exporter
                                               |
                                               +-> restricted access policy
```

### Verification gates

- Correlation fields are present for each step event.
- Sensitive fields are redacted by policy.
- Rotation/retention settings are enforced.

Traceability: NFR-003, AC-103, AC-102

---

## API/UI/CLI Runtime Surfaces Storyboard

### Surface split

| Surface | Purpose | Runtime status |
| --- | --- | --- |
| REST API | canonical runtime control surface | Required |
| Embedded UI | operator visibility + actions | Required |
| CLI | automation over API | Required |
| Scripts | provisioning/bootstrap only | Allowed (non-runtime) |

### CLI snapshot

```text
di jobs create --manifest .\node24.json --targets node-001,node-002
di jobs status --job-id job-20260414-001
di jobs cancel --job-id job-20260414-001 --reason "operator_request"
di nodes list
di artifacts upload --file .\nodejs.zip --manifest .\nodejs.manifest.json
```

### Verification gates

- Runtime operations are possible without script orchestration.
- UI timeline reflects live status transitions.
- CLI maps 1:1 to API behavior.

Traceability: NFR-004, AC-104, FR-001, AC-001, AC-002

---

## Orchestrator Self-Update Storyboard (Staged Swap + Supervisor)

### Required pattern

- staged candidate placement
- supervisor/wrapper process handoff
- startup health gate
- rollback to previous binary on failed startup

### Sequence

```text
Admin/API          Current Orchestrator         Artifact Source/API        Supervisor/Wrapper        Candidate Orchestrator        Health Probe
   |                       |                            |                         |                            |                      |
   | self-update request ->| validate auth/rbac         |                         |                            |                      |
   |                       | download candidate ------->|                         |                            |                      |
   |                       |<---------------------------| package bytes            |                            |                      |
   |                       | verify signature + hash    |                         |                            |                      |
   |                       | verify compatibility       |                         |                            |                      |
   |                       | stage candidate ---------->|                         |                            |                      |
   |                       | handoff + graceful stop -->| start candidate ------->| startup                   |                      |
   |                       |                            |                         |--------------------------->| /health pass/fail    |
   |                       |                            |                         | if fail -> restart previous|                      |
```

### State machine

```text
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

### Verification gates

- Candidate signature/hash passes before switch.
- Startup health check must pass for completion.
- Failed startup triggers rollback and linked audit events.

Traceability: NFR-005, AC-105

---

## Persistence Storyboard (SQLite Canonical Entities)

### Canonical entities

- `Job`
- `Node`
- `AssignmentLease`
- `ConfigSnapshot`

### Ordering invariant

Do not dispatch assignment before durable persistence success.

### Verification gates

- Runtime state is not dependent on in-memory-only stores.
- Entity transitions remain queryable for evidence/audit reconstruction.

Traceability: FR-001, FR-006, NFR-001, AC-001, AC-007, AC-101

---

## DevOps and Deployment Boundary Storyboard

### Policy baseline

- Pipeline may build/sign/package/deploy orchestrator.
- Pipeline must not execute workstation runtime install/update/rollback directly.
- Runtime node actions occur only via orchestrator API/CLI.

### Verification gates

- Pipeline definitions show orchestrator-only runtime boundary.
- Clean-host launch validation passes.
- Runtime node actions are auditable from orchestrator surfaces.

Traceability: NFR-004, NFR-005, AC-104, AC-105

---

## End-to-End Multi-Node Storyboard

### Scenario

Install package to two nodes in parallel with bounded orchestration concurrency.

### Timeline

```text
T0  submit job targeting node-001,node-002
T1  assignments dispatched to both nodes
T2  node-001 acquire/validate/install
T2  node-002 acquire/validate/install
T3  node-001 complete
T4  node-002 complete
T5  orchestrator marks job terminal success
```

### Partial failure branch

```text
node-001 success, node-002 failure
   -> job terminal = partial failure/failure (policy-defined)
   -> audit includes per-node terminal reason
```

### Verification gates

- Per-node states are independently visible.
- Aggregated job outcome logic is deterministic and auditable.

Traceability: FR-001, AC-001, AC-002, NFR-003

---

## Fault Injection Storyboard

### Fault set

- Checksum mismatch
- Network interruption during artifact download
- Agent disconnect mid-job
- Retry exhaustion
- Invalid cert reconnect

### Fault handling matrix

| Fault | Expected system behavior | Evidence |
| --- | --- | --- |
| Checksum mismatch | fail closed before execution | trust failure event + terminal reason |
| Network interruption | bounded retry if transient | retry timeline + counts |
| Agent disconnect | lease stale policy execution | AssignedStale transitions |
| Retry exhaustion | terminal fail with reason | final reason + attempts |
| Invalid cert reconnect | reject connection | auth reject audit |

### Verification gates

- Fault outcomes match policy model.
- No silent failures.
- Evidence is reconstructable from logs/events/state.

Traceability: AC-101, AC-102, AC-103

---

## Verification Checklist by Storyboard

### Orchestrator install

- [ ] Self-contained EXE launches on clean host.
- [ ] `/health` is healthy.
- [ ] Embedded UI is reachable.
- [ ] SQLite + artifact path initialized.

### Agent bootstrap

- [ ] One-time token issued and consumed.
- [ ] Service installed/running.
- [ ] mTLS reconnect succeeds.
- [ ] Invalid-cert reconnect fails.

### Artifact ingestion/delivery

- [ ] Upload stores immutable digest-bound record.
- [ ] Agent fetches artifacts only from internal artifact API.
- [ ] Range/chunk retrieval works for large artifacts.
- [ ] Hash/signature mismatch blocks execution.

### Runtime protocol and lease

- [ ] Canonical sequence observed.
- [ ] StepStatus ingest idempotency behavior is correct.
- [ ] Payload conflict is rejected and audited.
- [ ] Stale timeout/reassignment bounds enforced.

### Pipeline execution

- [ ] Full local typed pipeline executes in order.
- [ ] Adapter normalization is consistent.
- [ ] Terminal state and reason are deterministic.

### Update/modify/rollback

- [ ] Snapshot is created before mutate.
- [ ] Restore path executes on qualifying failure.
- [ ] Downgrade/high-risk approval gates are enforced.

### Operator surfaces

- [ ] API/UI/CLI execute runtime actions.
- [ ] Scripts are not required for runtime operations.
- [ ] Step timeline is visible and correlated.

### Packaging/devops

- [ ] Orchestrator-only deployment boundary enforced.
- [ ] No direct workstation deployment from pipeline.

---

## Storyboard-to-AC Matrix

| Storyboard area | Primary AC coverage |
| --- | --- |
| Packaging and clean-host launch | AC-105 |
| Fresh orchestrator install | AC-001, AC-105 |
| Agent bootstrap and identity | AC-005, AC-102 |
| Artifact ingestion and trust | AC-006, AC-102 |
| HTTP artifact delivery | AC-006 |
| Runtime sequence + idempotency | AC-003 |
| Lease/stale policy | AC-101 |
| Local typed pipeline | AC-004, AC-006 |
| Install/update/modify flows | AC-001, AC-002, AC-007 |
| Observability/timeline | AC-103 |
| API/CLI runtime surface | AC-104 |
| Security overlays end-to-end | AC-102 |

---

## Deferred Items (Out of Phase 1)

- Linux agent implementation
- Multi-orchestrator HA/disaster recovery semantics
- Advanced key rotation cadence and deep incident forensics
- Extended observability indexing/retention operations beyond PoC defaults
- Rollout ring automation and broad environment matrix hardening

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
