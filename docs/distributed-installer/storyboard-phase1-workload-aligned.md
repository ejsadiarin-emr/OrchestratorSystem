# Installation and Operational Storyboards (PoC Phase 1, Workload-Aligned)

Date: 2026-04-17
Status: Candidate storyboard aligned to workload-first PRD (for side-by-side review)
Scope: Windows-first, single-orchestrator distributed installer runtime

---

## Purpose

This document is a workload-first storyboard variant for comparison against `storyboard-phase1.md`.

It aligns terminology, endpoint usage, and runtime sequence with:

1. `poc-phase1-prd-final.md`
2. `poc-phase1-prd-and-implementation-tracker.md`

---

## Source-of-Truth Precedence

When documents conflict, use this order:

1. `docs/distributed-installer/poc-phase1-prd-final.md`
2. `docs/distributed-installer/storyboard-phase1-workload-aligned.md` (this document)
3. `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
4. Historical storyboard drafts for context only

---

## Inputs and Hard Constraints

### PoC Phase 1 constraints

- Windows-first only
- Single orchestrator only (no HA/multi-orchestrator commitments)
- Runtime artifact source is internal-only (orchestrator artifact store)
- SignalR is control/status channel only
- Artifact payload transfer is HTTP endpoint based (`GET/HEAD + Range`)
- Orchestrator distribution is self-contained single executable with embedded UI
- Runtime actions are API/UI/CLI driven
- Scripts are provisioning/bootstrap only
- Workload revisions are immutable once published
- Workload update does not remove packages in Phase 1

### Canonical runtime protocol sequence

`Connect -> Register/Authenticate -> AssignRun -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

### Canonical lifecycle modes

- `install`
- `update`
- `rollback`
- `cancel`

### API deprecation constraint

- `/api/jobs` mutation endpoints are deprecated immediately and return `410 Gone`
- All new runtime lifecycle flows use `/api/workload-runs`

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
  +--------------------------------------------------------------------------------------+
  |                                 Orchestrator Node                                    |
  |                                                                                      |
  | +--------------------+   +----------------------+   +-----------------------------+  |
  | | REST API           |   | SignalR Runtime Hub  |   | Run Planner / Policy Engine |  |
  | | workloads/runs/... |   | AssignRun/Ack/...    |   | sequencing/idempotency      |  |
  | +---------+----------+   +----------+-----------+   +--------------+--------------+  |
  |           |                         |                               |                 |
  |           +-------------------------+-------------------------------+                 |
  |                                      |                                               |
  |                     +----------------v----------------+                              |
  |                     | Runtime Protocol + Lease Policy |                              |
  |                     +----------------+----------------+                              |
  |                                      |                                               |
  |              +-----------------------+----------------------+                        |
  |              |                                              |                        |
  |     +--------v---------+                          +---------v--------------------+    |
  |     | SQLite           |                          | Artifact Store (local FS)    |    |
  |     | Workload/Run/    |                          | immutable digest records      |    |
  |     | Lease/Snapshot   |                          +------------------------------+    |
  |     +------------------+                                                          |
  +-------------------------------+------------------------------------+------------------+
                                  |                                    |
                       SignalR control/status                   HTTPS artifact
                       (no payload bytes)                       GET/HEAD + Range
                                  |                                    |
                     +------------v---------------+          +---------v---------+
                     | Agent Win Service          |<---------| Artifact Endpoint |
                     | (SignalR + mTLS)           |          +-------------------+
                     |                            |
                     | RunChannelService          |
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

| From | To | Primary risk | Required controls |
|---|---|---|---|
| Admin | Orchestrator API | privilege abuse/spoof | RBAC, authN/authZ, audit |
| Agent | SignalR Hub (Orch) | replay/spoofing | enrollment -> mTLS, sequence checks |
| Orch API | Artifact store | tamper/substitution | immutable digest metadata, ACL |
| Agent | Artifact API | MITM/tamper | TLS, hash+signature validation |
| Orch | SQLite | state integrity | app-level validation + host ACL |
| Agent service | Child process | escalation/unsafe args | constrained spawn policy |

---

## Core Storyboard Map

| Storyboard | Purpose |
|---|---|
| Media packaging | Build/sign/publish orchestrator package |
| Fresh orchestrator install | Bring up API/UI/Hub/persistence deterministically |
| Agent install via WinRM | Enroll node and bind identity (`token -> mTLS`) |
| Artifact ingestion | Ingest -> validate -> store immutable artifact record |
| Workload lifecycle run | Submit run -> assign -> execute -> observe |
| Workload update/rollback/cancel | Deterministic transition behavior and auditability |

---

## Media Packaging Storyboard

### Packaging posture

| Option | What it gives | Phase 1 decision |
|---|---|---|
| Self-contained EXE | clean-host startup, simple operator path | Selected (primary) |
| ZIP bundle | easy transfer and scripted unpack | Supported |
| ISO media | offline distribution pattern | Deferred |

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

- Signer/hash verification succeeds
- Package launches on clean host without .NET/IIS preinstall

---

## Fresh Orchestrator Install Storyboard

### Step-by-step flow

1. Admin stages `Orchestrator.exe` (or extracts ZIP)
2. Admin runs initialization (interactive or scripted config)
3. Config captures:
   - listen URL/port (default `:5000`)
   - initial admin credentials
   - SQLite database path
   - artifact storage path (local UNC or folder)
   - OTel exporter endpoint/export mode
4. Orchestrator starts API, Hub, persistence, embedded UI host
5. Startup checks run; bootstrap audit event emitted

### Verification gates

- `GET /health` returns healthy
- Embedded UI loads from orchestrator host
- `GET /api/nodes` returns valid schema
- SQLite file/schema initialize
- Artifact path writable and access-controlled

---

## Artifact Ingestion Storyboard (`POST /api/artifacts`)

### Upload/Ingestion flow

1. Admin uploads installer media (or requests vendor import)
2. Orchestrator runs template/analyzer enrichment pipeline
3. Admin confirms minimal required fields and any unresolved conditional fields
4. Orchestrator validates trust evidence and schema
5. Orchestrator stores immutable artifact record and emits ingest audit event

### Request shape

- Endpoint: `POST /api/artifacts`
- Content type: `multipart/form-data`
- Required parts:
  - `file`
  - `manifest`
- Optional part:
  - `detachedSignature`

### Minimal required admin fields

- `manifest.packageId`
- `manifest.version`
- `manifest.channel` (`stable|canary|test`)
- `manifest.artifactType` (unless inferable)

### Default resolution chain (deterministic)

`admin -> template -> analyzer -> default`

### Security and policy outcomes

- verification `fail`: ingest rejected (fail-closed)
- verification `warn`: ingest accepted only with elevated policy defaults:
  - `riskLevel=high`
  - `approvalRequired=true`

### Verification gates

- Minimal required fields enforced
- Conditional requirements enforced when unresolved fields remain
- Resolved manifest includes field source provenance
- Digest/signature trust evidence persisted and auditable

---

## Agent Installation Storyboard (Token -> mTLS)

### Main flow

1. Admin requests short-lived enrollment token from orchestrator
2. Admin runs bootstrap script with orchestrator URL + token
3. Script installs agent executable/service config and starts service
4. Agent collects node metadata on first startup
5. Agent connects with token + metadata
6. Orchestrator validates token, binds identity, invalidates token
7. Certificate material issued for steady-state mTLS
8. Agent reconnects with bound certificate and begins heartbeat

### Verification gates

- Windows service exists/running
- Node appears online
- Lease heartbeat observed
- Token cannot be reused
- Invalid/unbound cert reconnect rejected
- Cleanup branch leaves no partial state

---

## Workload Run Submission and Assignment Storyboard

### Operator intent flow

1. Sysadmin selects targets
2. Sysadmin selects workload revision and operation (`install|update|rollback|cancel`)
3. UI/CLI submits `POST /api/workload-runs`
4. API validates auth, schema, lifecycle constraints, and policy gates
5. Orchestrator persists workload run + node assignments
6. Orchestrator enqueues/dispatches assignment
7. Hub sends `AssignRun`, agent returns `AckClaim`, lease tracking starts
8. UI/CLI opens timeline view: `GET /api/workload-runs/{runId}`, `GET /api/workload-runs/{runId}/steps`

### End-to-end sequence diagram

```text
System Admin   UI/CLI      Orchestrator API     SQLite DB      Dispatch       SignalR Hub      Agent Service    Run Worker       Child Proc      Artifact API
    |           |                |                  |              |               |                |               |               |               |
    | submit run|                |                  |              |               |                |               |               |               |
    |---------->| POST /api/workload-runs          |              |               |                |               |               |               |
    |           |--------------->| validate auth/schema/policy    |               |                |               |               |               |
    |           |                | persist run ---->|              |               |                |               |               |               |
    |           |                | persist assignments ->|         |               |                |               |               |               |
    |           |                | enqueue dispatch ---->|         |               |                |               |               |               |
    |           |<---------------| 202 Accepted + runId |         |               |                |               |               |               |
    | open view | GET /api/workload-runs/{id}, /steps  |         |               |                |               |               |               |
    |<----------|<---------------| read timeline ----->|         |               |                |               |               |               |
    |           |                |                  |             | dequeue        |                |               |               |               |
    |           |                |                  |             |--------------->| AssignRun      |               |               |               |
    |           |                |                  |             |                |--------------->| AckClaim      |               |               |
    |           |                | update lease row ->|          |                |<---------------|               |               |               |
    |           |                |                  |             |                |<---------------| LeaseHeartbeat|               |               |
    |           |                |                  |             |                |                | run pipeline  |               |               |
    |           |                |                  |             |                |                |-------------->| spawn         |               |
    |           |                |                  |             |                |                |               | GET/HEAD+Range ------------->|
    |           |                |                  |             |                |<---------------| StepStatus*   |               |               |
    |           |                | upsert step row ->|           |                |                |               |               |               |
    |           |                |                  |             |                |<---------------| Complete/Fail |               |               |
    |           |                | set terminal row ->|          |                |                |               |               |               |
    |           |                |                  |             |                |<---------------| LeaseClose    |               |               |
```

### Deprecated endpoint behavior

Any attempted mutation on `/api/jobs` must return:

- `410 Gone`
- payload:
  - `code = "deprecated_endpoint"`
  - `message = "Use /api/workload-runs"`
  - `replacementPath = "/api/workload-runs"`

---

## Workload Install Execution Storyboard (End-to-End)

### Flow

1. Submit workload run in `install` mode
2. Validate/persist and assign
3. Agent executes full local package-step pipeline in revision order
4. Orchestrator ingests step statuses and updates timeline
5. Run reaches deterministic terminal state

### Verification gates

- Ordered step timeline visible with package index
- Terminal state reflects final step outcome
- Reason codes and audit links available

---

## Workload Update Storyboard (Revision-to-Revision)

### Flow

1. Operator submits `update` run from current node state to target workload revision
2. Planner computes changed-package set
3. Agent executes changed packages in canonical revision order
4. Post-install verification confirms target package states
5. Node workload revision promoted only after full required success

### Phase 1 constraints

- No automatic package removal during update
- Fail fast at package boundary with explicit reason

### Verification gates

- Changed-package plan deterministic and auditable
- No unchanged package execution unless required by policy
- Node revision promotion occurs only after complete success

---

## Rollback and Cancel Storyboard

### Cancel semantics

1. Orchestrator marks cancellation intent on active run
2. Agent receives cancellation at safe interruption boundary
3. Agent attempts graceful stop, then force termination on timeout
4. Run reaches cancelled/failed terminal state with explicit reason

### Rollback semantics

- If rollback path exists: execute snapshot/restore path
- If rollback path missing: terminal failure with explicit reason

### Verification gates

- Cancel transition auditable with reason code
- Child process termination policy followed
- Rollback attempt/outcome linked in audit chain

---

## Retry, Idempotency, and Replay Storyboard

### Policy principles

- Retries are bounded and transient-focused
- High-risk/non-idempotent actions are not blindly retried
- Idempotency prevents duplicate side effects on replay

### Status ingest rules

- upsert key: `(runId, nodeId, packageId, stepId, sequence)`
- stale/out-of-order update rejected
- same-key payload mismatch rejected and audited as `sequence_payload_conflict`
- reconnect resumes from `lastAcknowledgedSequence + 1`

### Verification gates

- Retry count/interval bounds enforced
- Replay with equal payload is safe
- Replay with mismatched payload rejected + audited

---

## Observability Storyboard

### Phase 1 operator-visible stack

- OTel Collector
- Loki
- Grafana

### Required query dimensions

- `workloadId`
- `workloadRevision`
- `runId`
- `nodeId`
- `packageId`
- `stepId`
- `sequence`
- `reasonCode`

### Verification gates

- Operator can query active/failed runs by `workloadId` or `runId`
- Package-step timeline reconstructable from logs/events
- File export path remains fallback only

---

## Notes for Side-by-Side Review

This aligned storyboard intentionally differs from `storyboard-phase1.md` in these areas:

- uses `AssignRun` instead of `AssignJob`
- uses `/api/workload-runs` lifecycle APIs as primary surface
- treats `/api/jobs` mutation as deprecated `410` path only
- uses `runId/workloadId/workloadRevision` correlation identifiers
- aligns lifecycle vocabulary to `install|update|rollback|cancel`
