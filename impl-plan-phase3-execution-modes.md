# Implementation Plan — Phase 3: Execution Modes

> **MVP Plan Ref:** Section 11 (Execution Modes), Section 15, Phase 3  
> **Depends on:** Phase 2 complete

> **Phase 2 Prerequisites (M-03):** Phase 3 depends on the following Phase 2 completions:
> - **P2-009** (artifact download endpoints) must be complete — Agent uses these to download package binaries
> - **P2-010** (step reporting endpoints) must be complete — Agent reports step results via these endpoints
> - **P2-008** (delta summary) must be complete — UPDATE mode uses delta computation to determine per-package actions
> - **P2-003** (polling/heartbeat) and **P2-004** (task queue) must be functional — Agent polls for tasks and reports heartbeats

## Dependency Graph

```
P2-008 (delta summary) ── P3-001 (install mode)
P2-009 (artifact download) ── P3-001 (install mode)
P2-010 (step reporting)  ── P3-001 (install mode)
                         ├── P3-002 (update mode)
                         └── P3-003 (uninstall mode)
```

All three execution modes share common infrastructure from Phase 2 (polling, task queue, step reporting). They can be implemented in sequence as each builds on patterns established by the previous.

---

## Common Infrastructure

### StepResult Type (B-03)

All step types (DETECT, INSTALL, UPDATE, UNINSTALL, VERIFY) report results using a uniform `StepResult` type. This eliminates the type mismatch between `CommandResult` (used by DETECT/INSTALL steps) and `VerifyResult` (previously used by VERIFY steps).

```csharp
public class StepResult
{
    public int ExitCode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
}

// CommandResult maps to StepResult:
// - ExitCode, Stdout, Stderr copied directly
// - Success = ExitCode == 0
// - Message = optional context

// DetectionResult maps to StepResult:
// - Success = Detected
// - Message = detected version or "not found"
// - ExitCode = 0 if detected, 1 if not
// - Stdout/Stderr = detection command output
```

### WorkloadRunStep Model Fields (M-04, G-05)

The `WorkloadRunStep` model includes these fields (defined in Phase 1, verified here):

```csharp
public class WorkloadRunStep
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public string PackageId { get; set; }
    public string? PackageVersion { get; set; }
    public int StepOrder { get; set; }       // sequential order within the package's steps
    public WorkloadRunStepAction Action { get; set; }
    public WorkloadRunStepStatus Status { get; set; }
    public string? Message { get; set; }
    public int? ExitCode { get; set; }
    public string? Stdout { get; set; }      // captured command output
    public string? Stderr { get; set; }      // captured command error output
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### WorkloadRunStatus Enum (G-01)

```csharp
public enum WorkloadRunStatus
{
    PENDING,
    AWAITING_CONFIRMATION,  // UPDATE mode: waiting for admin confirmation
    RUNNING,
    SUCCESS,
    FAILED,
    SKIPPED
}
```

- **INSTALL** and **UNINSTALL** modes transition directly from `PENDING → RUNNING` (no confirmation gate).
- **UPDATE** mode transitions `PENDING → AWAITING_CONFIRMATION → RUNNING → SUCCESS|FAILED|SKIPPED`.
- A `SKIPPED` run occurs when all packages have `deltaStatus = MATCHES` (nothing to do) — see I-03.

### Version Comparison Semantics (I-02)

> **Note:** `WorkloadVersion` and `PackageManifest.Version` are different concepts:
> - `WorkloadVersion` is the workload definition version (e.g., "1.0.0") — it groups packages together.
> - `PackageManifest.Version` is the individual package version (e.g., "2.3.1") — it is the version of the specific package binary.
> - Version comparison for delta computation uses `PackageManifest.Version` to determine if an installed package matches the desired version.

### Partial Failure Note (M-02)

> **Note:** If some packages succeed and some fail in a multi-package workload, the run status is `FAILED` — no new `PARTIAL_FAILURE` enum value is needed for MVP. The individual step statuses track which packages succeeded and which failed. A run is `SUCCESS` only if all packages succeed; if even one package fails, the run is `FAILED`.

---

## TICKET P3-001: Install Mode (Full Flow)

**MVP Plan Ref:** Section 11.2 (Install Mode), Section 11.1 (Per-Package Execution Order), Section 14 (MVP Scope)  
**Depends on:** P2-008, P2-009, P2-010

### Description

Implement the complete Install mode flow: Orchestrator dispatch → Agent execution → per-package steps → verification → DB reconciliation → workload assignment. Covers all three edge cases: 0/N (nothing installed), X/N (partial), N/N (all already installed → SKIPPED).

**Pre-check requirement (I-01):** INSTALL mode ALWAYS starts with a DETECT pre-check for each package. Even for fresh installs, DETECT determines if anything is already installed. This produces `deltaStatus` values that inform the action for each package. The DETECT step is NOT optional for INSTALL mode.

### Tasks — Orchestrator Side

- [ ] Extend `RunService.CreateRunAsync()` to support `mode = INSTALL`
- [ ] `POST /api/runs` with `mode: "INSTALL"` creates PENDING run with all package steps
- [ ] For each package in workload, create `WorkloadRunStep` entries with sequential `StepOrder` values (G-05):
  - DETECT (pre-install detection) — stepOrder = 10
  - PRE_INIT_STEP (one step per command in preInitSteps) — stepOrder = 20, 21, 22...
  - INSTALL — stepOrder = 30
  - POST_INIT_STEP (one step per command in postInitSteps) — stepOrder = 40, 41, 42...
  - VERIFY (post-install verification) — stepOrder = 50
- [ ] Implement step result processing via P2-010 endpoints:
  - `POST /api/runs/{runId}/steps` — Agent reports step results (P2-010)
  - Update `WorkloadRunStep` status, message, exitCode, timestamps
  - Transition run status based on step results
- [ ] Implement run completion processing via P2-010 endpoints:
  - `POST /api/runs/{runId}/complete` — Agent signals run completion (P2-010)
  - Accept `detectedPackages` in the request body (G-08)
  - If all packages succeeded: set run status to `.SUCCESS`
  - If any package failed: set run status to `FAILED` with details
  - After successful completion, call reconciliation logic (P2-007) with detected packages to refresh `AgentNode.PackageStates` (G-08)
- [ ] Wire Install mode step reporting through P2-010 endpoints (G-04)
- [ ] After successful INSTALL run:
  - Run DB reconciliation (P2-007) with detected packages
  - Update `AgentNode.AssignedWorkloadId` and `AssignedWorkloadVersion`
- [ ] Handle SKIPPED edge case (I-03): if all packages are MATCHES after pre-check, the Orchestrator sets run status = `SKIPPED`. This is determined during run creation — if every package in the workload matches the desired versions, skip the entire run. If even one package needs action, the run proceeds normally.

### Tasks — Agent Side

- [ ] Extend `AgentPollingService` to handle INSTALL mode task from polling response
- [ ] Agent mode dispatch routing (M-05): when `phase` field = `"INSTALL"` in `NextTaskResponse`, execute install flow:
  - `phase: "INSTALL"` → DETECT → PRE_INIT → INSTALL → POST_INIT → VERIFY
- [ ] Implement per-package execution sequence:
  ```
  1. DETECT — run detection, report result
  2. If DETECT detects package already installed at correct version → SKIP, report
  3. PRE_INIT_STEP — execute each command via cmd.exe /c, report result per command
  4. INSTALL — execute installCommand + installArgs via cmd.exe /c
  5. POST_INIT_STEP — execute each command via cmd.exe /c, report result per command
  6. VERIFY — re-detect, report installed/detected
  7. REPORT — post step result to Orchestrator
  ```
- [ ] Step failure behavior:
  - preInitStep fails → abort install for this package → FAILED
  - Installer fails → skip postInitSteps → FAILED  
  - postInitStep fails → installer already succeeded → PARTIAL_SUCCESS / WARNING
  - Package skipped (already installed) → neither pre nor post steps run
- [ ] Download artifacts before execution (M-01):
  - `GET /api/artifacts/{artifactId}/download` to get binary (P2-009)
  - `GET /api/artifacts/{artifactId}/manifest` to get manifest (P2-009)
  - Store in temp directory on agent machine
  - Verify artifact hash after download
  - Clean up downloaded artifacts after step completion (or on failure)
- [ ] Execute commands via `System.Diagnostics.Process` with `cmd.exe /c <command>`
- [ ] Report each step result individually to Orchestrator via P2-010 endpoints
- [ ] On run completion, include `detectedPackages` in the `POST /api/runs/{runId}/complete` request (G-08)
- [ ] Handle network failures gracefully (retry logic for artifact download)

### Code Example — Agent Install Execution

```csharp
// Services/ExecutionService.cs
public async Task<InstallResult> ExecutePackageInstallAsync(
    TaskPackage package, string artifactDir, CancellationToken ct)
{
    // 1. DETECT (pre-check — always runs, even for fresh installs)
    var detectionResult = _detectionService.Detect(package.Detection);
    var stepResult = MapToStepResult(detectionResult);
    await ReportStepAsync(package.PackageId, (int)StepOrder.DETECT, stepResult, ct);

    if (detectionResult.Detected && detectionResult.DetectedVersion == package.Version)
    {
        // Package already at correct version — SKIP
        return new InstallResult(PackageStatus.SKIPPED, "Already installed at correct version");
    }

    // 2. PRE_INIT_STEPS
    foreach (var step in package.PreInitSteps)
    {
        var result = await ExecuteCommandAsync(step, ct);
        await ReportStepAsync(package.PackageId, (int)StepOrder.PRE_INIT, MapToStepResult(result), ct);
        if (result.ExitCode != 0)
        {
            return new InstallResult(PackageStatus.FAILED, $"Pre-init step failed: {step}");
        }
    }

    // 3. INSTALL
    var binaryPath = Path.Combine(artifactDir, package.InstallCommand);
    var installArgs = package.InstallArgs ?? "";
    var installResult = await ExecuteCommandAsync($"\"{binaryPath}\" {installArgs}", ct);

    var installStepResult = MapToStepResult(installResult);
    await ReportStepAsync(package.PackageId, (int)StepOrder.INSTALL, installStepResult, ct);

    if (installResult.ExitCode != 0)
    {
        return new InstallResult(PackageStatus.FAILED, "Installer failed");
    }

    // 4. POST_INIT_STEPS
    foreach (var step in package.PostInitSteps)
    {
        var result = await ExecuteCommandAsync(step, ct);
        var postStepResult = MapToStepResult(result);
        await ReportStepAsync(package.PackageId, (int)StepOrder.POST_INIT, postStepResult, ct);

        if (result.ExitCode != 0)
        {
            // Installer succeeded but post-init failed — PARTIAL_SUCCESS, not FAILED
            return new InstallResult(PackageStatus.PARTIAL_SUCCESS,
                $"Post-init step failed: {step}. Installer succeeded.");
        }
    }

    // 5. VERIFY
    var verifyResult = _detectionService.Detect(package.Detection);
    var verifyStepResult = MapToStepResult(verifyResult);  // uses StepResult uniformly (B-03)
    await ReportStepAsync(package.PackageId, (int)StepOrder.VERIFY, verifyStepResult, ct);

    if (!verifyResult.Detected)
    {
        return new InstallResult(PackageStatus.FAILED, "Verification failed — package not detected after install");
    }

    return new InstallResult(PackageStatus.SUCCESS, "Package installed and verified");
}

private StepResult MapToStepResult(CommandResult cmdResult)
{
    return new StepResult
    {
        ExitCode = cmdResult.ExitCode,
        Stdout = cmdResult.Stdout,
        Stderr = cmdResult.Stderr,
        Success = cmdResult.ExitCode == 0
    };
}

private StepResult MapToStepResult(DetectionResult detResult)
{
    return new StepResult
    {
        ExitCode = detResult.Detected ? 0 : 1,
        Stdout = detResult.Output,
        Stderr = null,
        Success = detResult.Detected,
        Message = detResult.Detected
            ? $"Detected version {detResult.DetectedVersion}"
            : "Package not detected"
    };
}

private async Task ReportStepAsync(string packageId, int stepOrder, StepResult result, CancellationToken ct)
{
    // Uses (runId, packageId, stepOrder) as unique key — NOT (runId, action) (B-01)
    var step = await _db.WorkloadRunSteps
        .FirstOrDefaultAsync(s => 
            s.RunId == _currentRunId && 
            s.PackageId == packageId && 
            s.StepOrder == stepOrder);

    if (step != null)
    {
        step.Status = result.Success ? WorkloadRunStepStatus.COMPLETED : WorkloadRunStepStatus.FAILED;
        step.ExitCode = result.ExitCode;
        step.Stdout = result.Stdout;
        step.Stderr = result.Stderr;
        step.Message = result.Message;
        step.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}

private async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct)
{
    var processInfo = new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = $"/c {command}",
        UseShellExecute = false,       // no UAC prompt (B-02)
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
        // Verb = "runas" removed — Agent already runs as SYSTEM (B-02)
    };

    using var process = Process.Start(processInfo)!;
    var stdout = await process.StandardOutput.ReadToEndAsync(ct);
    var stderr = await process.StandardError.ReadToEndAsync(ct);
    await process.WaitForExitAsync(ct);

    return new CommandResult
    {
        ExitCode = process.ExitCode,
        Stdout = stdout,
        Stderr = stderr
    };
}
```

### Code Example — Orchestrator Step Processing

```csharp
// Controllers/RunsController.cs
[HttpPost("{runId}/steps")]
[AgentAuth]
public async Task<IActionResult> ReportStep(int runId, [FromBody] StepReportRequest request)
{
    var agentId = HttpContext.Items["AgentId"]!.ToString();
    var step = await _runService.ReportStepAsync(runId, agentId, request);
    return Ok(step);
}

[HttpPost("{runId}/complete")]
[AgentAuth]
public async Task<IActionResult> CompleteRun(int runId, [FromBody] RunCompleteRequest request)
{
    var agentId = HttpContext.Items["AgentId"]!.ToString();
    await _runService.CompleteRunAsync(runId, agentId, request);
    return Ok();
}

// Services/RunService.cs
public async Task<WorkloadRunStep> ReportStepAsync(
    int runId, string agentId, StepReportRequest request)
{
    // Uses (runId, packageId, stepOrder) as unique key (B-01, G-05)
    var step = await _db.WorkloadRunSteps
        .FirstOrDefaultAsync(s => 
            s.RunId == runId && 
            s.PackageId == request.PackageId && 
            s.StepOrder == request.StepOrder);

    if (step == null)
        throw new NotFoundException($"Step not found for run {runId}, package {request.PackageId}, order {request.StepOrder}");

    step.Status = Enum.Parse<WorkloadRunStepStatus>(request.Status);
    step.Message = request.Message;
    step.ExitCode = request.ExitCode;
    step.Stdout = request.Stdout;
    step.Stderr = request.Stderr;
    step.StartedAt = request.StartedAt;
    step.CompletedAt = DateTime.UtcNow;

    await _db.SaveChangesAsync();
    return step;
}
```

### Code Example — Run Completion with Detected Packages (G-08)

```csharp
// Services/RunService.cs
public async Task CompleteRunAsync(int runId, string agentId, RunCompleteRequest request)
{
    var run = await _db.WorkloadRuns.Include(r => r.Steps).FirstAsync(r => r.Id == runId);

    // Determine final run status based on step results
    var allSteps = run.Steps.ToList();
    bool anyFailed = allSteps.Any(s => s.Status == WorkloadRunStepStatus.FAILED);
    run.Status = anyFailed ? WorkloadRunStatus.FAILED : WorkloadRunStatus.SUCCESS;
    run.CompletedAt = DateTime.UtcNow;

    // If detected packages were provided, run reconciliation (G-08)
    if (request.DetectedPackages != null && request.DetectedPackages.Any())
    {
        await _reconciliationService.ReconcileAsync(agentId, request.DetectedPackages);
    }

    if (run.Status == WorkloadRunStatus.SUCCESS && run.Mode == WorkloadRunMode.INSTALL)
    {
        var agent = await _db.AgentNodes.FirstAsync(a => a.AgentId == run.AgentId);
        agent.AssignedWorkloadId = run.WorkloadId;
        agent.AssignedWorkloadVersion = run.WorkloadVersion;
    }

    await _db.SaveChangesAsync();
}

// RunCompleteRequest includes detected packages
public class RunCompleteRequest
{
    public string Status { get; set; }
    public string? Message { get; set; }
    public List<DetectedPackageRequest>? DetectedPackages { get; set; }
}

public class DetectedPackageRequest
{
    public string PackageId { get; set; }
    public string DetectedVersion { get; set; }
}
```

### Acceptance Criteria

**Orchestrator:**
- [ ] `POST /api/runs` with `mode: "INSTALL"` creates run with DETECT, PRE_INIT_STEP, INSTALL, POST_INIT_STEP, VERIFY steps for each package
- [ ] Each `WorkloadRunStep` has a sequential `StepOrder` value
- [ ] DETECT step always runs first for INSTALL mode (not optional)
- [ ] Step reports update `WorkloadRunStep` records with status, message, exitCode, stdout, stderr via P2-010 endpoints
- [ ] Step lookup uses `(runId, packageId, stepOrder)` as unique key — NOT `(runId, action)` (B-01)
- [ ] Run completion (`POST /api/runs/{runId}/complete`) sets final run status and accepts `detectedPackages` (G-08)
- [ ] After successful INSTALL: `AgentPackages` updated (reconciliation with detected packages), `AgentNode.AssignedWorkloadId` set
- [ ] All packages MATCHES after pre-check → run status = SKIPPED (I-03)
- [ ] Partial failure (some packages fail) → run status = FAILED (M-02)

**Agent:**
- [ ] Agent downloads artifact binary before executing install, verifies hash, cleans up after step (M-01)
- [ ] Agent follows per-package execution order: DETECT → PRE_INIT → INSTALL → POST_INIT → VERIFY
- [ ] Agent mode dispatch: `phase: "INSTALL"` triggers install flow (M-05)
- [ ] Pre-init step failure → abort package, report FAILED
- [ ] Installer failure → skip post-init steps, report FAILED
- [ ] Post-init step failure → installer succeeded → report PARTIAL_SUCCESS
- [ ] Package already installed at correct version → SKIP
- [ ] Each step reported individually to Orchestrator via P2-010 endpoints
- [ ] All step types report using uniform `StepResult` type (B-03)
- [ ] `detectedPackages` included in run completion request (G-08)

**Edge Cases:**
- [ ] 0/N installed → install all packages
- [ ] X/N installed (partial) → skip existing, install missing
- [ ] N/N installed → all SKIPPED, run status = SKIPPED

**Artifact Lifecycle (M-01):**
- [ ] Agent downloads artifact before each package's INSTALL/UPDATE step
- [ ] Agent verifies artifact hash after download
- [ ] Agent cleans up downloaded artifacts after step completion or on failure

### Verification Steps

1. Create a workload with 3 packages (e.g., DBeaver, Python, SSMS manifests)
2. Enroll an agent, dispatch INSTALL run for a clean agent
3. Agent executes: DETECT (not found) → INSTALL → VERIFY for each package
4. After completion: `AgentNode.AssignedWorkloadId` set, `AgentPackages` updated with detected packages
5. Re-run INSTALL → all packages detected at correct version → SKIPPED
6. Simulate a pre-init failure → package marked FAILED, post-init skipped
7. Simulate a post-init failure → package marked PARTIAL_SUCCESS
8. Verify artifact download, hash check, and cleanup occur correctly
9. Verify `detectedPackages` are reconciled after run completion

---

## TICKET P3-002: Update Mode (Full Flow + Delta + Confirmation)

**MVP Plan Ref:** Section 11.3 (Update Mode), Section 13 (Workload Version Semantics)  
**Depends on:** P3-001

### Description

Implement the complete Update mode with downgrade rejection, two-phase execution (install new → uninstall orphans), and admin confirmation gate. This is the most complex execution mode.

### State Transitions (G-01)

UPDATE mode has a distinct state transition flow due to the confirmation gate:

```
PENDING → AWAITING_CONFIRMATION → RUNNING → SUCCESS|FAILED|SKIPPED
```

- **PENDING**: Run created, delta computed, awaiting admin review
- **AWAITING_CONFIRMATION**: Delta summary presented to admin, waiting for explicit confirmation
- **RUNNING**: Admin confirmed, Agent executing
- **SUCCESS|FAILED|SKIPPED**: Terminal states

INSTALL and UNINSTALL modes skip the confirmation gate:
```
PENDING → RUNNING → SUCCESS|FAILED|SKIPPED
```

### Confirm Endpoint (G-02)

`POST /api/runs/{runId}/confirm` — Admin endpoint for confirming UPDATE runs.

**Request body:**
```json
// To confirm:
{ "confirmed": true }

// To reject:
{ "confirmed": false, "reason": "Delta looks too risky for production" }
```

**Behavior:**
- If `confirmed: true` → transition run from `AWAITING_CONFIRMATION → RUNNING`
- If `confirmed: false` → transition run from `AWAITING_CONFIRMATION → FAILED` with reason stored
- Requires admin authentication (not agent auth)
- Returns `404` if run not found
- Returns `409` if run not in `AWAITING_CONFIRMATION` state
- Returns `200` on success

```csharp
// Controllers/RunsController.cs
[HttpPost("{runId}/confirm")]
[AdminAuth]  // admin auth, not agent auth
public async Task<IActionResult> ConfirmRun(int runId, [FromBody] ConfirmRunRequest request)
{
    var run = await _db.WorkloadRuns.FirstOrDefaultAsync(r => r.Id == runId);
    if (run == null)
        return NotFound();

    if (run.Status != WorkloadRunStatus.AWAITING_CONFIRMATION)
        return Conflict(new { error = "Run is not in AWAITING_CONFIRMATION state" });

    if (request.Confirmed)
    {
        run.Status = WorkloadRunStatus.RUNNING;
        run.ConfirmedAt = DateTime.UtcNow;
    }
    else
    {
        run.Status = WorkloadRunStatus.FAILED;
        run.FailureReason = request.Reason ?? "Update rejected by admin";
        run.CompletedAt = DateTime.UtcNow;
    }

    await _db.SaveChangesAsync();
    return Ok(run);
}

public class ConfirmRunRequest
{
    public bool Confirmed { get; set; }
    public string? Reason { get; set; }
}
```

### Action Selection Decision Table (G-06)

When the Agent receives an UPDATE task, it uses the `deltaStatus` field to decide the action for each package:

| `deltaStatus`       | Agent Action                                        | Notes                                               |
|---------------------|-----------------------------------------------------|------------------------------------------------------|
| `NOT_INSTALLED`     | INSTALL                                             | Full install sequence (DETECT → PRE_INIT → INSTALL → POST_INIT → VERIFY) |
| `VERSION_DRIFT`     | UPDATE                                              | Update sequence (DETECT → UPDATE/REINSTALL → VERIFY), NOT install |
| `MATCHES`           | SKIP                                                | Package already at correct version, no action needed |
| `NOT_IN_WORKLOAD`   | UNINSTALL                                           | Remove package (DETECT → UNINSTALL → VERIFY), runs in Phase 2 |

### Update Strategy Handling (G-07)

The Agent handles each `updateStrategy` from the package manifest:

| `updateStrategy` | Agent Behavior                                                                                      |
|------------------|-----------------------------------------------------------------------------------------------------|
| `reinstall`      | Stop service → Uninstall old version → Install new version → Start service (full replace)           |
| `inPlace`        | Run update commands only (assumes installer supports in-place upgrade)                             |
| *(not specified)*| Default to `reinstall`                                                                              |

**Reinstall strategy (full replace) execution sequence:**
1. Stop the service if running
2. Run the uninstaller for the old version (`uninstallCommand + uninstallArgs`)
3. Run the installer for the new version (`installCommand + installArgs`)
4. Start the service

**InPlace strategy execution sequence:**
1. Run the update command (typically `installCommand + installArgs` with upgrade flags)
2. VERIFY the updated version

### Tasks — Orchestrator Side

- [ ] `POST /api/runs` with `mode: "UPDATE"` — requires `targetWorkloadId` and `targetWorkloadVersion`
- [ ] Validate: agent must have an assigned workload (can't update from nothing)
- [ ] Validate: target version must be > current assigned version (downgrade rejection)
- [ ] New run status flow for UPDATE mode (G-01):
  - Create run with `PENDING` status
  - Compute delta and present summary to UI
  - Transition to `AWAITING_CONFIRMATION` after delta is computed
  - Only transition to `RUNNING` when admin confirms via `POST /api/runs/{runId}/confirm`
- [ ] Compute full delta using `DeltaService` (from P2-008)
- [ ] If any package has `AHEAD` status → reject the update run, return 400 with delta details
- [ ] Create steps for two phases:
  - **Phase 1**: Install/update packages (DETECT → PRE_INIT → INSTALL/UPDATE → POST_INIT → VERIFY)
  - **Phase 2**: Uninstall orphans (DETECT → UNINSTALL → VERIFY)
- [ ] Return delta summary to UI for admin confirmation before execution proceeds
- [ ] Wire Update mode step reporting through P2-010 endpoints (G-04)
- [ ] After successful UPDATE run:
  - Reconcile DB with detected packages (G-08)
  - Update `AgentNode.AssignedWorkloadId` and `AssignedWorkloadVersion` to new version
  - Remove orphaned `AgentPackage` records for uninstalled packages

### Polling Response for UPDATE Mode (G-03)

The `NextTaskResponse` for UPDATE mode includes delta context so the Agent knows what action to take per package:

```csharp
public class NextTaskResponse
{
    public bool HasTask { get; set; }
    public string? RunId { get; set; }
    public string Phase { get; set; }          // "INSTALL", "UPDATE", or "UNINSTALL" (G-03, M-05)
    public string Mode { get; set; }           // "INSTALL", "UPDATE", or "UNINSTALL"
    public string? WorkloadId { get; set; }
    public string? WorkloadVersion { get; set; }
    public string? TargetWorkloadId { get; set; }
    public string? TargetWorkloadVersion { get; set; }
    public List<DeltaPackageTask>? Packages { get; set; }   // delta context for UPDATE mode
}

public class DeltaPackageTask
{
    public string PackageId { get; set; }
    public string? PackageVersion { get; set; }
    public string DeltaStatus { get; set; }   // "NOT_INSTALLED", "VERSION_DRIFT", "MATCHES", "NOT_IN_WORKLOAD"
    public string? UpdateStrategy { get; set; } // "reinstall" or "inPlace", null defaults to "reinstall" (G-07)
    public string? InstallCommand { get; set; }
    public string? InstallArgs { get; set; }
    public string? UninstallCommand { get; set; }
    public string? UninstallArgs { get; set; }
    public List<string>? PreInitSteps { get; set; }
    public List<string>? PostInitSteps { get; set; }
    public string? Detection { get; set; }
    public string? ArtifactId { get; set; }
}
```

### Tasks — Agent Side

- [ ] Handle UPDATE mode task from polling response (phase = "UPDATE")
- [ ] Agent mode dispatch routing (M-05): when `phase: "UPDATE"` in `NextTaskResponse`, execute update flow
- [ ] Execute Phase 1 (install/update) packages using delta action selection (G-06):
  - `NOT_INSTALLED` → full install sequence (DETECT → PRE_INIT → INSTALL → POST_INIT → VERIFY)
  - `VERSION_DRIFT` → update sequence per `updateStrategy` (G-07):
    - `reinstall` (default): stop service → uninstall old → install new → start service
    - `inPlace`: run update commands only → VERIFY
  - `MATCHES` → SKIP (DETECT → SKIP)
- [ ] After Phase 1 completion, check if Phase 2 (orphans) exists
- [ ] Execute Phase 2 (uninstall orphans):
  - For `NOT_IN_WORKLOAD` packages: DETECT → UNINSTALL → VERIFY
  - VERIFY should confirm package is NOT found
- [ ] Report each step result to Orchestrator via P2-010 endpoints
- [ ] On run completion, include `detectedPackages` in the `POST /api/runs/{runId}/complete` request (G-08)

### NEEDS_UPDATE Interaction (M-06)

When `AgentNode.Status = NEEDS_UPDATE`:
- The Agent polls and receives an UPDATE task
- After successful completion: AgentNode transitions from `NEEDS_UPDATE → WORKLOAD_ASSIGNED`
- If the update fails: AgentNode stays at `NEEDS_UPDATE` (retry on next poll)
- The state transition after run completion is performed by the Orchestrator based on the run result

### Code Example — Update Delta Validation

```csharp
// Services/RunService.cs (update mode)
public async Task<WorkloadRunResponse> CreateUpdateRunAsync(
    string agentId, string targetWorkloadId, string targetWorkloadVersion)
{
    var agent = await _db.AgentNodes.FirstAsync(a => a.AgentId == agentId);

    if (string.IsNullOrEmpty(agent.AssignedWorkloadId))
        throw new InvalidOperationException("Agent has no assigned workload — use INSTALL mode");

    // Downgrade rejection (I-02 note: version comparison uses PackageManifest.Version, but 
    // workload-level version comparison uses WorkloadVersion)
    if (CompareVersions(targetWorkloadVersion, agent.AssignedWorkloadVersion!) <= 0)
        throw new InvalidOperationException(
            $"Target version {targetWorkloadVersion} must be greater than " +
            $"current version {agent.AssignedWorkloadVersion}");

    // Compute delta
    var delta = await _deltaService.ComputeDeltaAsync(agentId, targetWorkloadId, targetWorkloadVersion);

    // AHEAD rejection — any package ahead blocks the entire update
    var aheadPackages = delta.Packages.Where(p => p.Status == DeltaStatus.Ahead).ToList();
    if (aheadPackages.Any())
    {
        throw new InvalidOperationException(
            $"Cannot update: packages {string.Join(", ", aheadPackages.Select(p => p.PackageId))} " +
            "are at a newer version than the target workload");
    }

    // Check if all packages match — if so, skip the run entirely (I-03)
    if (delta.Packages.All(p => p.Status == DeltaStatus.Matches))
    {
        var skippedRun = new WorkloadRun
        {
            AgentId = agentId,
            WorkloadId = targetWorkloadId,
            WorkloadVersion = targetWorkloadVersion,
            Mode = WorkloadRunMode.UPDATE,
            Status = WorkloadRunStatus.SKIPPED,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        _db.WorkloadRuns.Add(skippedRun);
        await _db.SaveChangesAsync();
        return MapToResponse(skippedRun);
    }

    // Create run with PENDING status, then transition to AWAITING_CONFIRMATION (G-01)
    var run = new WorkloadRun
    {
        AgentId = agentId,
        WorkloadId = targetWorkloadId,
        WorkloadVersion = targetWorkloadVersion,
        Mode = WorkloadRunMode.UPDATE,
        Status = WorkloadRunStatus.AWAITING_CONFIRMATION,
        CreatedAt = DateTime.UtcNow
    };
    _db.WorkloadRuns.Add(run);
    await _db.SaveChangesAsync();

    // Create step entries for both phases (with sequential StepOrder values — G-05)
    int stepOrder = 10;
    foreach (var deltaPkg in delta.Packages.Where(p => p.Status != DeltaStatus.Orphaned))
    {
        if (deltaPkg.Status == DeltaStatus.Matches)
        {
            _db.WorkloadRunSteps.Add(new WorkloadRunStep
            {
                RunId = run.Id,
                PackageId = deltaPkg.PackageId,
                PackageVersion = deltaPkg.TargetVersion,
                StepOrder = stepOrder,
                Action = WorkloadRunStepAction.SKIP,
                Status = WorkloadRunStepStatus.SKIPPED
            });
            stepOrder += 10;
            continue;
        }

        // Action selection based on deltaStatus (G-06)
        var action = deltaPkg.Status == DeltaStatus.NotInstalled
            ? WorkloadRunStepAction.INSTALL
            : WorkloadRunStepAction.UPDATE; // VERSION_DRIFT → UPDATE, not INSTALL

        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id, PackageId = deltaPkg.PackageId,
            PackageVersion = deltaPkg.TargetVersion,
            StepOrder = stepOrder, Action = WorkloadRunStepAction.DETECT,
            Status = WorkloadRunStepStatus.PENDING
        });
        stepOrder += 10;
        // ... add PRE_INIT, INSTALL/UPDATE, POST_INIT, VERIFY steps ...
    }

    foreach (var deltaPkg in delta.Packages.Where(p => p.Status == DeltaStatus.Orphaned))
    {
        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id, PackageId = deltaPkg.PackageId,
            PackageVersion = deltaPkg.CurrentVersion,
            StepOrder = stepOrder, Action = WorkloadRunStepAction.DETECT,
            Status = WorkloadRunStepStatus.PENDING
        });
        stepOrder += 10;
        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id, PackageId = deltaPkg.PackageId,
            PackageVersion = deltaPkg.CurrentVersion,
            StepOrder = stepOrder, Action = WorkloadRunStepAction.UNINSTALL,
            Status = WorkloadRunStepStatus.PENDING
        });
        stepOrder += 10;
        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id, PackageId = deltaPkg.PackageId,
            PackageVersion = deltaPkg.CurrentVersion,
            StepOrder = stepOrder, Action = WorkloadRunStepAction.VERIFY,
            Status = WorkloadRunStepStatus.PENDING
        });
        stepOrder += 10;
    }

    await _db.SaveChangesAsync();
    return MapToResponse(run);
}
```

### Code Example — Update Run Completion with Detected Packages (G-08)

```csharp
// Services/RunService.cs
public async Task CompleteUpdateRunAsync(int runId, RunCompleteRequest request)
{
    var run = await _db.WorkloadRuns.Include(r => r.Steps).FirstAsync(r => r.Id == runId);
    var agent = await _db.AgentNodes.FirstAsync(a => a.AgentId == run.AgentId);

    bool anyFailed = run.Steps.Any(s => s.Status == WorkloadRunStepStatus.FAILED);
    run.Status = anyFailed ? WorkloadRunStatus.FAILED : WorkloadRunStatus.SUCCESS;
    run.CompletedAt = DateTime.UtcNow;

    // Reconcile detected packages (G-08)
    if (request.DetectedPackages != null && request.DetectedPackages.Any())
    {
        await _reconciliationService.ReconcileAsync(run.AgentId, request.DetectedPackages);
    }

    if (run.Status == WorkloadRunStatus.SUCCESS)
    {
        // Update agent to new workload version
        agent.AssignedWorkloadId = run.TargetWorkloadId ?? run.WorkloadId;
        agent.AssignedWorkloadVersion = run.TargetWorkloadVersion ?? run.WorkloadVersion;

        // Remove orphaned AgentPackage records
        var orphanedPackageIds = run.Steps
            .Where(s => s.Action == WorkloadRunStepAction.UNINSTALL && s.Status == WorkloadRunStepStatus.COMPLETED)
            .Select(s => s.PackageId)
            .Distinct();

        foreach (var pkgId in orphanedPackageIds)
        {
            var record = await _db.AgentPackages
                .FirstOrDefaultAsync(ap => ap.AgentId == run.AgentId && ap.PackageId == pkgId);
            if (record != null)
                _db.AgentPackages.Remove(record);
        }

        // Transition agent state (M-06)
        if (agent.Status == AgentNodeStatus.NEEDS_UPDATE)
            agent.Status = AgentNodeStatus.WORKLOAD_ASSIGNED;
    }
    else
    {
        // Failed update — agent stays in NEEDS_UPDATE (M-06)
        // Agent will retry on next poll
    }

    await _db.SaveChangesAsync();
}
```

### Acceptance Criteria

**Orchestrator:**
- [ ] `POST /api/runs` with `mode: "UPDATE"` creates a two-phase run with all steps
- [ ] Downgrade rejection: target version ≤ current version → 400 error
- [ ] AHEAD rejection: any package newer than target → 400 error with delta details
- [ ] All packages MATCHES → run status = SKIPPED (I-03)
- [ ] State transitions: PENDING → AWAITING_CONFIRMATION → RUNNING → SUCCESS|FAILED|SKIPPED (G-01)
- [ ] Admin confirmation gate: run stays `AWAITING_CONFIRMATION` until confirmed via `POST /api/runs/{runId}/confirm`
- [ ] Confirm endpoint: `POST /api/runs/{runId}/confirm` with admin auth (G-02)
- [ ] Confirm endpoint: returns 404 if run not found, 409 if not in AWAITING_CONFIRMATION state
- [ ] After successful UPDATE: `AgentNode.AssignedWorkloadId` updated to new version
- [ ] Orphaned package `AgentPackage` records removed after successful uninstall
- [ ] Step reporting wired through P2-010 endpoints (G-04)
- [ ] Detected packages reconciled after run completion (G-08)

**Agent:**
- [ ] Phase 1: install/update packages in order using delta action selection (G-06)
- [ ] `NOT_INSTALLED` packages: full install sequence
- [ ] `VERSION_DRIFT` packages: update per `updateStrategy` — NOT install (G-06)
- [ ] `MATCHES` packages: SKIP
- [ ] `NOT_IN_WORKLOAD` packages: UNINSTALL in Phase 2
- [ ] `reinstall` strategy: stop service → uninstall old → install new → start service (G-07)
- [ ] `inPlace` strategy: run update commands only (G-07)
- [ ] Default strategy (not specified): `reinstall` (G-07)
- [ ] Each step reported individually to Orchestrator via P2-010 endpoints
- [ ] `detectedPackages` included in run completion request (G-08)
- [ ] Agent mode dispatch: `phase: "UPDATE"` triggers update flow (M-05)

**Delta Computation:**
- [ ] Packages in old AND new with same version → MATCHES (SKIP)
- [ ] Packages in old AND new with newer version in new → VERSION_DRIFT (UPDATE)
- [ ] Packages only in new → NOT_INSTALLED (INSTALL)
- [ ] Packages only in old (not in new) → NOT_IN_WORKLOAD / ORPHANED (uninstall in Phase 2)
- [ ] Packages with agent version > target version → block (AHEAD error)

**NEEDS_UPDATE Interaction (M-06):**
- [ ] Agent in NEEDS_UPDATE state polls and receives UPDATE task
- [ ] Successful completion → NEEDS_UPDATE → WORKLOAD_ASSIGNED
- [ ] Failed completion → stays NEEDS_UPDATE (retry on next poll)

### Verification Steps

1. Install workload v1 on agent (packages A, B, C)
2. Create workload v2 (packages A updated, B same, D new — C is orphaned)
3. Compute delta: A=VERSION_DRIFT, B=MATCHES, C=NOT_IN_WORKLOAD, D=NOT_INSTALLED
4. Dispatch UPDATE run → status = AWAITING_CONFIRMATION
5. Admin confirms via `POST /api/runs/{runId}/confirm` → status = RUNNING
6. Agent executes Phase 1 (UPDATE A, INSTALL D, SKIP B) using delta action selection (G-06)
7. Phase 2: UNINSTALL C
8. After completion: `AgentNode.AssignedWorkloadId` = v2, C removed from `AgentPackages`, detected packages reconciled
9. Attempt update with version ≤ current → 400 downgrade error
10. Attempt update with AHEAD package → 400 AHEAD error
11. Verify NEEDS_UPDATE → WORKLOAD_ASSIGNED transition on success (M-06)
12. Verify NEEDS_UPDATE persists on failure (M-06)
13. Test `reinstall` update strategy: stop → uninstall → install → start (G-07)
14. Test `inPlace` update strategy: update commands only → verify (G-07)
15. Test confirm endpoint returns 404 for nonexistent run, 409 for wrong state

---

## TICKET P3-003: Uninstall Mode (Full Flow)

**MVP Plan Ref:** Section 11.4 (Uninstall Mode)  
**Depends on:** P3-001

### Description

Implement Uninstall mode: dispatch uninstall task, agent runs uninstall commands for each package, verifies removal, reconciles DB.

### Uninstall DETECT Semantics (I-04)

In uninstall mode, the DETECT step has inverted semantics compared to INSTALL mode:
- **INSTALL mode DETECT**: "Is this package installed?" → If yes at correct version → SKIP
- **UNINSTALL mode DETECT**: "Is this package still installed?" → If no → SKIP (already gone), If yes → proceed with UNINSTALL

This ensures the Agent doesn't attempt to uninstall a package that has already been removed.

### Tasks — Orchestrator Side

- [ ] `POST /api/runs` with `mode: "UNINSTALL"` — requires `agentId`, `workloadId`, `workloadVersion`
- [ ] Validate: agent must have the specified workload assigned
- [ ] Create `WorkloadRunStep` entries with sequential `StepOrder` values (G-05) for each package: DETECT → UNINSTALL → VERIFY
- [ ] `preInitSteps` / `postInitSteps` are NOT executed during Uninstall mode (MVP)
- [ ] Wire Uninstall mode step reporting through P2-010 endpoints (G-04)
- [ ] After successful UNINSTALL run:
  - Remove `AgentPackage` records for all uninstalled packages
  - Clear `AgentNode.AssignedWorkloadId` and `AssignedWorkloadVersion` (set to null)
  - Mark agent as `REGISTERED` (no workload assigned)
- [ ] Run completion logic must verify all uninstall steps succeeded before marking run as SUCCESS (B-04):
  - Check if any step has exit code != 0 → mark as FAILED with details
  - Check if agent reports packages that couldn't be uninstalled → mark as FAILED with details
  - Only mark SUCCESS if all steps completed successfully

### Tasks — Agent Side

- [ ] Handle UNINSTALL mode task from polling response (phase = "UNINSTALL")
- [ ] Agent mode dispatch routing (M-05): when `phase: "UNINSTALL"` in `NextTaskResponse`, execute uninstall flow:
  - `phase: "UNINSTALL"` → DETECT → UNINSTALL → VERIFY
- [ ] For each package, use uninstall DETECT semantics (I-04):
  1. DETECT — check if package is currently installed
     - If NOT installed → SKIP this package (already gone)
     - If installed → proceed with UNINSTALL
  2. UNINSTALL — run `uninstallCommand + uninstallArgs` via cmd.exe /c
  3. VERIFY — re-detect to confirm removal (should NOT find the package)
- [ ] Report each step result to Orchestrator via P2-010 endpoints
- [ ] On run completion, include `detectedPackages` (packages still found, if any) in `POST /api/runs/{runId}/complete` (G-08)
- [ ] Handle verification failure: if package still detected after uninstall → step flagged as WARNING but other packages continue

### Code Example — Uninstall Step Creation

```csharp
// Services/RunService.cs (uninstall mode)
public async Task<WorkloadRunResponse> CreateUninstallRunAsync(
    string agentId, string workloadId, string workloadVersion)
{
    var agent = await _db.AgentNodes.FirstAsync(a => a.AgentId == agentId);

    if (agent.AssignedWorkloadId != workloadId ||
        agent.AssignedWorkloadVersion != workloadVersion)
    {
        throw new InvalidOperationException(
            "Agent is not assigned this workload version. Cannot uninstall.");
    }

    var packages = await _db.WorkloadPackages
        .Where(wp => wp.WorkloadId == workloadId)
        .ToListAsync();

    var run = new WorkloadRun
    {
        AgentId = agentId,
        WorkloadId = workloadId,
        WorkloadVersion = workloadVersion,
        Mode = WorkloadRunMode.UNINSTALL,
        Status = WorkloadRunStatus.PENDING,
        CreatedAt = DateTime.UtcNow
    };

    _db.WorkloadRuns.Add(run);

    int stepOrder = 10;
    foreach (var pkg in packages)
    {
        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id,
            PackageId = pkg.PackageId,
            PackageVersion = pkg.PackageVersion,
            StepOrder = stepOrder,
            Action = WorkloadRunStepAction.DETECT,
            Status = WorkloadRunStepStatus.PENDING
        });
        stepOrder += 10;
        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id,
            PackageId = pkg.PackageId,
            PackageVersion = pkg.PackageVersion,
            StepOrder = stepOrder,
            Action = WorkloadRunStepAction.UNINSTALL,
            Status = WorkloadRunStepStatus.PENDING
        });
        stepOrder += 10;
        _db.WorkloadRunSteps.Add(new WorkloadRunStep
        {
            RunId = run.Id,
            PackageId = pkg.PackageId,
            PackageVersion = pkg.PackageVersion,
            StepOrder = stepOrder,
            Action = WorkloadRunStepAction.VERIFY,
            Status = WorkloadRunStepStatus.PENDING
        });
        stepOrder += 10;
    }

    await _db.SaveChangesAsync();
    return MapToResponse(run);
}
```

### Code Example — Run Completion (Uninstall) — Fixed (B-04)

```csharp
// Services/RunService.cs
public async Task CompleteUninstallRunAsync(int runId, RunCompleteRequest request)
{
    var run = await _db.WorkloadRuns
        .Include(r => r.Steps)
        .FirstAsync(r => r.Id == runId);
    var agent = await _db.AgentNodes.FirstAsync(a => a.AgentId == run.AgentId);

    // B-04: Verify all uninstall steps succeeded before marking run as SUCCESS
    var steps = run.Steps.ToList();
    var failedSteps = steps.Where(s => s.Status == WorkloadRunStepStatus.FAILED).ToList();
    var anyPackageStillInstalled = request.DetectedPackages != null && request.DetectedPackages.Any();

    if (failedSteps.Any())
    {
        // Some steps failed — mark run as FAILED with details
        run.Status = WorkloadRunStatus.FAILED;
        run.Message = $"Uninstall failed for packages: {string.Join(", ", failedSteps.Select(s => s.PackageId))}";
        run.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return;
    }

    if (anyPackageStillInstalled)
    {
        // Agent reports packages that couldn't be uninstalled — mark as FAILED
        run.Status = WorkloadRunStatus.FAILED;
        run.Message = $"Packages still installed after uninstall attempt: {string.Join(", ", request.DetectedPackages.Select(p => p.PackageId))}";
        run.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return;
    }

    // All steps succeeded — mark as SUCCESS and clean up
    var uninstalledPackageIds = steps
        .Where(s => s.Action == WorkloadRunStepAction.UNINSTALL)
        .Select(s => s.PackageId)
        .Distinct();

    foreach (var packageId in uninstalledPackageIds)
    {
        var record = await _db.AgentPackages
            .FirstOrDefaultAsync(ap => ap.AgentId == run.AgentId && ap.PackageId == packageId);
        if (record != null)
        {
            _db.AgentPackages.Remove(record);
        }
    }

    // Clear workload assignment
    agent.AssignedWorkloadId = null;
    agent.AssignedWorkloadVersion = null;
    agent.Status = AgentNodeStatus.REGISTERED;

    // Reconcile detected packages (should be empty for successful uninstall)
    if (request.DetectedPackages != null)
    {
        await _reconciliationService.ReconcileAsync(run.AgentId, request.DetectedPackages);
    }

    run.Status = WorkloadRunStatus.SUCCESS;
    run.CompletedAt = DateTime.UtcNow;

    await _db.SaveChangesAsync();
}
```

### Acceptance Criteria

**Orchestrator:**
- [ ] `POST /api/runs` with `mode: "UNINSTALL"` creates PENDING run with DETECT → UNINSTALL → VERIFY steps (with sequential StepOrder values)
- [ ] Validation: agent must have the specified workload assigned
- [ ] `preInitSteps` / `postInitSteps` are NOT included in UNINSTALL steps
- [ ] After successful UNINSTALL: `AgentPackage` records removed, `AgentNode.AssignedWorkloadId/Version` cleared
- [ ] Run completion verifies all steps succeeded before marking SUCCESS (B-04)
- [ ] If any step has exit code != 0 → run status = FAILED with details (B-04)
- [ ] If agent reports still-installed packages → run status = FAILED (B-04)
- [ ] Step reporting wired through P2-010 endpoints (G-04)
- [ ] Detected packages reconciled after run completion (G-08)

**Agent:**
- [ ] Uninstall mode executes DETECT → UNINSTALL → VERIFY for each package
- [ ] DETECT uses uninstall semantics (I-04): if not installed → SKIP, if installed → proceed
- [ ] `uninstallCommand + uninstallArgs` run via cmd.exe /c
- [ ] VERIFY: re-detect, package should NOT be found
- [ ] If VERIFY finds package still present → WARNING status, not FAILED
- [ ] Each step reported individually via P2-010 endpoints
- [ ] Agent mode dispatch: `phase: "UNINSTALL"` triggers uninstall flow (M-05)
- [ ] `detectedPackages` included in run completion request (G-08)

**State Transitions:**
- [ ] Agent transitions from `WORKLOAD_ASSIGNED` to `REGISTERED` after successful uninstall
- [ ] Agent's `AgentPackages` for uninstalled packages are removed from DB

### Verification Steps

1. Install workload v1 on agent (packages A, B, C)
2. Dispatch UNINSTALL run for that workload
3. Agent executes: DETECT (found) → UNINSTALL → VERIFY (not found) for each package
4. After completion: agent status is `REGISTERED`, `AssignedWorkloadId` is null
5. `AgentPackages` for A, B, C are removed from DB
6. Attempt uninstall when agent has no assigned workload → error
7. Verify that a failed uninstall (package still present) results in run status = FAILED, not unconditional SUCCESS (B-04)
8. Verify DETECT in uninstall mode: package not installed → SKIP (I-04)
9. Verify step reporting uses P2-010 endpoints (G-04)
10. Verify `detectedPackages` are sent in run completion and reconciled (G-08)