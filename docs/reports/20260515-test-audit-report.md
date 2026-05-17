# DeploymentPoC — Unified Test Audit Report

**Date:** 2026-05-14 | **Scope:** Orchestrator (unit + integration), Agent (unit + integration), Contracts, Frontend

## Scorecard

| Area | Test Files | Tests | Pass | Fail | Critical | High | Medium | Low |
|---|---|---|---|---|---|---|---|---|
| Orchestrator Unit | 14 | 176 | 169 | **7** | 2 | 8 | 9 | 6 |
| Agent Unit | 15 | ~80 | ~80 | 0 | 1 | 2 | 4 | 5 |
| Agent Integration | 5 | ~25 | ~25 | 0 | 0 | 1 | 2 | 2 |
| Orchestrator Integration | 10 | ~35 | ~35 | 0 | 0 | 0 | 2 | 3 |
| Contracts | 2 | ~15 | ~15 | 0 | 0 | 2 | 0 | 2 |
| Frontend | 8 | ~50 | ~50 | 0 | 1 | 5 | 8 | 8 |
| **Total** | **54** | **~381** | **~374** | **7** | **4** | **18** | **25** | **26** |

---

## CRITICAL Findings

### C1. 7 Orchestrator Unit Tests Fail at Runtime

**File:** `WorkloadRunsControllerCurrentPackagesTests.cs`

**Root cause:** Tests mock `IHubContext<AgentRuntimeHub>` and capture `SendCoreAsync` calls, but the controller now delegates to `WorkloadRunDispatcher.DispatchAsync()` which uses a real dispatcher instance. The captured envelopes list stays empty — assertions fail on count=0 or IndexOutOfRange.

**Affected tests:** All 5 CurrentPackages tests + 2 downgrade/conflict tests (which also use `dynamic` binding on anonymous types).

### C2. Dead SignalR Code & Tests in Agent

**File:** `AgentRuntimeServiceTests.cs` — 3 tests `[Ignore("SignalR replaced by HTTP polling")]`

**Files:** `FakeHubConnection.cs`, `FakeHubConnectionFactory.cs` — test helpers for removed SignalR feature still exist.

**Impact:** 3 permanently-ignored tests, dead test infrastructure, and the HTTP polling tests still construct `FakeHubConnection` to satisfy the constructor even though SignalR is disabled.

### C3. Frontend Uses `fireEvent` Exclusively — No `userEvent`

**Files:** All 6 component test files

**Impact:** `fireEvent.click()` skips focus/blur/change event sequencing that real browsers perform. This means tests pass even when components break for real users (e.g., form validation that depends on blur events).

### C4. Mixed Test Frameworks in Agent Projects

**Files:** Agent unit tests freely mix NUnit `[Test]` and xUnit `[Fact]` in the same project, and even within the same file (`tests/agent/integration/PipelineExecutorTests.cs`).

**Impact:** Maintenance confusion, wrong assertion patterns, potential runner misconfiguration.

---

## HIGH Findings

### H1. Orchestrator: NodeHeartbeatMonitorServiceTests Test Nothing Meaningful

**File:** `NodeHeartbeatMonitorServiceTests.cs`

All 3 tests use `Assert.DoesNotThrowAsync` — they verify no exception but never check that nodes actually transition from Online→Offline when stale. The core behavioral logic is untested.

### H2. Orchestrator: InstallControllerTests Tests Legacy Pipeline with Weak Assertions

**File:** `InstallControllerTests.cs`

Both tests only check for `OkObjectResult` — the "error" test should return a non-200 status but doesn't. The `IPipeline<InstallContext>` architecture is documented as legacy.

### H3. Orchestrator: Major Controller/Service Coverage Gaps

- **EnrollmentController** — 0 tests
- **PackagesController** — 0 tests
- **WorkloadsController** — only CreateRevision and BulkImport tested (7+ endpoints untested)
- **WorkloadRunsController** — only Create tested (7+ endpoints untested)
- **NodesController** — only PreCheck tested (6+ endpoints untested)
- **UploadSessionService, ArtifactZipService, PolicyEvaluationService** — 0 unit tests

### H4. Agent: Duplicate DiffEngineTests Files

`tests/agent/unit/DiffEngineTests.cs` (7 basic tests, 2-param overload) AND `tests/agent/unit/Pipeline/DiffEngineTests.cs` (9 advanced tests, 3-4 param overloads). Both test `DiffEngine.ComputeDiff()`. Should be merged.

### H5. Agent: Critical Steps with Zero Test Coverage

- `WorkloadPreCheck.cs` — 0 tests
- `PreCheckProbe.cs` — 0 tests
- `InitStepEnvVars.cs` — 0 tests
- `PipelineContext.cs` — 0 direct tests

### H6. Contracts: Missing Tests for 8+ Shared DTOs

Only `AssignRunPayload` and `PackageAssignment` have tests. No round-trip serialization tests for: `PendingWorkloadRunResponse`, `FinalizationPayload`, `StepStatusPayload`, `DetectRequest/DetectResponse`, `InstallAdapterConfig`, `DetectionConfig`, `MessageTypes`, `WorkloadAssignmentStatus`.

### H7. Frontend: Workloads.test.tsx Has Only 2 Tests for 868-Line Component

Missing: create/edit revision, publish, delete, bulk import, error states — <5% coverage.

### H8. Frontend: Nodes.test.tsx — NodeDetailsModal Unmocked API Calls

Clicking a node row triggers `getNodeDetails` and `runNodePreChecks` which are not in the API mock.

### H9. Frontend: No Accessibility Testing

Zero files use `jest-axe` or verify ARIA patterns beyond incidental `getByRole`.

---

## MEDIUM Findings

### M1. Orchestrator: Inconsistent DB Test Patterns

`WorkloadImportServiceTests` and `WorkloadImportServiceMappingTests` use `InMemoryDatabase` (no constraint enforcement) while all other files use `SqliteConnection :memory:`. InMemoryDatabase can mask FK/constraint bugs.

### M2. Orchestrator: Massive Seed Data Duplication in CurrentPackages Tests

Each test seeds 30-80 lines of nearly identical DB setup. Extract a shared `TestSeedBuilder` or use `[TestCaseSource]`.

### M3. Orchestrator: Reflection-Based Private Method Testing

`WorkloadImportServiceTests.cs:286-305` — uses reflection to call private `WorkloadRunDispatcher.DeserializeStringList`. Tests implementation details with no compile-time safety.

### M4. Orchestrator: Report Tests Are Integration Tests Masquerading as Unit Tests

`WorkloadRunsControllerReportTests.cs` uses real DB, real services — none are mocked.

### M5. Agent: "Integration" Tests Don't Start Real Server

Agent integration tests construct services directly. They never start Kestrel or test `/api/detect` or `/health` endpoints. These are component tests, not integration tests.

### M6. Frontend: Missing MemoryRouter in 4 of 5 Page Tests

`WorkloadRuns`, `Workloads`, `Install`, `Nodes` render without routing context. Works now but will break if any child adds a router hook.

### M7. Frontend: CSS Class Assertions Test Implementation Details

`Nodes.test.tsx` asserts on `bg-emerald-*`, `bg-slate-*` Tailwind classes — these change with styling refactors. Should use semantic queries.

### M8. Frontend: Dashboard Bypasses getOrchestratorHomeData Transformation

`Dashboard.test.tsx` mocks `getOrchestratorHomeData` to return pre-shaped data, bypassing the 100+ line transformation logic in `api.ts:1527-1635`. Bugs in that logic won't be caught.

---

## LOW Findings (Summary)

- PipelineTests.cs has a tautological test: `Assert.That(true, Is.True)`
- Agent: `AcquireArtifactTests` uses reflection for private methods
- Agent: Registry tests will fail on non-Windows (no platform guard)
- Agent: Timing-dependent timeout test (1s `DownloadTimeoutSeconds`)
- Contracts: `AssignRunPayloadTests` doesn't test JSON round-trip
- Frontend: Unused mock `advanceWorkloadRun` in WorkloadRuns tests
- Frontend: `realtime.ts` has no direct unit tests
- Frontend: 15+ API functions lack direct tests
- Frontend: No error boundary or loading state tests for most pages
- Orchestrator: `InstallerDbContextShapeTests` tests legacy entity shapes

---

## Coverage Gap Matrix

### Orchestrator Backend (0% test coverage)

| Area | Missing |
|---|---|
| Controllers | EnrollmentController, PackagesController, most WorkloadsController/WorkloadRunsController/NodesController endpoints |
| Services | UploadSessionService, ArtifactZipService, PolicyEvaluationService (unit), WorkloadRunDispatcher.DispatchAsync, NodeWorkloadStateService (3 methods) |
| Integration | GET /api/workload-runs/pending, /api/detect probe, PUT/PATCH workloads, DELETE endpoints |

### Agent Backend (0% test coverage)

| Area | Missing |
|---|---|
| Steps | WorkloadPreCheck, PreCheckProbe, PostInstallVerify (unit), EmitFinalization (unit) |
| Pipeline | InitStepEnvVars, PipelineContext |
| Services | AgentEnrollmentService.ConsumeEnrollmentTokenAsync |
| E2E | No tests start agent Kestrel, test /api/detect or /health |

### Frontend (0% test coverage)

| Area | Missing |
|---|---|
| Pages | ArtifactStore.tsx, CommandCenter.tsx, Packages.tsx |
| Components | NodeDetailsModal, Layout, Topbar, Sidebar, MetricCard, ExecutionsTable, LogInspector, InfoHint |
| Services | realtime.ts, 15+ API functions in api.ts |
| Utilities | zip-preview.ts |

---

## Recommended Priority Order

1. **Fix 7 failing orchestrator unit tests** (C1) — broken tests erode confidence
2. **Remove dead SignalR test infrastructure** (C2) — eliminates confusion
3. **Add missing contract serialization tests** (H6) — contract mismatches cause silent runtime failures
4. **Standardize test framework per project** (C4) — pick NUnit or xUnit, not both
5. **Fix NodeHeartbeatMonitorServiceTests** (H1) — currently tests nothing
6. **Add WorkloadPreCheck/PreCheckProbe unit tests** (H5) — critical untested pipeline steps
7. **Merge duplicate DiffEngineTests** (H4) — straightforward cleanup
8. **Migrate frontend to userEvent** (C3) — systematic improvement
9. **Expand Workloads.test.tsx** (H7) — 2 tests for 868 lines is inadequate
10. **Add MemoryRouter wrapper utility** (M6) — small investment, big fragility reduction