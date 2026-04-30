# Implementation Plan: Workload-Run Audit Fixes

## Objective
Address three findings from the workload-run system audit:
1. **Workload-level pre-check** — Add aggregate disk-space validation before any package operations begin.
2. **Test gaps** — Cover update-mode `preInitSteps`/`postInitSteps`, rollback core pipeline, and `PreCheckProbe` → init-step interactions.
3. **Artifact URL alignment** — Confirm the GUID-based fallback is in place and protected by tests.

---

## Step 1: Extend Contract — Add `SizeBytes` to `PackageAssignment`

**Context Brief**  
The agent needs artifact sizes to compute total disk space required for a workload. The orchestrator already knows artifact metadata when building assignments. Add an optional `SizeBytes` field to the shared contract so the orchestrator can transmit it without breaking existing consumers.

**Files**
- `src/Shared/Contracts/Runtime/RunPayloads/PackageAssignment.cs`

**Change**
Add `public long? SizeBytes { get; set; }` to `PackageAssignment`.

**Verification**
```bash
dotnet build src/Shared/Contracts
```

**Exit Criteria**
- Build succeeds.
- No other compile errors in solutions that reference `Shared.Contracts`.

**Dependencies**: None  
**Parallelizable**: No

---

## Step 2: Orchestrator — Populate `SizeBytes` When Building Assignments

**Context Brief**  
`WorkloadRunDispatcher.BuildPackageAssignments` and `WorkloadRunsController.BuildPendingPackageDto` construct the payloads sent to the agent. Populate `SizeBytes` from the artifact store metadata (or `PackageEntity` if already stored there). If size is unknown, leave it `null` — the agent will skip the aggregate check or can optionally fall back to `HEAD` requests.

**Files**
- `src/Orchestrator/Services/WorkloadRunDispatcher.cs`
- `src/Orchestrator/Controllers/WorkloadRunsController.cs`

**Change**
- In `BuildPackageAssignments`, set `SizeBytes = pkg?.SizeBytes` (or equivalent artifact metadata field).
- In `BuildPendingPackageDto`, set `SizeBytes = pkg?.SizeBytes` on `PendingPackageDto` if that DTO maps to `PackageAssignment`; otherwise verify `PendingPackageDto` already carries the size or add it.

**Verification**
```bash
dotnet build src/Orchestrator
dotnet test tests/Orchestrator --filter "FullyQualifiedName~Dispatcher"
```

**Exit Criteria**
- Build succeeds.
- Dispatcher tests pass (or at least do not fail due to the new field).

**Dependencies**: Step 1  
**Parallelizable**: No

---

## Step 3: Agent — Create `WorkloadPreCheck.cs`

**Context Brief**  
Create a new static step class that accepts the `DiffResult` and target package list, sums `SizeBytes` for packages that will actually be downloaded (`Added` + `Changed`), and compares against available free space on the temp drive (and optionally the install drive). It should return a typed result so `PipelineExecutor` can record the step and abort early on failure.

**Files**
- `src/Agent/Core/Steps/WorkloadPreCheck.cs` (new)

**Interface**
```csharp
public sealed class WorkloadPreCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public static class WorkloadPreCheck
{
    public static Task<WorkloadPreCheckResult> ExecuteAsync(
        DiffResult diff,
        List<PackageAssignment> targetPackages,
        ILogger logger,
        CancellationToken ct)
}
```

**Implementation Notes**
- Sum `SizeBytes` only for `diff.Added` and `diff.Changed`. Unchanged packages are skipped; removed packages free space.
- Check `Path.GetTempPath()` drive first (artifacts download there).
- Optionally check install-drive root(s) derived from target packages if install paths are known; if not, temp check is the MVP.
- Log required vs available bytes for observability.

**Verification**
```bash
dotnet build src/Agent
```

**Exit Criteria**
- Build succeeds.
- Class compiles and is reachable from `PipelineExecutor`.

**Dependencies**: Step 1  
**Parallelizable**: Yes (with Step 2, since Step 1 is done)

---

## Step 4: Agent — Wire `WorkloadPreCheck` into `PipelineExecutor`

**Context Brief**  
Insert the workload pre-check immediately after `DiffEngine.ComputeDiff` and before the `PreWorkloadSteps` loop. Record the step via `context.RecordStep` and `SendWorkloadStepStatusAsync` (or reuse `SendStepStatusAsync` with a synthetic package index). On failure, return `FinalizeAsync` so the run halts cleanly without touching any packages.

**Files**
- `src/Agent/Core/Pipeline/PipelineExecutor.cs`

**Change**
After:
```csharp
var diff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, preCheckResults);
```
Add:
```csharp
if (!isRollback)
{
    var wlPreCheck = await WorkloadPreCheck.ExecuteAsync(diff, targetPackages, _logger, ct);
    context.RecordStep("WorkloadPreCheck", -1, "", wlPreCheck.Success, wlPreCheck.Error);
    await SendStepStatusAsync(sendMessageAsync, context, /* synthetic assignment for index -1? */, "WorkloadPreCheck", wlPreCheck.Success, wlPreCheck.Error, ct);
    if (!wlPreCheck.Success)
    {
        _logger.LogError("WorkloadPreCheck failed: {Error}", wlPreCheck.Error);
        return await FinalizeAsync(sendMessageAsync, context, ct);
    }
}
```

**Caution**  
`SendStepStatusAsync` currently expects a `PackageAssignment`. If the messaging contract does not support a workload-level step, either:
- (A) Extend the status DTO to allow a nullable `PackageIndex`, or
- (B) Send a synthetic `PackageAssignment` with `Name = "WorkloadPreCheck"` and `PackageIndex = -1`.

Prefer (A) if the orchestrator UI consumes step statuses, otherwise (B) is acceptable for the agent-side audit log.

**Verification**
```bash
dotnet build src/Agent
```

**Exit Criteria**
- Build succeeds.
- No changes to existing phase ordering except the inserted pre-check.

**Dependencies**: Steps 2 and 3  
**Parallelizable**: No

---

## Step 5: Tests — Workload Pre-Check Coverage

**Context Brief**  
Add unit and integration tests for `WorkloadPreCheck` and its integration into `PipelineExecutor`. Verify it runs after `ComputeDiff`, before `PreWorkloadSteps`, and aborts the pipeline on failure.

**Files**
- `tests/Agent/Unit/WorkloadPreCheckTests.cs` (new)
- `tests/Agent/Integration/PipelineExecutorTests.cs` (append)

**Test Cases**
1. `WorkloadPreCheck_Passes_WhenSpaceIsSufficient`
2. `WorkloadPreCheck_Fails_WhenTempDriveSpaceIsInsufficient`
3. `WorkloadPreCheck_OnlyCountsAddedAndChangedPackages`
4. `PipelineExecutor_RunsWorkloadPreCheck_BeforePreWorkloadSteps`
5. `PipelineExecutor_Halts_WhenWorkloadPreCheckFails`
6. `PipelineExecutor_SkipsWorkloadPreCheck_OnRollback`

**Verification**
```bash
dotnet test tests/Agent --filter "FullyQualifiedName~WorkloadPreCheck"
```

**Exit Criteria**
- All new tests pass.
- Existing agent tests still pass.

**Dependencies**: Step 4  
**Parallelizable**: No

---

## Step 6: Tests — Fill Update/Rollback/Init-Step Coverage Gaps

**Context Brief**  
The audit identified six gaps. Add tests for the most critical four:
- Update mode with a `Changed` package running `preInitSteps`/`postInitSteps`.
- Rollback mode verifying core pipeline (`Uninstall`, `Acquire`, `Install`, `Verify`) still runs while skipping all init steps.
- `PreCheckProbe` returning `WrongVersion` leading to `Changed` classification and init-step execution.
- Two-phase uninstall-before-install with init steps on the changed/added package.

**Files**
- `tests/Agent/Unit/InitStepPipelineTests.cs` (append)
- `tests/Agent/Integration/PipelineExecutorTests.cs` (append)

**Test Cases**
1. `Update_ChangedPackage_RunsPreInitAndPostInit`
2. `Rollback_UninstallsRemovedAndInstallsChanged_WithoutInitSteps`
3. `Update_PreCheckWrongVersion_MarksChanged_RunsInitSteps`
4. `Update_TwoPhaseWithInitSteps_RunsInOrder`

**Verification**
```bash
dotnet test tests/Agent
```

**Exit Criteria**
- All new tests pass.
- Existing agent tests still pass.

**Dependencies**: None (these test existing behavior), but best done after Step 4 to avoid merge conflicts in `PipelineExecutorTests.cs`.  
**Parallelizable**: Yes (with Steps 1-5 if file conflicts are managed), but recommend serial after Step 5.

---

## Step 7: Verification — Artifact URL Fallback & Full Suite

**Context Brief**  
Confirm the GUID-based artifact fallback is present in `PipelineExecutor.cs` and add a regression test so the old `Name/Version` fallback cannot be reintroduced.

**Files**
- `src/Agent/Core/Pipeline/PipelineExecutor.cs` (read-only confirmation)
- `tests/Agent/Unit/AcquireArtifactTests.cs` or `tests/Agent/Integration/PipelineExecutorTests.cs` (append)

**Test Case**
- `AcquireArtifact_UsesGuidBasedUrl_WhenDownloadUrlIsEmpty` — verify the constructed URL ends with `{PackageId}/download`.

**Verification**
```bash
dotnet test tests/Agent
dotnet test tests/Orchestrator
```

**Exit Criteria**
- Full agent + orchestrator test suites pass.
- Code review confirms no `package.Name/package.Version` fallback remains.

**Dependencies**: Steps 5 and 6  
**Parallelizable**: No

---

## Dependency Graph

```
Step 1 ──→ Step 2 ──┐
                    ├──→ Step 4 ──→ Step 5 ──┐
Step 3 ─────────────┘                        ├──→ Step 7
                              Step 6 ────────┘
```

- **Step 1** is the root.
- **Steps 2 & 3** are parallel after Step 1.
- **Step 4** requires both 2 and 3.
- **Step 5** requires Step 4.
- **Step 6** can run in parallel with Steps 1-5, but recommend serial after Step 5 to avoid test-file merge conflicts.
- **Step 7** gates on everything.

---

## Rollback Strategy

- If `SizeBytes` population in the orchestrator proves complex (e.g., artifact metadata not readily available), fall back to **agent-side `HEAD` requests** in `WorkloadPreCheck` to discover `Content-Length` before downloading. This adds network latency but requires zero orchestrator changes.
- If messaging contract changes for workload-level steps are blocked, use the synthetic `PackageAssignment` approach in Step 4 and log a follow-up ticket to extend the status DTO.

---

## Estimated Effort

| Step | Files Touched | Estimated Time |
|------|--------------|----------------|
| 1 | 1 contract | 10 min |
| 2 | 2 orchestrator | 30 min |
| 3 | 1 new agent step | 30 min |
| 4 | 1 agent executor | 20 min |
| 5 | 2 test files | 45 min |
| 6 | 2 test files | 60 min |
| 7 | 1 test file + confirm | 20 min |
| **Total** | **~10 files** | **~3.5 hours** |
