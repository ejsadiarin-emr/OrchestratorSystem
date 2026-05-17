# Core Contracts Pack

Date: 2026-04-11  
Status: Draft (prefilled from locked decisions)

## Purpose

Single canonical reference for implementation contracts before full OpenAPI/schema completion.

---

## 1) API endpoint inventory (pre-OpenAPI)

| Endpoint ID | Method | Path | Purpose | Request shape | Response shape | Auth |
|---|---|---|---|---|---|---|
| API-001 | POST | `/api/jobs` | Submit install/upgrade/rollback job request | `CreateJobRequest` (targets, manifest, mode, options) | `CreateJobResponse` (jobId, state) | Admin/Operator |
| API-002 | GET | `/api/jobs/{jobId}` | Fetch job summary and state | n/a | `JobDetailResponse` (state, timeline, reason codes) | Admin/Operator/ReadOnly |
| API-003 | GET | `/api/jobs/{jobId}/steps` | Fetch step-level status and telemetry references | n/a | `JobStepListResponse` | Admin/Operator/ReadOnly |
| API-004 | GET | `/api/nodes` | List registered nodes and health | n/a | `NodeListResponse` | Admin/Operator/ReadOnly |
| API-005 | POST | `/api/jobs/{jobId}/cancel` | Cancel queued/assigned/in-progress job safely | `CancelJobRequest` | `CancelJobResponse` | Admin/Operator |

---

## 2) Canonical data model/entity list

| Entity | Key fields | Purpose | Persistence |
|---|---|---|---|
| Job | `jobId`, `state`, `mode`, `nodeIds`, `reasonCode`, `createdAtUtc`, `updatedAtUtc` | Canonical orchestration state for each deployment request | SQL table |
| Node | `nodeId`, `agentId`, `hostname`, `agentVersion`, `lastSeenUtc`, `status` | Registered node/agent identity and health (`agentId` is runtime identity; `nodeId` is inventory target key) | SQL table |
| AssignmentLease | `assignmentId`, `leaseId`, `jobId`, `agentId`, `ttlSeconds`, `lastHeartbeatUtc`, `lastAckedSequence` | Runtime ownership, stale detection, and reconnect cursor | SQL table |
| ConfigSnapshot | `configSnapshotId`, `jobId`, `nodeId`, `packageId`, `sourceSchemaVersion`, `capturedAtUtc`, `storageLocation`, `integrityHash` | Upgrade rollback source of truth | SQL table |

---

## 3) Manifest schema draft

### Required fields

- `packageId`
- `targetVersion`
- `targets`
- `executionMode`
- `artifact`
- `artifact.integrity.algorithm`
- `artifact.integrity.hash`
- `steps`
- `rollbackPolicy`
- `idempotencyKey`

### JSON draft

```json
{
  "packageId": "deltav-adjacent-component",
  "targetVersion": "1.2.3",
  "executionMode": "install",
  "idempotencyKey": "job-2026-04-11-001",
  "targets": ["node-a", "node-b"],
  "artifact": {
    "source": "\\\\repo\\packages\\deltav-adjacent-component-1.2.3.zip",
    "integrity": {
      "algorithm": "sha256",
      "hash": "8f1a2f3f9a53a4b50f7fcbda6a42e1e4b8028ac3d73b4b5f6a1de8d3c9f6b1e0"
    },
    "signature": "authenticode:CN=Emerson Distributed Installer Signing"
  },
  "rollbackPolicy": { "mode": "compensate", "restoreConfigSnapshot": true },
  "steps": [
    { "stepId": "001-precondition", "name": "PreConditionCheck", "params": {} }
  ]
}
```

---

## 4) Interface contracts

### 4.1 `IInstallStep`

```csharp
public interface IInstallStep
{
    string Name { get; }
    Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct);
}
```

### 4.2 `IPreCheck`

```csharp
public interface IPreCheck
{
    string Name { get; }
    ConfidenceLevel Confidence { get; }
    Task<PreCheckResult> ExecuteAsync(JobContext context, CancellationToken ct);
}
```

---

## 5) Agent/orchestrator message contracts

### Canonical runtime sequence

`Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

### Shared message envelope schema

All runtime hub messages use this envelope; message-specific payload fields are in `payload`.

```json
{
  "messageType": "AssignJob|AckClaim|LeaseHeartbeat|StepStatus|Complete|Fail|LeaseClose",
  "protocolVersion": "1.0",
  "messageId": "uuid",
  "timestampUtc": "2026-04-11T12:34:56Z",
  "assignmentId": "string",
  "leaseId": "string",
  "jobId": "string",
  "agentId": "string",
  "sequence": 42,
  "payload": {}
}
```

Envelope rules:

- `messageType`, `protocolVersion`, `messageId`, `timestampUtc`, `sequence` are always required.
- `assignmentId`, `leaseId`, `jobId`, `agentId` are required when relevant to that message type.
- `payload` contains message-specific data (for example `stepId`, `status`, `result`, `reasonCode`).

### Message keys

| Message | Required keys |
|---|---|
| AssignJob | Shared envelope + payload: `manifest`, `executionMode`, `policy` |
| AckClaim | Shared envelope + payload: `claimState` |
| StepStatus | Shared envelope + payload: `stepId`, `status`, `progress?`, `detail?`, `reasonCode?` |
| LeaseHeartbeat | Shared envelope + payload: `healthStatus?` |
| Complete/Fail | Shared envelope + payload: `result`, `reasonCode`, `summary?` |

### Idempotency rule

Status updates are idempotent upserts keyed by `(jobId, stepId, sequence)`.

If the same idempotency key is received with a different payload hash, reject the update, emit audit event `sequence_payload_conflict`, and keep prior accepted record.

Stale/out-of-order updates are rejected; reconnect requires resume from `lastAcknowledgedSequence + 1`.

`AssignedStale` handling uses PoC defaults from requirements contract:

- Lease TTL: `90s`
- Heartbeat interval: `15s`
- Stale threshold: `3` missed heartbeats
- Auto-fail bound: `lease_timeout_exhausted` after 2 reassignment attempts or 15 minutes stale duration

---

## 6) Open items

- Final request/response schemas for API endpoint inventory to be formalized in OpenAPI artifact.

---

## 7) Packaging and runtime contract note

- Orchestrator delivery target is a self-contained executable that includes API host and embedded React UI assets.
- This aligns with `learning-plan.md` (self-contained, zero-prerequisite deployment constraint) and `ADR-005`.
