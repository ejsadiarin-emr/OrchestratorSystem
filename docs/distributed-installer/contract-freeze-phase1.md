# PoC Phase 1 Contract Freeze (Legacy Jobs -> Workload Runs)

Date: 2026-04-17
Status: Frozen for Phase 1 planning and implementation
Owner: Product + Architecture + Backend

## Purpose

Freeze canonical naming, endpoint, and runtime contract terms before W1 implementation.

This document removes ambiguity between legacy `job` terminology and workload-first Phase 1 contracts.

## Authority and Scope

- Applies to Phase 1 only.
- Canonical policy remains `docs/distributed-installer/poc-phase1-prd-final.md`.
- This document defines implementation-facing mapping and migration rules.

## Canonical Runtime Sequence

`Connect -> Register/Authenticate -> AssignRun -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

Canonical identifier set for runtime and persistence correlation:

- `runId`
- `workloadId`
- `workloadRevision`
- `nodeId`
- `packageId`
- `stepId`
- `sequence`

## Canonical API Surface (Phase 1)

Active lifecycle endpoints:

- `POST /api/workload-runs`
- `GET /api/workload-runs/{runId}`
- `GET /api/workload-runs/{runId}/steps`
- `POST /api/workload-runs/{runId}/cancel`

Deprecated mutation endpoints:

- `POST /api/jobs`
- `POST /api/jobs/{jobId}/cancel`

Required deprecation response for deprecated mutation endpoints:

- HTTP `410 Gone`
- payload:
  - `code = "deprecated_endpoint"`
  - `message = "Use /api/workload-runs"`
  - `replacementPath = "/api/workload-runs"`

## Legacy-to-Canonical Mapping

| Legacy term/field | Canonical term/field | Rule |
|---|---|---|
| `Job` | `WorkloadRun` | Use canonical in all new docs/code/tests |
| `jobId` | `runId` | Replace in contracts, DTOs, telemetry, UI labels |
| `AssignJob` | `AssignRun` | Replace in runtime message types and diagrams |
| `/api/jobs` create | `/api/workload-runs` create | Legacy endpoint returns `410` |
| `/api/jobs/{jobId}/cancel` | `/api/workload-runs/{runId}/cancel` | Legacy endpoint returns `410` |
| Job steps timeline | Workload run steps timeline | Keep package-step detail semantics |
| `modify` mode | `update` or explicit lifecycle mode | Do not introduce new lifecycle verbs in Phase 1 |

## Naming Rules for New Work

1. New code must use workload-first nouns (`WorkloadDefinition`, `WorkloadRevision`, `WorkloadRun`, `NodeWorkloadState`).
2. New runtime contracts must use `AssignRun` and `runId`.
3. New API examples/docs must use `/api/workload-runs*` for lifecycle mutations.
4. Legacy `job` terms may appear only in:
   - deprecation handlers,
   - migration bridges,
   - historical references.

## Test and Evidence Guardrails

Before starting W1 implementation batches, verify documentation contract consistency:

```bash
rg "AssignJob|jobId|/api/jobs" docs/distributed-installer -n
rg "AssignRun|runId|/api/workload-runs" docs/distributed-installer -n
```

Expected intent:

- `AssignJob|jobId|/api/jobs` appears only in historical/deprecation context.
- Active storyboard/PRD/tracker examples use canonical workload-run terms.

## Non-Goals

- This document does not define database schema details.
- This document does not define implementation sequence internals.
- This document does not override PRD FR/NFR/AC policy.
