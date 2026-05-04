# Phase 4 Review: Structured Gap Analysis

## GAPS — MVP Requirements Not Covered by Any Ticket

### G1: Missing List Views for Artifacts and Workloads
The sidebar (P4-001) links to `/artifacts` and `/workloads`, but P4-002 only covers upload (no artifact list), and P4-004 only covers upload (no workload list). The MVP spec Section 15, Items 22–23 imply browsing existing resources before selecting them. Without list views, admins cannot verify what's been uploaded, making run wizard workload selection (P4-007) impossible to validate visually.

**Impact:** High — breaks the core workflow (select workload → run)

### G2: WORKLOAD_ASSIGNED Status Missing from Schema
P1-002 defines `AgentNodeStatus` as `REGISTERED, UNREGISTERED, LOST`. However:
- P2-004 uses `AgentNodeStatus.WORKLOAD_ASSIGNED` in code
- P4-007 filters agents by `WORKLOAD_ASSIGNED` status
- MVP Section 12 includes `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE` in the state machine

The enum needs `WORKLOAD_ASSIGNED` added, and agents must transition to it after successful INSTALL. P1-002 didn't account for this, and no ticket adds it.

**Impact:** High — P4-007 mode validation cannot work without this status

### G3: Agent Detail View Undefined
P4-006 says "Click agent row to see detail (expandable or link to detail view)" but no `GET /api/agents/{agentId}` endpoint or detail page ticket exists. The detail view would need agent packages, run history — neither endpoint is defined.

**Impact:** Medium — collapsed acceptance criterion

### G4: No Artifact/Delete or Artifact Replacement Workflow
The MVP specifies "Artifact duplicate rejection on import" (Section 14), and the upload endpoint rejects duplicates. But there's no UI flow for what happens when an admin needs to update an artifact. No delete, no replace, no version-bumping guidance.

**Impact:** Low — MVP can live without this, but the UX is a dead end on error

### G5: Wizard State Management Undefined
P4-007 says "Selections stored in state for Step 2 and Step 3" but specifies no mechanism. The wizard spans 3 tickets (P4-007, P4-008, P4-009) with no shared state contract — no URL params, no context provider, no state machine defined. Passing `agentId + workloadId + workloadVersion + mode` across route transitions is unspecified.

**Impact:** High — wizard is the core user flow and has no wiring specification

### G6: No Specification for Error/Loading/Empty State Patterns
P4 tickets individually mention specific error toasts, but there's no shared:
- Error boundary component
- Retry strategy for API failures
- Loading skeleton/spinner standard
- Empty state component
- Network error/disconnect handling

**Impact:** Medium — inconsistent UX, duplicated effort

---

## MISSING ENDPOINTS — Backend APIs Needed by P4 but Not Implemented Anywhere

| Endpoint | Needed By | Existing Ticket | Notes |
|---|---|---|---|
| `GET /api/agents` | P4-006, P4-001 dashboard, P4-007 | **None** | P1-006 only creates `POST /api/agents/enroll` and `POST /api/agents/{agentId}/unregister` |
| `GET /api/agents/{agentId}` | P4-006 detail | **None** | Not in P1, P2, or P3 |
| `GET /api/enrollment/tokens` | P4-005 token list | **None** | P1-005 only creates `POST /api/enrollment/tokens` |
| `GET /api/workloads` | P4-007 workload dropdown | **None** | P1-011 only creates `POST /api/workloads` |
| `GET /api/workloads/{id}/versions` | P4-007 version selector | **None** | Not in any ticket |
| `GET /api/runs` | P4-010 runs list, P4-001 dashboard | **None** | P2-005 creates `POST /api/runs` and `GET /api/runs/{runId}` but not a list endpoint |
| `GET /api/runs/{runId}/steps` | P4-010 step table | **None** | Only `POST /api/runs/{runId}/steps` exists (Agent reporting) |
| `GET /api/artifacts` | P4-002 list (missing view) | **None** | P1-009 only creates `POST /api/artifacts` |
| `POST /api/runs/{runId}/confirm` | P4-009 UPDATE confirmation | **None** | P3-002 mentions it in description but has no explicit task/acceptance criteria for it |

**Critical:** P4-010 and P4-006 define backend endpoint tasks (`Add GET /api/runs/{runId} endpoint...`) inside frontend tickets. This is an anti-pattern — backend endpoints should be in their own tickets with proper API contracts, tests, and acceptance criteria.

---

## UI/UX ISSUES

### U1: P4-008 Auto-Dispatch PRE_CHECK Creates Race Condition
Entering "Step 2" auto-dispatches a `PRE_CHECK` run via `POST /api/runs`. If the user navigates back and changes agent/workload in Step 1, the previous PRE_CHECK run is orphaned with no cancellation mechanism. The "one active run per agent" constraint (P2-003) makes re-dispatching impossible until the orphaned run completes or times out.

**Recommendation:** Add run cancellation (`DELETE /api/runs/{runId}` or `POST /api/runs/{runId}/cancel`) or redesign Step 2 to dispatch only on explicit "Run Pre-Check" button rather than on mount.

### U2: P4-009 Confirmation Gate Lacks Backend Endpoint
The UPDATE mode confirmation requires `POST /api/runs/{runId}/confirm`. P3-002 describes this gate in prose but the tasks section does not explicitly implement the confirm endpoint. Without it, the 3-step wizard cannot transition UPDATE runs from PENDING to RUNNING.

### U3: P4-008 Polling Interval Unspecified
The ticket says to poll `GET /api/runs/{runId}` but doesn't specify the interval. The agent polls every 30s (default), so the UI should poll at a comparable rate (every 5s as P4-010 uses, or faster). This needs explicit alignment with the agent polling interval for UX consistency.

### U4: P4-001 Dashboard Requires Unimplemented Aggregation Queries
Agent counts (REGISTERED, LOST, UNREGISTERED) need `GET /api/agents` (missing). Recent runs need `GET /api/runs` with ordering and limit (missing). The dashboard will be entirely empty or broken on load.

### U5: P4-002 No Artifact List — Upload Page Is a Dead End
After uploading, the success state says "offer to upload another or navigate to artifacts list" — but `/artifacts` route has no list view. This should navigate to an artifact list or the upload page should include a recent artifacts section.

### U6: P4-007 Mode Validation Logic Incomplete
The ticket says UPDATE/UNINSTALL are "only available for agents with `WORKLOAD_ASSIGNED` status" but `WORKLOAD_ASSIGNED` isn't in the DB schema (see G2). Also, the MVP spec doesn't define what agent statuses are filterable — UNREGISTERED agents shouldn't appear in the dropdown at all, and LOST agents need a warning.

---

## READINESS ASSESSMENT

**Phase 4 is NOT ready for implementation.** There are 9 missing backend endpoints required before any P4 page can function. Additionally, the wizard state management is undefined, and a schema gap (`WORKLOAD_ASSIGNED` status) will block mode validation.

### Blockers (must resolve before starting P4):

1. **Create a Phase 3.5 or append to Phase 3:** Backend read endpoints ticket covering all 9 missing APIs listed above
2. **Add `WORKLOAD_ASSIGNED` to `AgentNodeStatus` enum** — schema migration in P1-002 needs updating, and P1-003/P2-004 need to handle the transition
3. **Implement `POST /api/runs/{runId}/confirm` endpoint** — required by UPDATE mode confirmation gate (P3-002 references it but doesn't have an explicit task)
4. **Define wizard state management pattern** — URL params, context provider, or state machine for P4-007→008→009

### Recommended sequencing:

```
1. Fix schema (WORKLOAD_ASSIGNED)           → P1-002 amendment
2. Add all missing read endpoints            → new P3.5 ticket
3. Add confirm endpoint                     → P3-002 amendment  
4. Add run cancellation endpoint            → new ticket (U1 fix)
5. Then begin P4-001 (shell)               → depends on (2) partially
6. Then P4-002 through P4-006              → depends on (2) fully
7. Then P4-007 through P4-010              → depends on (2)(3)(4)
```