# Run Creator Wizard — Implementation Plan

## Decisions Record

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Mode selection | Install + Uninstall only; Update is conditional within Install | Update is just Install with a delta — no separate flow needed |
| Pre-check gating | Client-side blocking gate (Option A) | Backend drift check is safety net; wizard enforces UX gate |
| Force override | Available to skip pre-check gate | Power users can bypass with explicit acknowledgment |
| Status model | `Current` / `Drifted` / (no record = Absent) | Simple; Drifted covers all non-matching states; reason computed at wizard time |
| Downgrade | Blocked | No version ordering for rollback in MVP |
| Revision→Version rename | Deferred | Mechanical but large; separate task |
| Offline nodes | Excluded from pre-checks, disabled in UI | Pre-checks require agent HTTP access |
| Multi-node | Per-node pre-check gating with individual pass/fail | Required for wizard flow |
| Idempotency surfacing | Show warning in wizard if duplicate active run exists | MVP: surface the idempotency conflict |
| DB reconciliation | Extend existing logic to create/update NodeWorkloadState for partial states | Current code only deletes or updates; needs upsert for missing states |

---

## Phase 1: NodeWorkloadState Status Enum + DB Reconciliation

### 1.1 Add Status Enum to NodeWorkloadStateEntity

**File:** `apps/orchestrator/backend/Data/Entities/NodeWorkloadStateEntity.cs`

Add:
```csharp
public string Status { get; set; } = "Unknown"; // "Current", "Drifted", "Unknown"
```

Status values:
- `Current` — All packages match the assigned workload version
- `Drifted` — Packages don't match the assigned version (older, partial, mixed)
- `Unknown` — Pre-checks not yet run, or pre-check failed

No `Absent` value — absence of a `NodeWorkloadState` record means "nothing installed."

**Migration:** Add nullable string column `Status` with default `"Unknown"`. Existing rows get `"Unknown"`. EF Core configuration adds a check constraint for the three valid values.

**EF Core config** in `DeploymentPocDbContext.OnModelCreating`:
```csharp
entity.Property(e => e.Status).HasMaxLength(32).HasDefaultValue("Unknown");
entity.HasCheckConstraint("CK_NodeWorkloadState_Status", "[Status] IN ('Current','Drifted','Unknown')");
```

**Shared contract** — add to `shared/contracts/Runtime/` (or `Models/`):
```csharp
public sealed class WorkloadAssignmentStatus
{
    public const string Current = "Current";
    public const string Drifted = "Drifted";
    public const string Unknown = "Unknown";
}
```

### 1.2 Extend Reconciliation Logic

**File:** `apps/orchestrator/backend/Controllers/NodesController.cs` — `ReconcileProbeResults` method (line ~502)

Current gaps:
- If no `NodeWorkloadState` exists and agent reports some packages present → does nothing
- Never sets `Status` field (doesn't exist yet)

**New logic:**

| Scenario | Current | New |
|----------|---------|-----|
| No DB state, agent has packages | No action | **Create** `NodeWorkloadState` with `Status = "Drifted"`, `CurrentRevisionId = null`, `PackageStatesJson` from probe results |
| No DB state, agent has no packages | No action | No action (correct — no record needed) |
| DB state, agent has all packages matching | Update `PackageStatesJson` | Update `PackageStatesJson`, set `Status = "Current"` |
| DB state, agent has partial/drifted packages | Update `PackageStatesJson` | Update `PackageStatesJson`, set `Status = "Drifted"` |
| DB state, agent has no packages | Delete record | Delete record (correct — no row = Absent) |

**Also add:** `WorkloadId` parameter to `ReconcileProbeResults` so it can upsert with the correct workload reference. Currently it operates on an existing state only.

### 1.3 Update Status on Run Completion

**File:** `apps/orchestrator/backend/Runtime/NodeWorkloadStateService.cs` — `HandleCompleteAsync` (line ~133)

**New logic:**

| Completion Result | Current | New |
|-------------------|---------|-----|
| Successful install/upgrade steps | Set `CurrentRevisionId` | Set `CurrentRevisionId` + `Status = "Current"` |
| Successful uninstall steps | (handled elsewhere) | Set `Status = "Unknown"` (state should be deleted, but if kept, mark unknown) |
| All packages AlreadySatisfied (no steps executed) | No update to `CurrentRevisionId` | Set `CurrentRevisionId` to run's target revision + `Status = "Current"` |
| Failure | No update | Set `Status = "Drifted"` (partial install may have occurred) |

### 1.4 API: Expose Status in Responses

Update `NodeWorkloadStateResponse` and `NodeWorkloadAssignment` DTOs to include `Status`.

**Files:**
- `apps/orchestrator/backend/Contracts/Api/Nodes/NodeListResponse.cs` — `NodeWorkloadAssignment` (add `Status` field)
- `apps/orchestrator/backend/Contracts/Api/Nodes/NodeListResponse.cs` — `NodeWorkloadStateResponse` (add `Status` field)

### Acceptance Criteria — Phase 1

- [ ] `NodeWorkloadStateEntity` has `Status` column with check constraint
- [ ] Migration runs successfully on existing DB
- [ ] Pre-check reconciliation creates `NodeWorkloadState` for partial states (no row → row with `Status = "Drifted"`)
- [ ] Pre-check reconciliation sets `Status = "Current"` when all packages match
- [ ] Pre-check reconciliation sets `Status = "Drifted"` when packages drift
- [ ] Pre-check reconciliation deletes record when agent reports no packages
- [ ] Run completion sets `Status = "Current"` on successful install/upgrade
- [ ] Run completion sets `Status = "Current"` + `CurrentRevisionId` when all AlreadySatisfied
- [ ] Run failure sets `Status = "Drifted"`
- [ ] API responses include `Status` field

### Verification Steps — Phase 1

1. Run `dotnet test` — all existing tests pass
2. Create a `NodeWorkloadStateEntity` via migration, verify `Status` column exists with default `"Unknown"`
3. Call pre-check API for a node with no state but agent reports packages → verify `NodeWorkloadState` created with `Status = "Drifted"`
4. Call pre-check API for a node with state, agent reports all matching → verify `Status = "Current"`
5. Complete a run where agent reports AlreadySatisfied for all packages → verify `CurrentRevisionId` set and `Status = "Current"`
6. Complete a run that fails midway → verify `Status = "Drifted"`

---

## Phase 2: Version Comparison + Downgrade Protection

### 2.1 Add VersionComparer Utility (Orchestrator-Side)

**New file:** `apps/orchestrator/backend/Services/VersionComparisonService.cs`

The agent already has `VersionComparer` (`apps/agent/backend/Steps/VersionComparer.cs`) that does prefix matching. We need ordering (greater than / less than) on the orchestrator side.

```csharp
public static class VersionComparisonService
{
    // Returns: negative = a < b, zero = a == b, positive = a > b
    // Returns null if versions cannot be compared (non-numeric segments)
    public static int? CompareVersions(string? versionA, string? versionB)
    {
        // Parse into numeric-dot segments
        // Compare segment by segment
        // Return null if either has non-numeric segments that can't be parsed
    }

    public static bool IsDowngrade(string? currentVersion, string? targetVersion)
    {
        var result = CompareVersions(currentVersion, targetVersion);
        return result.HasValue && result.Value > 0;
    }

    public static bool IsUpgrade(string? currentVersion, string? targetVersion)
    {
        var result = CompareVersions(currentVersion, targetVersion);
        return result.HasValue && result.Value < 0;
    }
}
```

### 2.2 Enhance Pre-Check Response with Version Comparison

**File:** `shared/contracts/Runtime/Probes/DetectResponse.cs`

Add to `PackageDetectionResult`:
```csharp
public string? ExpectedVersion { get; set; } // The version expected by the workload
public string? Comparison { get; set; }       // "same" | "older" | "newer" | "unknown"
```

**Orchestrator enhancement** in `NodesController.ReconcileProbeResults` or the pre-check handler:
After getting probe results, for each package where `Status == WrongVersion`, compute `Comparison` using `VersionComparisonService.CompareVersions(actualVersion, expectedVersion)`.

### 2.3 Add Downgrade Protection to Run Creation

**File:** `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs` — `Create` method

After loading node workload states, for each node in `install` mode:
```csharp
// For nodes with CurrentRevisionId != null && != request.RevisionId
// Load current revision's packages
// Load target revision's packages
// For overlapping packages (same name), check if current version > target version
// If any package would downgrade, return 422 with details
```

**New error response:**
```json
{
  "code": "DOWNGRADE_BLOCKED",
  "message": "Cannot install workload v1 on node X — it has v2 which is a newer version.",
  "details": [
    { "nodeId": "...", "hostname": "...", "package": "PackageA", "currentVersion": "2.0", "targetVersion": "1.0" }
  ]
}
```

### Acceptance Criteria — Phase 2

- [ ] `VersionComparisonService.CompareVersions` returns correct ordering for numeric versions (e.g., "1.0" < "1.1" < "2.0")
- [ ] `VersionComparisonService.CompareVersions` returns null for non-comparable strings
- [ ] Pre-check response includes `Comparison` field for `WrongVersion` results
- [ ] Run creation returns 422 `DOWNGRADE_BLOCKED` when any selected node has a newer version of a package than the target revision
- [ ] Downgrade is NOT blocked when current version is older (normal upgrade)
- [ ] Downgrade is NOT blocked when node has no current version (fresh install)

### Verification Steps — Phase 2

1. Unit tests for `VersionComparisonService.CompareVersions`: numeric versions, pre-release tags, single-segment, multi-segment, null/empty
2. Call pre-check API with a version mismatch → verify `Comparison` field is populated
3. Create a run for a node with workload v2 targeting workload v1 → verify 422 response
4. Create a run for a node with workload v1 targeting workload v2 → verify run is created (normal upgrade)
5. Create a run for a node with no workload targeting any version → verify run is created (fresh install)

---

## Phase 3: Backend Wizard Support

### 3.1 Fix Uninstall Guard (Relax Revision Matching)

**File:** `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs` — lines 97-115

**Current:** All nodes must have `CurrentRevisionId == request.RevisionId`

**New:** All nodes must have a `NodeWorkloadState` for the workload (i.e., `CurrentRevisionId != null`). The specific revision doesn't need to match — the workload's uninstall commands are in the target revision's packages.

```csharp
// OLD:
var allAssignedToRevision = nodes.All(n =>
    nodeStatesByNodeId.TryGetValue(n.NodeId, out var state) &&
    state.CurrentRevisionId == request.RevisionId);

// NEW:
var allHaveWorkload = nodes.All(n =>
    nodeStatesByNodeId.TryGetValue(n.NodeId, out var state) &&
    state.CurrentRevisionId != null);
```

### 3.2 Fix "Already Fully Installed" Edge Case

**File:** `apps/orchestrator/backend/Runtime/NodeWorkloadStateService.cs` — `HandleCompleteAsync`

**Current:** `CurrentRevisionId` only set if timeline has `InstallOrUpgrade` or `UninstallPackage` success entries.

**New:** If run completes successfully with no install/upgrade/uninstall steps AND all pre-check probes returned `AlreadySatisfied`, set `CurrentRevisionId` to the run's target revision and `Status = "Current"`.

This handles the case where a node already has all packages but the `NodeWorkloadState` needs to record the assignment.

### 3.3 Add Pre-Check Summary Endpoint for Wizard

**New endpoint:** `POST /api/nodes/prechecks/summary`

Returns per-node summary computed from pre-check results + version comparison:

```json
{
  "nodes": [
    {
      "nodeId": "...",
      "hostname": "...",
      "workloadStatus": "Current" | "Drifted" | "Absent",
      "action": "Skip" | "FreshInstall" | "Update" | "InstallMissing" | "Reinstall" | "BlockedDowngrade",
      "actionDetail": "...",
      "packages": [
        {
          "packageId": "...",
          "name": "...",
          "status": "AlreadySatisfied" | "WrongVersion" | "NotPresent",
          "comparison": "same" | "older" | "newer" | "unknown" | null,
          "actualVersion": "...",
          "expectedVersion": "..."
        }
      ]
    }
  ]
}
```

Request body: `{ "nodeIds": [...], "workloadId": "...", "revisionId": "..." }`

This computes the `action` field:
- `Absent` workload status, all NotPresent → `FreshInstall`
- `Current` workload status, all AlreadySatisfied → `Skip` or `Reinstall` if forced
- `Drifted` workload status, some older → `Update`
- `Drifted` workload status, some NotPresent, some same → `InstallMissing`
- `Drifted` workload status, some newer → `BlockedDowngrade`

### 3.4 Add Dry-Run Endpoint Enhancement

**File:** `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs` — existing `DryRunPreview` endpoint

Enhance with version comparison and action labels. The existing `DryRunPackageAction.Action` field currently has: `FreshInstall`, `Update`, `Reinstall`, `NoAction`. Add:
- `InstallMissing` — some packages present (same version), some absent
- `BlockedDowngrade` — at least one package would be downgraded

### 3.5 Surface Idempotency Conflicts

In `WorkloadRunsController.Create` — lines 144-155, the active run guard returns 409 with a generic message.

**Enhancement:** Include the conflicting run details in the 409 response:
```json
{
  "code": "ACTIVE_RUN_CONFLICT",
  "message": "Node X already has an active run for workload Y",
  "conflictingRunId": "...",
  "conflictingRunState": "Running"
}
```

### Acceptance Criteria — Phase 3

- [ ] Uninstall mode only requires node to have workload assigned, not exact revision match
- [ ] Run completion sets `CurrentRevisionId` + `Status = "Current"` even when all packages AlreadySatisfied
- [ ] Pre-check summary endpoint returns per-node action labels with version comparison
- [ ] Dry-run endpoint includes `InstallMissing` and `BlockedDowngrade` actions
- [ ] Active run conflict response includes conflicting run details
- [ ] Downgrade protection blocks run creation at the API level

### Verification Steps — Phase 3

1. Create uninstall run for node with workload v1 assigned, targeting workload v2 revision → verify success (guard relaxed)
2. Create install run for node that already has all packages (AlreadySatisfied) → verify `CurrentRevisionId` set after completion
3. Call pre-check summary endpoint for node with partial packages → verify `InstallMissing` action
4. Call pre-check summary endpoint for node with newer package → verify `BlockedDowngrade` action
5. Create a run for a node that already has an active run → verify 409 with conflicting run details

---

## Phase 4: Frontend Wizard UI

### 4.1 Replace Two-Phase Modal with Stepper Wizard

**File:** `apps/orchestrator/web/src/pages/WorkloadRuns.tsx`

Replace the current `showSummary` boolean flip with a state machine:

```typescript
type WizardStep = 'mode' | 'workload' | 'nodes' | 'precheck' | 'confirm'
const [step, setStep] = useState<WizardStep>('mode')
```

Use the existing `<Stepper>` component from `components/ui/stepper.tsx` for step indicators.

### 4.2 Step 1: Mode Selection

- Two cards: Install / Uninstall
- Visual distinction: Install = accent color, Uninstall = danger color
- Selecting a mode advances to Step 2

### 4.3 Step 2: Workload + Version Selection

- Workload dropdown (from `listWorkloads`)
- Version dropdown:
  - Install mode: published versions only
  - Uninstall mode: versions currently installed on any online node
- Auto-select first workload + latest version

### 4.4 Step 3: Node Selection

- List of nodes filtered by mode:
  - Install: all online nodes
  - Uninstall: only nodes with the selected workload assigned (from `listNodeWorkloadStates`)
- Per-node info: hostname, OS, current workload version badge
- Select all online / Clear all
- "Cannot select offline nodes" indicator for offline nodes
- Node filter text input

### 4.5 Step 4: Pre-Checks (Blocking Gate)

**This is the core new behavior.**

When entering this step:
1. Disable "Next" button
2. Auto-run pre-checks for all selected nodes via `POST /api/nodes/prechecks/summary`
3. Show per-node results with computed action labels:

| Action | Badge Color | Label |
|--------|-------------|-------|
| `Skip` | Green | "Already current — nothing to do" |
| `FreshInstall` | Blue | "Fresh install" |
| `Update` | Amber | "Update: v1 → v2" (with version numbers) |
| `InstallMissing` | Amber | "Install missing, skip existing" |
| `Reinstall` | Slate | "Reinstall" (only if force enabled) |
| `BlockedDowngrade` | Red | "Blocked: downgrade not supported" |

4. **Blocking behavior:**
   - If ANY node has `BlockedDowngrade` → Next button disabled, red warning shown
   - If ALL nodes are `Skip` → Show "All nodes are current. Create run to register assignment?" prompt
   - If some nodes pass and others have warnings → Allow proceed with per-node warning acknowledgment
   - Force Override toggle: enables proceeding past warnings (not downgrade blocks)

5. **Offline node handling:** Nodes that fail pre-checks (timeout/error) are marked as "Pre-check failed" and excluded from run creation unless Force Override is enabled

### 4.6 Step 5: Confirm

- Summary card showing: mode, workload name, version, node count
- Per-node action summary (from pre-check results)
- For uninstall mode: red warning box + "I understand" checkbox
- "Back" button returns to Step 4
- "Create Run" / "Confirm Create Run" button submits

### 4.7 Pre-Check Auto-Run Changes

**Current:** Pre-checks auto-run on every `workloadId` + `revisionId` change (useEffect).

**New:** Pre-checks ONLY run when the user is on Step 4 (explicit step, not on every change). Remove the auto-pre-check useEffect.

### 4.8 Update Frontend Types

**File:** `apps/orchestrator/web/src/types.ts`

Add:
```typescript
type WorkloadAssignmentStatus = 'Current' | 'Drifted' | 'Unknown'
type PreCheckAction = 'Skip' | 'FreshInstall' | 'Update' | 'InstallMissing' | 'Reinstall' | 'BlockedDowngrade'

interface PreCheckPackageResult {
  packageId: string
  name: string
  status: 'AlreadySatisfied' | 'WrongVersion' | 'NotPresent'
  comparison?: 'same' | 'older' | 'newer' | 'unknown'
  actualVersion?: string
  expectedVersion?: string
}

interface PreCheckSummaryNode {
  nodeId: string
  hostname: string
  workloadStatus: WorkloadAssignmentStatus
  action: PreCheckAction
  actionDetail?: string
  packages: PreCheckPackageResult[]
}
```

### 4.9 Update API Client

**File:** `apps/orchestrator/web/src/services/api.ts`

Add:
```typescript
export async function runNodesPreCheckSummary(
  nodeIds: string[],
  workloadId: string,
  revisionId: string
): Promise<PreCheckSummaryResponse>
```

Maps to `POST /api/nodes/prechecks/summary`.

### Acceptance Criteria — Phase 4

- [ ] Wizard has 5 steps with visual Stepper indicator
- [ ] Step 1: Mode selection (Install/Uninstall) with clear visual distinction
- [ ] Step 2: Workload + version dropdown, context-appropriate version lists
- [ ] Step 3: Node selection with per-node workload status badges
- [ ] Step 4: Pre-check gate — blocks on downgrade, warns on drift, allows Skip for all-current
- [ ] Step 4: Force Override toggle available
- [ ] Step 4: Offline nodes excluded with clear indicator
- [ ] Step 5: Confirmation summary with per-node action labels
- [ ] Step 5: Uninstall requires "I understand" checkbox
- [ ] No auto-pre-check on workload/revision change (removed)
- [ ] Idempotency conflict shown as warning if active run exists for node+workload
- [ ] Existing run list and run details functionality preserved

### Verification Steps — Phase 4

1. Open Run Creator → Step 1 visible, Install/Uninstall cards render
2. Select Install → auto-advance to Step 2 → workload dropdown populated
3. Select workload → version dropdown shows published versions → auto-select latest
4. Advance to Step 3 → node list shows online nodes with workload status badges
5. Advance to Step 4 → pre-checks auto-run → per-node results show action labels
6. All nodes `Current` → Next enabled, shows "nothing to do" message
7. One node `BlockedDowngrade` → Next disabled, red warning
8. Enable Force Override → Next enabled (except for downgrade blocks)
9. Uninstall mode → Step 2 shows installed versions only
10. Step 5 → summary shows per-node actions, uninstall requires checkbox
11. Submit creates run via API → success → modal closes, run appears in list
12. Submit with active run conflict → warning shown in wizard

---

## Phase 5: Integration Testing + Edge Case Verification

### 5.1 Backend Unit Tests

**New test file:** `tests/orchestrator/unit/NodeWorkloadStateStatusTests.cs`

- Reconciliation sets `Status = "Current"` when all packages match
- Reconciliation sets `Status = "Drifted"` for partial/wrong versions
- Reconciliation creates new state for "no DB row but agent has packages" scenario
- Reconciliation deletes state when agent reports no packages
- Run completion sets `Status = "Current"` even for AlreadySatisfied-only runs
- Run failure sets `Status = "Drifted"`
- Downgrade guard returns 422 for node with newer version
- Downgrade guard passes for node with older version (normal upgrade)
- Uninstall guard passes when node has any CurrentRevisionId (not just exact match)

### 5.2 Backend Integration Tests

**New test file:** `tests/orchestrator/integration/PreCheckSummaryTests.cs`

- Pre-check summary endpoint returns per-node action labels
- Pre-check summary correctly identifies downgrade scenario
- Pre-check summary correctly identifies partial install scenario
- Dry-run preview includes `InstallMissing` and `BlockedDowngrade` actions

### 5.3 Frontend Tests

**New test file:** `apps/orchestrator/web/src/pages/__tests__/WorkloadRunsWizard.test.tsx`

- Wizard renders all 5 steps
- Mode selection advances to workload step
- Pre-check gate blocks on downgrade
- Pre-check gate allows all-current skip
- Force override enables progression
- Uninstall requires confirmation checkbox
- Idempotency conflict displayed

### 5.4 Manual E2E Scenarios

| Scenario | Steps | Expected Result |
|----------|-------|-----------------|
| Fresh install | Create node, no workload → install workload v1 | Pre-check: FreshInstall → Success → `Status = "Current"` |
| Install already-current | Install same workload v1 again | Pre-check: Skip → Confirm creates run → `Status = "Current"` on completion |
| Update | Install workload v1 → install workload v2 | Pre-check: Update → Success → `Status = "Current"` with `CurrentRevisionId = v2` |
| Partial install | Manually remove 1 of 2 packages → pre-check → install | Pre-check: InstallMissing → Success |
| Downgrade blocked | Install workload v2 → attempt install v1 | Pre-check: BlockedDowngrade → Next disabled |
| Uninstall | Install workload → uninstall | Pre-check shows workload → Confirm with checkbox → Success |
| Offline node | Node status = Offline → attempt pre-check | Excluded from selection, cannot proceed |

### Acceptance Criteria — Phase 5

- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] All frontend tests pass
- [ ] Manual E2E scenarios verified
- [ ] `dotnet test` passes at repository root
- [ ] `pnpm test` passes in `apps/orchestrator/web`
- [ ] `pnpm lint` passes in `apps/orchestrator/web`
- [ ] `pnpm build` (typecheck) passes in `apps/orchestrator/web`

---

## Implementation Order

```
Phase 1 (DB + Reconciliation) ──→ Phase 2 (Version Comparison) ──→ Phase 3 (Backend Wizard Support)
                                                                      │
                                                                      ↓
                                                               Phase 4 (Frontend Wizard UI)
                                                                      │
                                                                      ↓
                                                               Phase 5 (Testing)
```

Phases 1-3 are sequential (each builds on the previous). Phase 4 can begin once Phase 3's API contracts are defined (though implementation depends on Phase 3 endpoints being available). Phase 5 runs alongside Phase 4.

**Estimated effort per phase:**
- Phase 1: 2-3 sessions (entities, migration, reconciliation logic)
- Phase 2: 1-2 sessions (version comparison, downgrade protection)
- Phase 3: 2-3 sessions (endpoint changes, dry-run enhancement, uninstall guard fix)
- Phase 4: 3-4 sessions (wizard UI, pre-check gate, confirmation flow)
- Phase 5: 1-2 sessions (tests, E2E verification)