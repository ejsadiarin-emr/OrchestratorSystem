# Phase 3 Review Report: Execution Modes

## GAPS — MVP Requirements Not Covered

### G-01: `AWAITING_CONFIRMATION` Status Missing for Admin Gate
**Severity: Blocker**

P3-002 creates UPDATE runs as `PENDING` awaiting admin confirmation. But P2-003's `GetNextTaskAsync()` fetches `PENDING` runs and immediately transitions them to `RUNNING`. The agent would pick up an unconfirmed update run. The `WorkloadRunStatus` enum (`PENDING | RUNNING | SUCCESS | FAILED | SKIPPED`) has no `AWAITING_CONFIRMATION` state. Either:
- Add `AWAITING_CONFIRMATION` to the enum and filter it out of polling, or
- Add a `requiresConfirmation` boolean flag to `WorkloadRun` and only reveal confirmed runs via polling.

MVP Plan Section 11.3 is explicit: *"Admin confirms before execution proceeds."*

### G-02: `POST /api/runs/{runId}/confirm` Endpoint Undefined
**Severity: Blocker**

Referenced in P3-002 tasks and acceptance criteria but no controller, DTO, or service method is provided. Needs:
- Who can call it (admin auth, not agent auth)
- Request/response models
- Validation (run must be in `AWAITING_CONFIRMATION` / approvable state)
- Transition logic (`AWAITING_CONFIRMATION` → `RUNNING` with `startedAt`)

### G-03: Polling Response Lacks Delta Context for UPDATE Mode
**Severity: High**

The `NextTaskResponse` / `TaskPackage` model from P2-003 doesn't include `deltaStatus` (MISSING / VERSION_DRIFT / MATCHES / ORPHANED) or `phase` (1 or 2). The Agent cannot distinguish between:
- A package to install (MISSING, Phase 1)
- A package to update (VERSION_DRIFT, Phase 1)
- A package to skip (MATCHES)
- A package to uninstall (ORPHANED, Phase 2)

Without this information, the Agent cannot execute the two-phase update correctly. `TaskPackage` needs at minimum: `deltaStatus`, `phase`, and `updateStrategy` (already present for install strategy but needed for update vs overinstall).

### G-04: `POST /api/runs/{runId}/steps` and `POST /api/runs/{runId}/complete` Endpoints Not Implemented in Phase 2
**Severity: High (Cross-phase dependency)**

These endpoints are listed in P2-002's auth middleware scope but no Phase 2 ticket implements the Orchestrator-side controller. P3-001 provides the implementation code, but Phase 2's P2-006 (Agent Detection) expects these endpoints to exist for step reporting. Either Phase 2 is incomplete, or Phase 3 must implement these endpoints first and P2-006's acceptance criteria presuppose Phase 3 infrastructure.

### G-05: No Step Ordering Field in `WorkloadRunStep`
**Severity: Medium**

MVP Plan Section 7 `WorkloadRunStep` schema: `id, runId, packageId, packageVersion, action, status, message, exitCode, startedAt, completedAt`. When `preInitSteps` contains multiple commands (e.g., `"net stop SQLBrowser"`, `"reg add …"`), each creates a `WorkloadRunStep` with `action = PRE_INIT_STEP` for the same `packageId + runId`. Without a `stepOrder` or `sequence` field, the execution order is ambiguous. The `id` (auto-increment) could serve as implicit ordering, but this should be explicit.

### G-06: `UPDATE` vs `INSTALL` Step Action Selection Logic Missing
**Severity: Medium**

The `WorkloadRunStepAction` enum includes both `INSTALL` and `UPDATE`. P3-002's step creation code comment says "INSTALL/UPDATE" but doesn't show the logic that selects `UPDATE` for VERSION_DRIFT packages vs `INSTALL` for MISSING packages. The Two-Phase Step Creation code example only shows `SKIP` for MATCHES and `DETECT` for everything else. The action selection for VERSION_DRIFT packages needs explicit `UPDATE` action assignment.

### G-07: `reinstall` Update Strategy Handling Undefined on Agent Side
**Severity: Medium**

MVP Plan Section 6.1 defines `updateStrategy: "overinstall" | "reinstall"`. P3-002 mentions handling both but provides no Agent-side code for `reinstall`. When `updateStrategy=reinstall`, the Agent must run `uninstallCommand + uninstallArgs` then `installCommand + installArgs` — effectively an UNINSTALL+INSTALL sequence for a single package. This requires either:
- Two separate step entries (UNINSTALL then INSTALL) for the package
- Agent-side logic that detects `reinstall` and chains two sub-operations

Neither approach is specified.

### G-08: Complete Run Endpoint `detectedPackages` Body Not Wired to Reconciliation
**Severity: Medium**

MVP Plan Section 8 defines `POST /api/runs/{runId}/complete` with body `{ status, detectedPackages: [{ packageId, version, detected: bool }] }`. P3-001 mentions "Run DB reconciliation (P2-007) after success" but the Orchestrator-side `ReportStepAsync` and completion code don't show how `detectedPackages` from the complete call feeds into reconciliation. The `CompleteUninstallRunAsync` in P3-003 ignores `detectedPackages` entirely and only uses step statuses.

---

## BUGS — Technical Issues in Code Examples

### B-01: `ReportStepAsync` Query Ambiguity for Multi-Command Steps
**Severity: High**

```csharp
var step = await _db.WorkloadRunSteps
    .FirstAsync(s => s.RunId == runId && s.PackageId == request.PackageId
                  && s.Action == Enum.Parse<WorkloadRunStepAction>(request.Action));
```

When multiple `PRE_INIT_STEP` entries exist for the same package, `FirstAsync` returns whichever row the database happens to return first — not necessarily the next unexecuted one. The query needs `stepOrder` or a `status == PENDING` filter to target the correct step.

### B-02: `Verb = "runas"` in `ProcessStartInfo` Does Not Elevate
**Severity: Low**

```csharp
Verb = "runas" // elevated
```

`ProcessStartInfo.Verb = "runas"` triggers a UAC prompt. The MVP Plan states the Agent always runs with elevated privileges. Since the Agent Windows Service already runs as Administrator (via `UseWindowsService()`), `cmd.exe /c` child processes inherit elevated privileges. The `Verb = "runas"` is unnecessary and would actually cause a UAC consent prompt that hangs in a service context. Remove it.

### B-03: `VerifyResult` Type Mismatch for VERIFY Step Reporting
**Severity: Medium**

```csharp
var verifyResult = _detectionService.Detect(package.Detection);
await ReportStepAsync(package.PackageId, "VERIFY", verifyResult, ct);
```

`Detect()` returns `DetectionResult` (with `Detected`, `DetectedVersion`, `Message`). But `ReportStepAsync` expects a `CommandResult` (with `ExitCode`, `Stdout`, `Stderr`). These are different types. The VERIFY step needs its own reporting path that converts detection results to step status (e.g., `Detected: true` → `SUCCESS`, `Detected: false` → `FAILED`).

### B-04: `CompleteUninstallRunAsync` Unconditionally Marks Run as `SUCCESS`
**Severity: Medium**

```csharp
run.Status = WorkloadRunStatus.SUCCESS;
```

This is set without checking if any steps failed. If all VERIFY steps find packages still present (WARNING), the run should probably not be `SUCCESS`. The method should aggregate step statuses and determine the overall run status.

---

## INCONSISTENCIES — Contradictions Between Tickets or MVP Plan

### I-01: Install Mode Pre-Check Requirement Unclear
MVP Plan Section 10 says: *"Pre-checks determine eligibility for all modes. Install, Update, and Uninstall all require a pre-check run (or re-use a recent one) before execution proceeds."* P3-001 doesn't mention requiring a pre-check before creating an INSTALL run. The tasks go directly to creating the run. P3-002 also skips the pre-check requirement — it jumps to delta computation without explicitly running a pre-check first. Should the Orchestrator enforce that a recent pre-check exists before allowing run creation?

### I-02: Workload Version vs Package Version in Downgrade Check
P3-002's `CompareVersions(targetWorkloadVersion, agent.AssignedWorkloadVersion!)` compares workload versions (e.g., "2.0" vs "1.0"). But P2-008's `CompareVersions` is used for package versions (e.g., "24.1.0" vs "23.0.0"). Workload versions may follow simpler schemes ("1.0", "2.0") while package versions can be complex ("15.0.18390.0"). The same `CompareVersions` method with its `Version.TryParse` fallback to string comparison handles both, but workload version comparison is a different semantic question — should "2.0-beta" or "2.0.1" be considered valid workload versions? The MVP Plan doesn't constrain workload version format.

### I-03: P3-001 SKIPPED Status for N/N Edge Case vs WorkloadRunStatus Enum
P3-001 says "if all packages MATCHES after pre-check, run status = SKIPPED". But the step creation code creates DETECT/PRE_INIT/INSTALL/POST_INIT/VERIFY steps for ALL packages before the Agent processes them. When the Agent skips a package (already installed), it reports DETECT as `Detected: true` with correct version. The Orchestrator then needs logic to infer: "all packages detected at correct version → run status = SKIPPED." This isn't shown in the P3-001 Orchestrator code. It's a gap between the stated behavior and the implementation.

### I-04: Uninstall DETECT Step Semantics Differ from Install/Update DETECT
In Install/Update mode, DETECT determines if a package is installed (and at what version) to decide SKIP vs. proceed. In Uninstall mode (P3-003), DETECT confirms what we're about to remove. If DETECT returns `Detected: false` during uninstall, what happens? P3-003 says it's "optional, confirms what we're removing" but the acceptance criteria says "Agent executes: DETECT (found) → UNINSTALL → VERIFY". If the package isn't found, should UNINSTALL be skipped? This behavioral difference isn't explicitly handled.

---

## MISSING DETAILS — Items Mentioned But Not Fully Specified

### M-01: Artifact Download and Temp Directory Lifecycle
P3-001 tasks mention downloading artifacts and storing in a temp directory, but no code shows:
- Temp directory creation
- Artifact download implementation  
- Cleanup after execution (success and failure paths)
- Disk space validation before download
- Retry logic structure (mentioned: "Handle network failures gracefully")

### M-02: Run Status Transition Logic for Partial Failures
What happens when 3 of 5 packages succeed and 2 fail? The P3-001 code returns individual `InstallResult` per package, but the overall run status logic isn't shown. P3-001 says "If any package failed: set run status to FAILED" but no code shows the aggregation from per-package results to run-level status.

### M-03: Phase 1 Failure — Does Phase 2 Run?
P3-002 specifies two-phase execution but doesn't address Phase 1 failures. If Phase 1 (install/update) fails for one package:
- Does Phase 2 (uninstall orphans) still run?
- Is the run marked FAILED immediately after Phase 1 failure?
- Do remaining Phase 1 packages continue after one fails?

MVP Plan Section 11.3 shows Phase 2 only running after Phase 1 verification succeeds, implying Phase 2 is skipped on Phase 1 failure. This should be explicit.

### M-04: `WorkloadRunStep` `stdout`/`stderr` Fields
MVP Plan Section 7 note says: *"Each individual command inside preInitSteps / postInitSteps is logged as its own WorkloadRunStep entry with stdout and exit code captured."* But the schema lists `message` and `exitCode` — no `stdout` or `stderr` columns. Either:
- The schema is wrong and needs `stdout`/`stderr` TEXT columns, or
- The note is aspirational and `stdout`/`stderr` are captured in the `message` field.

### M-05: Phase Transition Signaling Between Phase 1 and Phase 2 in UPDATE Mode
How does the Agent know Phase 1 is complete and Phase 2 should start? The polling model (P2-003) gives the Agent one task at a time. Options:
- Two separate `WorkloadRun` entries (one for Phase 1, one for Phase 2 after confirmation)
- A single run with the Agent understanding phase boundaries
- A new task dispatched after Phase 1 reports completion

None of these is specified.

### M-06: Agent-Side Model for Different Execution Modes
P3-001/002/003 all extend `AgentPollingService` but don't show the dispatch logic. The Agent receives a task with `mode: "INSTALL" | "UPDATE" | "UNINSTALL"` and must route to the correct execution handler. This routing logic and mode-specific behavior (e.g., calling `ExecutionService` for install vs `UninstallService` for uninstall) isn't specified.

### M-07: `NEEDS_UPDATE` State Interaction with Uninstall
Agent state machine: `REGISTERED → WORKLOAD_ASSIGNED → NEEDS_UPDATE`. P3-003 uninstalls from `WORKLOAD_ASSIGNED` but doesn't check for `NEEDS_UPDATE`. The `NEEDS_UPDATE` state means a newer workload version is available. Should uninstalling from `NEEDS_UPDATE` transition to `REGISTERED`? The same as from `WORKLOAD_ASSIGNED`? This isn't addressed.

---

## READINESS ASSESSMENT

**Phase 3 is NOT ready for implementation.** Three blockers must be resolved first:

1. **Admin confirmation gate** (G-01, G-02): The `AWAITING_CONFIRMATION` status and `/confirm` endpoint are architecturally necessary before UPDATE mode works. Without them, the agent would execute unconfirmed updates.

2. **Polling response schema for UPDATE mode** (G-03): The Agent cannot execute a two-phase update without knowing which packages belong to which phase. The `TaskPackage` / `NextTaskResponse` model must be extended with delta status and phase information before P3-002 can proceed.

3. **Step reporting endpoints** (G-04): `POST /api/runs/{runId}/steps` and `POST /api/runs/{runId}/complete` must exist before the Agent can report results. These should either be backported into Phase 2 or made the first task in Phase 3.

**Recommended pre-implementation checklist:**
- [ ] Add `AWAITING_CONFIRMATION` to `WorkloadRunStatus` enum
- [ ] Design and document `POST /api/runs/{runId}/confirm` endpoint (admin auth, DTOs, transitions)
- [ ] Extend `TaskPackage` with `deltaStatus`, `phase`, and ensure `updateStrategy` is populated for UPDATE mode
- [ ] Add `stepOrder` (int) column to `WorkloadRunSteps` schema
- [ ] Implement step reporting and run completion endpoints (consider pulling from P3-001 into P2)
- [ ] Define Phase 1→Phase 2 signaling approach for UPDATE mode
- [ ] Specify Agent-side mode dispatch routing