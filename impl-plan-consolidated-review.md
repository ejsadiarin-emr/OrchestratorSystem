# MVP Plan Review: Implementation Readiness Assessment

## Verdict: **NOT READY** — 15 blocking issues across all 4 phases

The plans are architecturally sound and well-structured, but have cross-cutting gaps that will cause compilation errors, broken flows, and dead ends if implemented as-is. Below is the consolidated analysis.

---

## CRITICAL (Blocks All Phases)

### 1. `AgentNodeStatus` Enum Missing Values
**Phase**: P1-002 | **Affects**: P2-004, P3-002, P4-007
- P1-002 defines: `REGISTERED, UNREGISTERED, LOST`
- MVP Section 12 requires: `WORKLOAD_ASSIGNED`, `NEEDS_UPDATE`
- P2-004, P3-002, P4-007 all reference `WORKLOAD_ASSIGNED`
- **Fix**: Add `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE` to the enum in P1-002

### 2. `WorkloadPackage` Missing `workloadVersion` Column
**Phase**: P1-002 schema | **Affects**: P2-003, P2-005, P3-002
- `WorkloadPackage` links to `Workloads` via `workloadId` but has no `workloadVersion` column
- When a workload has multiple versions (v1, v2), their packages are indistinguishable
- Queries in P2-003 and P2-005 filter only by `workloadId`, returning packages from ALL versions
- **Fix**: Add `workloadVersion` column, make composite key `(workloadId, workloadVersion, packageId)`

### 3. `pollingIntervalSeconds` Not Stored on `AgentNode`
**Phase**: P1-002 schema, P2-004 | **Affects**: P2-004, LOST detection
- P1-006's `EnrollResponse` returns `pollingIntervalSeconds` but never persists it
- P2-004's LOST detection uses a global default instead of per-agent values
- **Fix**: Add `PollingIntervalSeconds` column to `AgentNodes`, store at enrollment, use in LOST threshold calculation

### 4. Missing Backend Read Endpoints (9 endpoints)
**Phase**: Gap across P1-P3 | **Affects**: P4 entirely
| Endpoint | Consumer |
|---|---|
| `GET /api/agents` | P4-006, P4-001, P4-007 |
| `GET /api/agents/{id}` | P4-006 detail |
| `GET /api/enrollment/tokens` | P4-005 |
| `GET /api/workloads` | P4-007 dropdown |
| `GET /api/workloads/{id}/versions` | P4-007 version selector |
| `GET /api/runs` | P4-010, P4-001 |
| `GET /api/runs/{id}/steps` | P4-010 step table |
| `GET /api/artifacts` | P4-002 list view |
| `POST /api/runs/{id}/confirm` | P4-009, P3-002 |

- **Fix**: New ticket(s) for all read endpoints + confirm endpoint

### 5. `AWAITING_CONFIRMATION` Missing from `WorkloadRunStatus`
**Phase**: P3-002 | **Affects**: Admin confirmation gate
- UPDATE runs need admin confirmation before execution
- P2-003's `GetNextTaskAsync` picks up `PENDING` runs — unconfirmed UPDATE runs would execute immediately
- **Fix**: Add `AWAITING_CONFIRMATION` to enum, filter from polling queries

---

## HIGH (Blocks Specific Flows)

### 6. No Artifact Download Endpoints
- P2-003 returns artifact URLs but no ticket implements `GET /api/artifacts/{id}/download` or `/manifest`
- **Fix**: Add endpoints with `[AgentAuth]`

### 7. No Step Reporting / Run Completion Endpoints
- P2-006/P3-001 Agent code sends step reports and run completion, but no Orchestrator controller exists
- **Fix**: Implement `POST /api/runs/{runId}/steps` and `POST /api/runs/{runId}/complete`

### 8. `TaskPackage` Lacks Delta Context for UPDATE Mode
- `NextTaskResponse` has no `deltaStatus` (MISSING/VERSION_DRIFT/MATCHES/ORPHANED) or `phase` (1 or 2)
- Agent can't distinguish install vs update vs skip vs uninstall packages
- **Fix**: Extend `TaskPackage` with `deltaStatus`, `phase`, and ensure `updateStrategy` is populated

### 9. EF Core FK Configurations Missing in P1-002
- Entity configurations don't define foreign keys, cascade behaviors, or indexes
- `AgentSecret` column has no index (linear scan on every authenticated request)
- **Fix**: Add `OnModelCreating` FK configurations and `AgentSecret` index

### 10. Auth Middleware Not Implemented
- P1-003 describes auth but no ticket implements the `[AgentAuth]` attribute or middleware
- **Fix**: Add middleware ticket to P1 (referenced by P2-002)

---

## MEDIUM (Correctness/UX Issues)

| # | Issue | Fix |
|---|---|---|
| 11 | WorkloadPackage queries missing version filter (P2-003, P2-005) | Add `workloadVersion` filter after schema fix |
| 12 | N+1 query in P2-003 `GetNextTaskAsync` | Batch-load artifacts |
| 13 | Null reference on missing artifact (P2-003) | Add null check |
| 14 | `stepOrder` missing from `WorkloadRunStep` schema | Add column |
| 15 | `ReportStepAsync` query ambiguity for multi-command steps | Add `status == PENDING` filter |
| 16 | Wizard state management undefined (P4-007→008→009) | Define state contract |
| 17 | Auto-dispatch PRE_CHECK race condition (P4-008) | Add run cancellation or make dispatch explicit |
| 18 | LOST→WORKLOAD_ASSIGNED recovery contradicts MVP state machine | Update MVP Section 12 |
| 19 | `detectedPackages` not wired to reconciliation (P3-003) | Wire complete endpoint to reconciliation |
| 20 | UPDATE `reinstall` strategy undefined on Agent side | Specify UNINSTALL+INSTALL sequence |

---

## Recommended Pre-Implementation Order

```
1. P1-002 AMENDMENT: Add WORKLOAD_ASSIGNED, NEEDS_UPDATE to enum;
   Add pollingIntervalSeconds to AgentNodes;
   Add workloadVersion to WorkloadPackage;
   Add FK configs, indexes, stepOrder to WorkloadRunStep;
   Add AWAITING_CONFIRMATION to WorkloadRunStatus

2. P1 NEW TICKET: Auth middleware ([AgentAuth] attribute + handler)

3. P2 AMENDMENT: Add artifact download endpoints (P2-003 extension)
                 Add step reporting + run completion endpoints
                 Fix WorkloadPackage queries with version filter
                 Fix N+1 and null reference bugs

4. P3 AMENDMENT: Add POST /api/runs/{runId}/confirm endpoint
                 Extend TaskPackage with deltaStatus + phase
                 Define Phase 1→Phase 2 signaling for UPDATE mode

5. P3.5 NEW TICKET: All 9 missing read endpoints (GET /api/agents, etc.)
                    Add GET /api/runs/{id}/steps (admin)
                    Add run cancellation endpoint

6. P4 AMENDMENT: Define wizard state management pattern
                 Fix auto-dispatch race condition
                 Add artifact/workload list views
```

**Bottom line**: The plans need schema fixes, missing enum values, and ~13 new endpoints before any phase can be implemented correctly. I'd estimate 2-3 days of plan amendments before code can start.