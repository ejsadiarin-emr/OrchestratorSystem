# 013 - Post-Run Detection Verification and Report Generation

## Problem

After a workload run completes, there is no summary of what changed. The orchestrator receives a binary `Complete`/`Fail` from the agent, updates the state machine, and stops. Operators have no artifact (txt report) showing pre-run vs. post-run package state per node.

Currently:
- `PreCheckProbe` results are captured in Phase 0 but discarded after the pipeline finishes
- `PostInstallVerify`/`PostUninstallVerify` run during the pipeline but results are not aggregated into a report
- `PostWorkloadSteps` exist as generic shell commands but are not tailored for structured detection-based summarization
- No report endpoint exists on the orchestrator; no report is stored or downloadable
- `ReasonCodes.PostInstallVerifyFailed = 2003` is defined in `shared/contracts/Jobs/ReasonCodes.cs:17` but never referenced

The feature ("post install smoke tests → check deployment status → outputs a report") is absent from the codebase. A grep for `smoke`, `PostValidation`, or `health check` across all `.cs` files returns zero results.

- Shallow, tightly coupled modules: report generation doesn't exist — the agent pipeline has the data but throws it away; the orchestrator has no report storage
- Integration risk: operators must manually SSH into nodes to verify deployment state
- Navigation friction: no single artifact captures the before/after state of a deployment

## Proposed Interface

### Agent: `ReportGenerator` (new class)

```
ReportGenerator.GenerateAsync(PipelineContext, AssignRunPayload, preCheckResults, postVerifyResults)
  → string (plain text report)
```

The report is generated synchronously in a new Phase 4 of the pipeline (after Phase 3 PostWorkload steps, before Finalize). It uses data already in memory:
- `PipelineContext.StepHistory` — all step results with timestamps
- `PipelineContext.PreCheckResults` — per-package pre-run detection snapshots
- `AssignRunPayload` — workload metadata, package list, mode
- Post-verify results — captured during Phase 1 (PostUninstallVerify) and Phase 2 (PostInstallVerify)

Report format: plain text with sections for pre-run state, post-run state, and summary.

### Agent: Report transmission

The report text is included as a new `Report` field in the `Complete`/`Fail` status envelope the agent sends via `PATCH /api/workload-runs/{runId}`. No extra HTTP round-trip.

### Orchestrator: Report storage

Add `ReportText` column to `WorkloadRunEntity` (TEXT, nullable). Store the report when the agent reports `Complete` or `Fail`.

### Orchestrator: Report endpoint

`GET /api/workload-runs/{runId}/report` — returns the stored report as `text/plain`.

### Frontend: Download button

Add a "Download Report" link/button to the run diagnostics modal in `WorkloadRuns.tsx`. Calls the report endpoint, triggers browser download.

### HTTP-only path

SignalR push remains disabled. The report flows through the existing HTTP polling path (`PATCH` final status).

## Acceptance Criteria

### AC-1: Agent generates report after every pipeline execution

**Given** a workload run is executing on the agent\
**When** the pipeline reaches finalization (Phase 4, before `FinalizeAsync`)\
**Then** a plain text report is generated containing:
  - Run metadata (RunId, Workload, Node, Mode, timestamps, Result)
  - Pre-Run Detection table (per-package: Expected version, Detected version, Status)
  - Post-Run Detection table (per-package: Expected version, Detected version, Status)
  - Run Summary (counts: installed, updated, uninstalled, unchanged with before→after version detail)
  - Step Timeline (chronological list of all steps with status and detail)

**Verification:**
1. Run `dotnet test tests/agent/unit --filter "FullyQualifiedName~ReportGeneratorTests"` — all new generator tests pass
2. Run `dotnet test tests/agent/integration --filter "FullyQualifiedName~PipelineExecutorTests"` — integration tests confirm report is non-null in Complete/Fail envelopes

### AC-2: Report is generated even when the pipeline fails

**Given** a pipeline step fails during execution\
**When** `FinalizeAsync` runs\
**Then** a report is still generated with:
  - `Result: FAILED [reason]` in the header
  - The failed step marked with error detail in the timeline
  - Partial post-run detection results for packages that completed before the failure
  - Packages that were never reached show `(not reached)` in the post-run table

**Verification:**
1. Run `dotnet test tests/agent/unit --filter "FullyQualifiedName~ReportGeneratorTests"` — assert report is not null/empty on failure scenario, assert `Result: FAILED` appears, assert `(not reached)` appears for skipped packages

### AC-3: Report is transmitted in the Complete/Fail PATCH envelope

**Given** the agent pipeline has finished\
**When** the agent sends `PATCH /api/workload-runs/{runId}` with `{ status: "Complete" }` or `{ status: "Failed" }`\
**Then** the request body includes a `report` field containing the full report text

**Verification:**
1. Inspect the `PATCH` request body in `AgentRuntimeService.cs` — confirm `Report` property is populated from `PipelineResult`
2. Run `dotnet test tests/agent/unit --filter "FullyQualifiedName~PipelineExecutorTests"` — mock the HTTP send and assert the serialized payload contains a non-empty `report` field

### AC-4: Orchestrator stores the report on status update

**Given** the agent sends `PATCH /api/workload-runs/{runId}` with a `report` field\
**When** `WorkloadRunsController.UpdateStatus()` processes the update\
**Then** the report text is persisted in `WorkloadRunEntity.ReportText`\
**And** the column accepts null (backward-compatible with runs created before this feature)

**Verification:**
1. Run `dotnet test tests/orchestrator/unit` — new test asserts `ReportText` is saved from the PATCH body
2. Inspect the `InstallerDbContext.OnModelCreating()` — confirm `ReportText` column type is nullable TEXT

### AC-5: Orchestrator exposes a report download endpoint

**Given** a workload run has completed with a stored report\
**When** `GET /api/workload-runs/{runId}/report` is called\
**Then** the response:
  - Status code: `200 OK`
  - Content-Type: `text/plain`
  - Body: the stored report text

**Given** a workload run has no stored report (e.g., pre-existing run, or run still in progress)\
**When** `GET /api/workload-runs/{runId}/report` is called\
**Then** the response:
  - Status code: `404 Not Found`

**Given** a non-existent runId is requested\
**When** `GET /api/workload-runs/{runId}/report` is called\
**Then** the response:
  - Status code: `404 Not Found`

**Verification:**
1. Run `dotnet test tests/orchestrator/unit` — new `WorkloadRunsControllerTests` or `WorkloadRunsControllerReportTests` class covers all three scenarios above
2. Run `dotnet run` and `curl http://localhost:5124/api/workload-runs/{id}/report` against a completed run

### AC-6: Frontend displays a download button in the run diagnostics modal

**Given** the run diagnostics modal is open for a completed run\
**When** the run has a stored report\
**Then** a "Download Report" button is visible\
**And** clicking it downloads the report as a `.txt` file via the browser's download mechanism

**Given** the run diagnostics modal is open for a run in progress (no report yet)\
**When** the run status is `Queued` or `Running`\
**Then** the "Download Report" button is hidden or disabled with a tooltip "Report available after run completes"

**Verification:**
1. Run `cd apps/orchestrator/web && pnpm test` — new test in `WorkloadRuns.test.tsx` asserts button visibility/absence based on run state
2. Run `cd apps/orchestrator/web && pnpm lint` — no lint errors in modified files

### AC-7: ReasonCodes.PostInstallVerifyFailed is wired up

**Given** `PostInstallVerify.ExecuteAsync()` returns `Success = false`\
**When** the pipeline constructs the `Fail` envelope in `FinalizeAsync`\
**Then** the envelope includes `ReasonCode = ReasonCodes.PostInstallVerifyFailed (2003)` instead of a generic failure code

**Verification:**
1. Run `dotnet test tests/agent/integration --filter "FullyQualifiedName~PipelineExecutorTests.PostInstallVerify"` — assert the Fail envelope contains the correct reason code
2. Search `shared/contracts/Jobs/ReasonCodes.cs` for all usages of `PostInstallVerifyFailed` — confirm it is referenced in agent code (currently unreferenced)

### AC-8: All new code has tests

**Given** the feature is implemented\
**Then** the test suite covers:

| Test Project | Test Class | What It Covers |
|---|---|---|
| `tests/agent/unit` | `ReportGeneratorTests` | Pre/post state tables, all modes (install/update/uninstall), success and failure paths, empty package lists, package counts in summary |
| `tests/agent/unit` | `ReportGeneratorTests` | Version formatting (null/missing versions, long versions), edge cases |
| `tests/agent/integration` | `PipelineExecutorTests` (updated) | Report field present in Complete/Fail envelopes, report non-empty on success and failure |
| `tests/agent/integration` | `PipelineExecutorTests` (updated) | PostInstallVerify failure sets ReasonCode 2003 |
| `tests/orchestrator/unit` | `WorkloadRunsControllerReportTests` (new) | Report endpoint: 200 with report, 404 without report, 404 for missing run |
| `tests/orchestrator/unit` | `WorkloadRunsControllerTests` or `NodeWorkloadStateServiceRevisionTests` (updated) | ReportText persisted from PATCH body, no regression on existing state transitions |
| `apps/orchestrator/web` | `WorkloadRuns.test.tsx` (updated) | Download button visible when report available, hidden when run pending/running |

## Dependency Strategy

- **Existing (reuse)**: `PackageDetector`, `VersionComparer`, `DetectionConfig`, `PipelineContext`
- **New (agent-side)**: `ReportGenerator` — pure function, no IO, trivially testable
- **New (orchestrator-side)**: `ReportText` column on `WorkloadRunEntity`, `GET report` endpoint
- **Modified**: `PipelineExecutor.FinalizeAsync()` — pass report into status envelope
- **Modified**: `WorkloadRunsController.UpdateStatus()` — store report text on `Complete`/`Fail`
- **Modified**: `WorkloadRunEntity` — add `ReportText` property
- **Modified**: Frontend `WorkloadRuns.tsx` — add download button in diagnostics modal

## Testing Strategy

- **New boundary tests to write**:
  - `ReportGeneratorTests` — pre/post state table formatting, all modes (install/update/uninstall), empty package list, partial failure scenarios
  - `WorkloadRunsControllerTests` — report endpoint returns 404 when no report, returns report text when stored, handles non-existent run
  - `PipelineExecutorTests` (integration) — assert report field present in Complete envelope, assert report generated on failure path too
- **Old tests to change**: `WorkloadRunEntity` schema change may need test fixture updates; `PipelineExecutorTests` completion assertions should validate report field
- **Test environment needs**: EF Core InMemory (existing), no new infrastructure

## Implementation Recommendations

- The report generator should be a pure function with no IO — takes structured data in, returns a string. This makes it trivially testable and easy to evolve the format later.
- Phase 4 should run even if prior phases had failures — a partial report is better than no report. Capture "FAILED — package not installed" in the report rather than skipping generation.
- Use `ReasonCodes.PostInstallVerifyFailed = 2003` in the fail envelope when PostInstallVerify fails, now that we're referencing it.
- The report endpoint should not require auth beyond what the rest of the API uses (no agent_id query param needed — this is an admin UI endpoint).
- Keep the report format simple plain text initially. A structured format (JSON, HTML) can be added later by extending the generator without changing the pipeline flow.

### Report Content Template

```
=== Deployment Report ===
Run ID:       {runId}
Workload:     {workloadName} (revision {revisionLabel})
Node:         {nodeDisplayName} ({nodeId})
Mode:         install | update | uninstall
Started:      {pipelineStartUtc}
Completed:    {completedAtUtc}
Result:       SUCCESS | FAILED [reason]

--- Pre-Run Detection ---
Package         Expected    Detected    Status
my-app          1.2.3       1.1.0       WrongVersion
my-tool         2.0.0       (none)      NotPresent

--- Post-Run Detection ---
Package         Expected    Detected    Status
my-app          1.2.3       1.2.3       AlreadySatisfied  ✓
my-tool         2.0.0       2.0.0       AlreadySatisfied  ✓

--- Run Summary ---
Packages processed:  2
  Installed:         1 (my-tool 2.0.0)
  Updated:           1 (my-app 1.1.0 → 1.2.3)
  Uninstalled:       0
  Unchanged:         0

--- Step Timeline ---
Step                Package       Status      Detail
PreCheckProbe       my-app        Completed   WrongVersion
PreCheckProbe       my-tool       Completed   NotPresent
PreWorkloadSteps    -             Completed
Uninstall           my-app        Completed   uninstall_first upgrade
AcquireArtifact     my-app        Completed
Install             my-app        Completed   exit_code_0
PostInstallVerify   my-app        Completed
AcquireArtifact     my-tool       Completed
Install             my-tool       Completed   exit_code_0
PostInstallVerify   my-tool       Completed
PostWorkloadSteps   -             Completed
```
