# Test Audit Remediation — Implementation Plan

**Source:** `docs/reports/20260515-test-audit-report.md`
**Date:** 2026-05-15
**Total Findings:** 4 Critical, 9 High, 8 Medium, 10 Low

---

## Dependency Graph

```
TICKET-01 (fix 7 failing tests) ← no blockers, START HERE
TICKET-02 (remove dead SignalR infra) ← no blockers, START HERE
TICKET-03 (add contract serialization tests) ← no blockers
TICKET-04 (standardize agent test framework) ← no blockers
TICKET-05 (rewrite heartbeat monitor tests) ← no blockers
TICKET-06 (add WorkloadPreCheck/PreCheckProbe tests) ← TICKET-04 must complete first (framework choice)
TICKET-07 (merge duplicate DiffEngineTests) ← TICKET-04 must complete first (framework choice)
TICKET-08 (migrate frontend to userEvent) ← no blockers
TICKET-09 (expand Workloads.test.tsx) ← TICKET-08 should complete first (testing pattern)
TICKET-10 (add MemoryRouter wrapper) ← no blockers, but do before TICKET-09
TICKET-11 (fix InstallControllerTests) ← no blockers
TICKET-12 (add orchestrator coverage gaps) ← no blockers
TICKET-13 (fix agent integration tests naming) ← no blockers
TICKET-14 (standardize DB test patterns) ← no blockers
TICKET-15 (remove seed data duplication) ← TICKET-01 must complete first (tests must pass before refactor)
TICKET-16 (eliminate reflection-based private method testing) ← no blockers
TICKET-17 (add accessibility testing) ← TICKET-08 should complete first
TICKET-18 (fix frontend CSS class assertions) ← no blockers
TICKET-19 (fix Dashboard mock bypass) ← no blockers
TICKET-20 (fix low-priority items) ← no blockers
```

---

## TICKET-01: Fix 7 Failing Orchestrator Unit Tests

**Priority:** P0-Critical
**Finding:** C1
**Blocked by:** None — can start immediately
**Blocks:** TICKET-15 (seed data refactor depends on tests passing first)

### Context

`WorkloadRunsControllerCurrentPackagesTests.cs` has 7 failing tests. The controller delegates to `WorkloadRunDispatcher.DispatchAsync()` but the tests mock `IHubContext<AgentRuntimeHub>` and assert on `SendCoreAsync` call captures — which the dispatcher no longer uses. The captured envelopes list stays empty, causing `Assert.That(count, ...)` and indexer failures.

Additionally, 2 of the 7 tests (`downgrade` and `conflict` scenarios) use `dynamic` binding on anonymous types, which adds type-safety risk.

### Current Broken Code

```csharp
// tests/orchestrator/unit/WorkloadRunsControllerCurrentPackagesTests.cs
// Lines 50-66: Mock setup captures SendCoreAsync calls
_capturedEnvelopes = new List<MessageEnvelope>();
_clientProxyMock = new Mock<IClientProxy>();
_clientProxyMock
    .Setup(p => p.SendCoreAsync("AssignRun", It.IsAny<object?[]?>(), It.IsAny<CancellationToken>()))
    .Callback<string, object?[]?, CancellationToken>((_, args, _) =>
    {
        if (args is not null && args.Length > 0 && args[0] is MessageEnvelope env)
        {
            _capturedEnvelopes.Add(env);
        }
    })
    .Returns(Task.CompletedTask);
```

The tests then assert on `_capturedEnvelopes[0]` or `_capturedEnvelopes.Count` — which is always 0/empty because the dispatcher doesn't call `SendCoreAsync`.

### Implementation Steps

1. **Read `WorkloadRunDispatcher`** to understand the actual dispatch path:
   - `apps/orchestrator/backend/Services/WorkloadRunDispatcher.cs`
   - Identify which method/property the dispatcher uses to send run assignments

2. **Determine the correct assertion target.** The dispatcher likely calls the DB directly or uses a different service interface. Update mock setup to intercept the actual dispatch mechanism — likely one of:
   - A repository/service call that can be mocked via `Mock<IWorkloadRunDispatcher>` or by mocking the DB context's `SaveChanges` + querying the resulting entities
   - If `DispatchAsync` writes to the DB and the tests already use `SqliteConnection :memory:`, assert on DB state instead of SignalR call captures

3. **Replace `IHubContext<AgentRuntimeHub>` mock** in these tests with whatever dispatch mechanism the controller actually uses. Remove the `_capturedEnvelopes` list and `_clientProxyMock`/`_hubContextMock` fields entirely.

4. **Rewrite the 5 `Current*` test assertions** to verify the dispatched workload run state in the DB (e.g., asserted `WorkloadRunEntity` records have correct `Status`, `NodeId`, package assignments).

5. **Fix the 2 downgrade/conflict tests** that use `dynamic`:
   - Replace `dynamic` anonymous type access with strongly-typed model classes
   - Use `JsonSerializer.Deserialize<T>()` with explicit types, or assert on `JsonElement` properties

6. **Remove `using Microsoft.AspNetCore.SignalR;`** import if no longer needed.

7. **Run tests:** `dotnet test tests/orchestrator/unit --filter "FullyQualifiedName~CurrentPackages"`. All 7 must pass.

### Verification

- `dotnet test tests/orchestrator/unit` — all 176 tests pass (0 failures)
- No `IHubContext<AgentRuntimeHub>` references remain in `WorkloadRunsControllerCurrentPackagesTests.cs`

---

## TICKET-02: Remove Dead SignalR Test Infrastructure

**Priority:** P0-Critical
**Finding:** C2
**Blocked by:** None — can start immediately
**Blocks:** None

### Context

Agent's `AgentRuntimeServiceTests.cs` has 3 tests marked `[Ignore("SignalR replaced by HTTP polling")]`. The `FakeHubConnection.cs` and `FakeHubConnectionFactory.cs` test helpers are only used by these skipped tests and the active HTTP polling tests (which construct `FakeHubConnection` unnecessarily to satisfy constructor parameters).

### Implementation Steps

1. **Remove the 3 `[Ignore]` tests** from `tests/agent/unit/AgentRuntimeServiceTests.cs`:
   - `ExecuteAsync_SendsLeaseHeartbeat_WithCorrectEnvelopeFields` (line ~18)
   - `ExecuteAsync_Reconnect_RaisesIdentify_AfterReconnectedEvent` (line ~69)
   - `ExecuteAsync_LogsReconnecting_WhenReconnectingEventFires` (line ~156)

2. **Delete `tests/agent/unit/FakeHubConnection.cs`** and `tests/agent/unit/FakeHubConnectionFactory.cs`.

3. **Update active tests that still reference `FakeHubConnection`/`FakeHubConnectionFactory`:**
   - The remaining 5 tests in `AgentRuntimeServiceTests.cs` (lines 116-788) all construct `FakeHubConnection` and pass it via `FakeHubConnectionFactory`
   - Since `AgentRuntimeService` no longer uses SignalR, check if `AgentRuntimeService` constructor still accepts `IHubConnectionFactory` — if so, either:
     - Replace with a no-op or mock `IHubConnectionFactory` if the parameter is still required
     - Remove the parameter entirely if the agent's SignalR dependency has been deleted from the production code

4. **Check if `AgentRuntimeService.cs` still references SignalR:**
   - If the constructor takes `IHubConnectionFactory`, and that interface is only used for SignalR, remove it from the constructor and all callers/injectors
   - Remove `using Microsoft.AspNetCore.SignalR.Client;` from test file

5. **Run agent unit tests:** `dotnet test tests/agent/unit`

### Verification

- No `[Ignore]` tests remain in `AgentRuntimeServiceTests.cs`
- `FakeHubConnection.cs` and `FakeHubConnectionFactory.cs` are deleted
- `dotnet test tests/agent/unit` — all tests pass, 3 fewer tests than before (the ignored ones)
- No `FakeHubConnection` references in test code

---

## TICKET-03: Add Missing Contract Serialization Tests

**Priority:** P1-High
**Finding:** H6
**Blocked by:** None
**Blocks:** None

### Context

Only `AssignRunPayload` and `PackageAssignment` have contract tests. The following shared DTOs have zero round-trip serialization tests: `PendingWorkloadRunResponse`, `FinalizationPayload`, `StepStatusPayload`, `DetectRequest`/`DetectResponse`, `InstallAdapterConfig`, `DetectionConfig`, `MessageTypes`, `WorkloadAssignmentStatus`.

Contract mismatches between orchestrator and agent cause silent runtime failures. Serialization round-trip tests catch these early.

### Implementation Steps

1. **Create test file** `tests/contracts/ContractRoundTripTests.cs` (or add to existing test class).

2. **For each untested DTO**, add a test that:
   - Constructs an instance with all fields populated (including nullable/optional ones)
   - Serializes to JSON via `System.Text.Json.JsonSerializer`
   - Deserializes back
   - Asserts all fields match the original

3. **Pattern to follow (from existing `AssignRunPayloadTests.cs`):**

```csharp
[Test]
public void PendingWorkloadRunResponse_RoundTrips_ThroughJson()
{
    var original = new PendingWorkloadRunResponse
    {
        RunId = Guid.NewGuid(),
        WorkloadId = Guid.NewGuid(),
        WorkloadName = "TestWorkload",
        Mode = "Install",
        Packages = [new PackageAssignment { Name = "pkg1", Version = "1.0" }],
        CurrentPackages = []
    };

    var json = JsonSerializer.Serialize(original);
    var deserialized = JsonSerializer.Deserialize<PendingWorkloadRunResponse>(json);

    Assert.That(deserialized!.RunId, Is.EqualTo(original.RunId));
    Assert.That(deserialized.WorkloadName, Is.EqualTo(original.WorkloadName));
    // ... assert all fields
}
```

4. **DTOs to test** (one test method per DTO):
   - `PendingWorkloadRunResponse` — all fields
   - `FinalizationPayload` — all fields
   - `StepStatusPayload` — all fields
   - `DetectRequest` — all fields including detection configs
   - `DetectResponse` — all fields including per-package results
   - `InstallAdapterConfig` — command, arguments, uninstall commands, timeout, upgrade behavior, expected exit codes
   - `DetectionConfig` — type, path, registry key patterns, version manifest config
   - `MessageTypes` — verify constant values match expected strings
   - `WorkloadAssignmentStatus` — enum values

5. **Use the project's `JsonSerializerOptions`** if contracts have a shared options instance (check `Contracts/` for `JsonSerializerDefaults` or similar). Replicating the same options ensures test fidelity.

6. **Run contract tests:** `dotnet test tests/contracts`

### Verification

- `dotnet test tests/contracts` — all tests pass
- Each untested DTO now has at least one round-trip test
- Total contract test count increases by ~9+ tests

---

## TICKET-04: Standardize Agent Test Framework

**Priority:** P1-High
**Finding:** C4
**Blocked by:** None
**Blocks:** TICKET-06, TICKET-07 (framework choice must be settled before writing new tests)

### Context

Agent unit tests mix NUnit `[Test]` and xUnit `[Fact]` attributes. `tests/agent/unit/DiffEngineTests.cs` uses xUnit, while `tests/agent/unit/AgentRuntimeServiceTests.cs` uses NUnit. `tests/agent/integration/PipelineExecutorTests.cs` mixes both in the same file.

### Implementation Steps

1. **Decide on framework:** Based on the orchestrator tests consistently using NUnit + Moq, and the agent integration tests also using NUnit, **standardize on NUnit** for all agent tests.

2. **Audit all agent test files** for `[Fact]` / `[Theory]` / xUnit `Assert.*` usage:
   - `tests/agent/unit/DiffEngineTests.cs` — uses `[Fact]`, xUnit `Assert`
   - `tests/agent/integration/PipelineExecutorTests.cs` — mixed
   - Any other files with `[Fact]`

3. **Convert each xUnit test file** to NUnit:
   - `[Fact]` → `[Test]`
   - `[Theory]` → `[TestCase]` (inline data) or `[TestCaseSource]`
   - `Assert.Single(collection)` → `Assert.That(collection, Has.Count.EqualTo(1))`
   - `Assert.Equal(expected, actual)` → `Assert.That(actual, Is.EqualTo(expected))`
   - `Assert.True(condition)` → `Assert.That(condition, Is.True)`
   - Add `using NUnit.Framework;`, remove `using Xunit;`

4. **Update `tests/agent/unit/Agent.Tests.csproj`** — remove xUnit package reference if it exists (check `<PackageReference Include="xunit"` and remove it). Ensure `NUnit3TestAdapter` and `Microsoft.NET.Test.Sdk` are present.

5. **Run all agent tests:** `dotnet test tests/agent/unit && dotnet test tests/agent/integration`

### Verification

- `grep -r "\[Fact\]" tests/agent/` returns nothing
- `grep -r "using Xunit" tests/agent/` returns nothing
- All agent test files use `using NUnit.Framework;`
- `dotnet test tests/agent/unit && dotnet test tests/agent/integration` — all pass

---

## TICKET-05: Rewrite NodeHeartbeatMonitorServiceTests

**Priority:** P1-High
**Finding:** H1
**Blocked by:** None
**Blocks:** None

### Context

All 3 tests in `NodeHeartbeatMonitorServiceTests.cs` use `Assert.DoesNotThrowAsync` — they verify no exception but never check that `ScanAsync` actually transitions stale nodes from Online→Offline. The core behavioral logic is completely untested.

### Current Broken Code

```csharp
// tests/orchestrator/unit/Services/NodeHeartbeatMonitorServiceTests.cs:44-61
[Test]
public async Task ScanAsync_DetectsStaleNodes()
{
    // Seeds a stale node, then:
    var service = CreateService();
    Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));
    // Never asserts node.Status == "Offline"!
}
```

The production code at `NodeHeartbeatMonitorService.cs:29-49` clearly sets `node.Status = "Offline"` for stale nodes and calls `SaveChangesAsync`.

### Implementation Steps

1. **Rewrite `ScanAsync_DetectsStaleNodes`** to verify behavioral outcome:

```csharp
[Test]
public async Task ScanAsync_DetectsStaleNodes_TransitionsToOffline()
{
    using var scope = _serviceProvider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

    var staleNode = new NodeEntity
    {
        NodeId = Guid.NewGuid(),
        Hostname = "stale",
        Status = "Online",
        LastSeenUtc = DateTime.UtcNow.AddMinutes(-5)
    };
    db.Nodes.Add(staleNode);
    await db.SaveChangesAsync();

    var service = CreateService();
    await service.ScanAsync(CancellationToken.None);

    var refreshed = await db.Nodes.FindAsync(staleNode.NodeId);
    Assert.That(refreshed!.Status, Is.EqualTo("Offline"));
}
```

2. **Rewrite `ScanAsync_DoesNotAffectFreshOnlineNodes`** to verify fresh node stays Online:

```csharp
[Test]
public async Task ScanAsync_LeavesFreshOnlineNodes_Online()
{
    using var scope = _serviceProvider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

    var freshNode = new NodeEntity
    {
        NodeId = Guid.NewGuid(),
        Hostname = "fresh",
        Status = "Online",
        LastSeenUtc = DateTime.UtcNow.AddSeconds(-30)
    };
    db.Nodes.Add(freshNode);
    await db.SaveChangesAsync();

    var service = CreateService();
    await service.ScanAsync(CancellationToken.None);

    var refreshed = await db.Nodes.FindAsync(freshNode.NodeId);
    Assert.That(refreshed!.Status, Is.EqualTo("Online"));
}
```

3. **Rewrite `ScanAsync_DoesNotAffectAlreadyOfflineNodes`** to verify offline nodes stay Offline and LastSeenUtc is preserved:

```csharp
[Test]
public async Task ScanAsync_DoesNotReprocessAlreadyOfflineNodes()
{
    using var scope = _serviceProvider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

    var offlineNode = new NodeEntity
    {
        NodeId = Guid.NewGuid(),
        Hostname = "offline",
        Status = "Offline",
        LastSeenUtc = DateTime.UtcNow.AddMinutes(-5)
    };
    db.Nodes.Add(offlineNode);
    await db.SaveChangesAsync();
    var originalLastSeen = offlineNode.LastSeenUtc;

    var service = CreateService();
    await service.ScanAsync(CancellationToken.None);

    var refreshed = await db.Nodes.FindAsync(offlineNode.NodeId);
    Assert.That(refreshed!.Status, Is.EqualTo("Offline"));
    Assert.That(refreshed.LastSeenUtc, Is.EqualTo(originalLastSeen));
}
```

4. **Add a mixed scenario test** — 2 stale + 1 fresh, verify only both stale go Offline:

```csharp
[Test]
public async Task ScanAsync_BulkTransition_MultipleStaleNodes()
{
    using var scope = _serviceProvider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

    var stale1 = new NodeEntity { NodeId = Guid.NewGuid(), Hostname = "stale1", Status = "Online", LastSeenUtc = DateTime.UtcNow.AddMinutes(-10) };
    var stale2 = new NodeEntity { NodeId = Guid.NewGuid(), Hostname = "stale2", Status = "Online", LastSeenUtc = DateTime.UtcNow.AddMinutes(-3) };
    var fresh = new NodeEntity { NodeId = Guid.NewGuid(), Hostname = "fresh", Status = "Online", LastSeenUtc = DateTime.UtcNow.AddSeconds(-30) };
    db.Nodes.AddRange(stale1, stale2, fresh);
    await db.SaveChangesAsync();

    var service = CreateService();
    await service.ScanAsync(CancellationToken.None);

    Assert.That((await db.Nodes.FindAsync(stale1.NodeId))!.Status, Is.EqualTo("Offline"));
    Assert.That((await db.Nodes.FindAsync(stale2.NodeId))!.Status, Is.EqualTo("Offline"));
    Assert.That((await db.Nodes.FindAsync(fresh.NodeId))!.Status, Is.EqualTo("Online"));
}
```

5. **Run tests:** `dotnet test tests/orchestrator/unit --filter "NodeHeartbeatMonitor"`

### Verification

- No `Assert.DoesNotThrowAsync` calls remain in `NodeHeartbeatMonitorServiceTests.cs`
- All assertions verify actual DB state transitions
- `dotnet test tests/orchestrator/unit --filter "NodeHeartbeatMonitor"` — all pass

---

## TICKET-06: Add WorkloadPreCheck and PreCheckProbe Unit Tests

**Priority:** P1-High
**Finding:** H5
**Blocked by:** TICKET-04 (framework must be NUnit before writing new tests)
**Blocks:** None

### Context

`WorkloadPreCheck.cs` and `PreCheckProbe.cs` are critical agent pipeline steps with zero test coverage. `PreCheckProbe` makes HTTP calls to the orchestrator's `/api/detect` endpoint. `WorkloadPreCheck` orchestrates pre-check workflow.

### Implementation Steps

1. **Create `tests/agent/unit/Steps/WorkloadPreCheckTests.cs`:**
   - Test that `ExecuteAsync` returns `PreCheckResult` with correct statuses
   - Test with empty package list
   - Test with packages that have detection configs
   - Test error handling (network failure, timeout, malformed response)

2. **Create `tests/agent/unit/Steps/PreCheckProbeTests.cs`:**
   - Test HTTP POST to orchestrator `/api/detect`
   - Mock `HttpClient` via `MockHttpMessageHandler` pattern
   - Test successful detection response parsing
   - Test failed detection (non-2xx status)
   - Test timeout behavior

3. **Create `tests/agent/unit/Pipeline/InitStepEnvVarsTests.cs`:**
   - Test environment variable expansion
   - Test missing variable handling
   - Test shell command interpolation

4. **Use NUnit `[Test]` attributes** (per TICKET-04 outcome).

5. **Run:** `dotnet test tests/agent/unit`

### Verification

- 3 new test files created
- Each file has ≥3 test methods
- `dotnet test tests/agent/unit` — all pass

---

## TICKET-07: Merge Duplicate DiffEngineTests

**Priority:** P1-High
**Finding:** H4
**Blocked by:** TICKET-04 (framework must be standardized first)
**Blocks:** None

### Context

Two files test the same `DiffEngine.ComputeDiff()` method:
- `tests/agent/unit/DiffEngineTests.cs` — 9 tests, basic scenarios, uses xUnit `[Fact]`
- `tests/agent/unit/Pipeline/DiffEngineTests.cs` — 7 tests, advanced pre-check scenarios, uses xUnit `[Fact]`

The advanced file additionally imports `DeploymentPoC.Agent.Steps` for `PreCheckResult`/`PreCheckStatus`.

### Implementation Steps

1. **After TICKET-04 completes**, both files will use NUnit. At that point:

2. **Merge into `tests/agent/unit/Pipeline/DiffEngineTests.cs`** (the more comprehensive location):
   - Move all 9 tests from `DiffEngineTests.cs` (root) into `Pipeline/DiffEngineTests.cs`
   - Add `using DeploymentPoC.Agent.Steps;` if not already present (needed for `PreCheckResult`)
   - Organize test methods: basic scenarios first, then pre-check override scenarios

3. **Add `[TestFixture]` attribute** to the merged class.

4. **Delete `tests/agent/unit/DiffEngineTests.cs`** (the root-level duplicate).

5. **Run:** `dotnet test tests/agent/unit --filter "DiffEngine"`

### Verification

- Only one `DiffEngineTests.cs` file exists: `tests/agent/unit/Pipeline/DiffEngineTests.cs`
- Total test count for DiffEngine = 16 (9 basic + 7 advanced)
- All 16 tests pass

---

## TICKET-08: Migrate Frontend Tests to userEvent

**Priority:** P1-High
**Finding:** C3
**Blocked by:** None
**Blocks:** TICKET-09, TICKET-17 (testing pattern upgrade)

### Context

All 6 frontend component test files use `fireEvent.click()` from `@testing-library/react`. This skips the focus/blur/change event sequence that real browsers perform, meaning tests pass even when components break for real users (e.g., form validation that depends on blur events).

### Implementation Steps

1. **Install `@testing-library/user-event`:**

```bash
cd apps/orchestrator/web
pnpm add -D @testing-library/user-event
```

2. **Update each test file** (6 files):

| File | Action |
|------|--------|
| `src/pages/Dashboard.test.tsx` | Replace `fireEvent.click` → `userEvent.click` |
| `src/pages/Nodes.test.tsx` | Replace `fireEvent.click` → `userEvent.click` |
| `src/pages/Workloads.test.tsx` | Replace `fireEvent.click` → `userEvent.click` |
| `src/pages/WorkloadRuns.test.tsx` | Replace `fireEvent.click` → `userEvent.click` |
| `src/pages/Install.test.tsx` | Replace `fireEvent.click` → `userEvent.click` |
| `src/services/api.test.ts` | No changes needed (not component test) |

3. **Pattern for migration:**

Before:
```tsx
import { fireEvent, render, screen } from '@testing-library/react'

// In test:
fireEvent.click(screen.getByRole('button', { name: /submit/i }))
```

After:
```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// In test:
const user = userEvent.setup()
await user.click(screen.getByRole('button', { name: /submit/i }))
```

4. **Make all affected test functions `async`** — `userEvent` interactions are async. Add `await` before each `user.click()`, `user.type()`, `user.selectOptions()`, etc.

5. **Remove `fireEvent` imports** where fully replaced. Keep `fireEvent` only if any test uses non-interactive events (e.g., `fireEvent.focus()` for edge cases not covered by userEvent).

6. **Run frontend tests:** `cd apps/orchestrator/web && pnpm test`

7. **Check for new test failures** caused by the more realistic event sequencing — these indicate real bugs that `fireEvent` was hiding.

### Verification

- `grep -r "fireEvent" apps/orchestrator/web/src/ --include="*.test.*"` returns zero or only legitimate edge-case uses
- All `userEvent` calls are `await`ed
- `pnpm test` — all pass

---

## TICKET-09: Expand Workloads.test.tsx Coverage

**Priority:** P1-High
**Finding:** H7
**Blocked by:** TICKET-08 (should use userEvent pattern) and TICKET-10 (MemoryRouter wrapper)
**Blocks:** None

### Context

`Workloads.test.tsx` has only 2 tests for an 868-line component. Missing: create/edit revision, publish, delete, bulk import, error states.

### Implementation Steps

1. **Read the `Workloads.tsx` component** to identify all user-facing features and API calls.

2. **Add `MemoryRouter` wrapper** (per TICKET-10 pattern) — the component will break without it if any child uses router hooks.

3. **Write tests for each major feature:**

```tsx
describe('Workloads page', () => {
  it('creates a new revision', async () => {
    const user = userEvent.setup()
    render(<Workloads />, { wrapper: TestRouterWrapper })
    await user.click(await screen.findByRole('button', { name: /create revision/i }))
    // ... fill form, submit
    expect(api.createWorkloadRevision).toHaveBeenCalled()
  })

  it('publishes a draft revision', async () => { /* ... */ })
  it('deletes a workload', async () => { /* ... */ })
  it('imports workloads from JSON', async () => { /* ... */ })
  it('displays error state when API fails', async () => { /* ... */ })
  it('shows workload detail when row is clicked', async () => { /* ... */ })
})
```

4. **Mock API functions** that these interactions trigger — ensure all new API calls are in the mock.

5. **Test error states** — mock API to reject, verify error message rendering.

6. **Run:** `cd apps/orchestrator/web && pnpm test -- Workloads`

### Verification

- `Workloads.test.tsx` has ≥8 test cases (from 2)
- All major UI interactions covered
- `pnpm test -- Workloads` — all pass

---

## TICKET-10: Add MemoryRouter Wrapper to Frontend Page Tests

**Priority:** P2-Medium
**Finding:** M6
**Blocked by:** None
**Blocks:** TICKET-09 (should be done before expanding Workloads tests)

### Context

4 of 5 page tests render without `<MemoryRouter>`, which works now but breaks if any child adds a router hook. Files: `WorkloadRuns.test.tsx`, `Workloads.test.tsx`, `Install.test.tsx`, `Nodes.test.tsx`.

### Implementation Steps

1. **Create a shared test utility** at `apps/orchestrator/web/src/test-utils/TestRouterWrapper.tsx`:

```tsx
import { BrowserRouter, MemoryRouter } from 'react-router-dom'
import { ReactNode } from 'react'

export function TestRouterWrapper({ children }: { children: ReactNode }) {
  return (
    <MemoryRouter>
      {children}
    </MemoryRouter>
  )
}
```

2. **Update each of the 4 page test files** to use the wrapper:

```tsx
import { render, screen } from '@testing-library/react'
import { TestRouterWrapper } from '../test-utils/TestRouterWrapper'

// Before:
render(<Workloads />)

// After:
render(<Workloads />, { wrapper: TestRouterWrapper })
```

3. **Run:** `cd apps/orchestrator/web && pnpm test`

### Verification

- All 4 page tests now render within `MemoryRouter`
- `TestRouterWrapper` utility created and shared
- All existing tests still pass

---

## TICKET-11: Fix InstallControllerTests — Weak Assertions & Legacy Pipeline

**Priority:** P2-Medium
**Finding:** H2
**Blocked by:** None
**Blocks:** None

### Context

`InstallControllerTests.cs` has 2 tests that only check for `OkObjectResult`. The "error" test should return a non-200 status but doesn't. The `IPipeline<InstallContext>` architecture is documented as legacy.

### Implementation Steps

1. **Read `InstallController.cs`** to understand current behavior.

2. **Determine if `InstallController` is still active** or superseded by `WorkloadRunsController`. If legacy, consider:
   - Adding `[Obsolete]` attribute to the controller
   - Or removing the tests entirely with a comment about the legacy code

3. **If keeping the tests**, fix assertions:
   - Success test: assert on response object shape (not just `OkObjectResult`)
   - Error test: assert on non-200 status code (e.g., `Assert.That(result, Is.InstanceOf<BadRequestObjectResult>())`, or fix the controller to return the correct error code)

4. **Run:** `dotnet test tests/orchestrator/unit --filter "InstallController"`

### Verification

- No `Assert.DoesNotThrow` or weak `OkObjectResult`-only assertions remain
- Error test actually validates error response

---

## TICKET-12: Add Orchestrator Coverage Gaps

**Priority:** P2-Medium
**Finding:** H3 (controllers/services with 0 tests)
**Blocked by:** None
**Blocks:** None

### Context

Zero unit test coverage for: `EnrollmentController`, `PackagesController`, most `WorkloadsController`/`WorkloadRunsController`/`NodesController` endpoints, `UploadSessionService`, `ArtifactZipService`, `PolicyEvaluationService`.

### Implementation Steps

This is a large ticket. Break into sub-issues per controller/service if needed.

1. **EnrollmentController** — test token issuance, validation, expiry, single-use enforcement
2. **PackagesController** — test CRUD endpoints, artifact listing, package listing
3. **WorkloadRunsController** — test GET pending, GET by ID, PATCH status, DELETE
4. **NodesController** — test GET all, GET by ID, PUT update display name, DELETE
5. **UploadSessionService** — test chunked upload sessions (create, upload chunk, complete, resume)
6. **ArtifactZipService** — test zip extraction, manifest pairing, validation
7. **PolicyEvaluationService** — test policy rules evaluation

For each, follow the existing project patterns (NUnit, Moq, SqliteConnection :memory: for DB-dependent tests).

### Verification

- Each controller/service has ≥1 test file
- `dotnet test tests/orchestrator/unit` — all pass

---

## TICKET-13: Fix Agent Integration Test Naming & Real Server Testing

**Priority:** P2-Medium
**Finding:** M5
**Blocked by:** None
**Blocks:** None

### Context

Agent "integration" tests construct services directly but never start Kestrel. They test `/api/detect` and `/health` endpoints via direct method calls, not HTTP requests. These are component tests, not integration tests.

### Implementation Steps

1. **Rename current integration test project** (or its test class names) to reflect that they are component tests, not true integration tests.

2. **Add `WebApplicationFactory<Program>`-based tests** for true integration testing of the agent's Kestrel endpoints:

```csharp
// tests/agent/integration/AgentApiIntegrationTests.cs
[TestFixture]
public class AgentApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AgentApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Test]
    public async Task DetectEndpoint_ReturnsExpectedResponse()
    {
        var response = await _client.PostAsync("/api/detect", JsonContent.Create(detectRequest));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
```

3. **Add platform guard** for tests that only work on Windows:

```csharp
[SetUp]
public void SkipOnNonWindows()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        Assert.Ignore("Agent integration tests require Windows");
}
```

### Verification

- Kestrel starts in tests and responds to HTTP requests
- `dotnet test tests/agent/integration` — passes on Windows

---

## TICKET-14: Standardize DB Test Patterns

**Priority:** P2-Medium
**Finding:** M1
**Blocked by:** None
**Blocks:** None

### Context

`WorkloadImportServiceTests` and `WorkloadImportServiceMappingTests` use `InMemoryDatabase` (no constraint enforcement) while all other test files use `SqliteConnection :memory:`. InMemoryDatabase silently allows FK violations and missing required fields.

### Implementation Steps

1. **Read both test files** to understand their setup patterns.

2. **Convert `InMemoryDatabase` usages to `SqliteConnection :memory:`** following the project's established pattern:

```csharp
// Before:
var options = new DbContextOptionsBuilder<InstallerDbContext>()
    .UseInMemoryDatabase("test-db")
    .Options;

// After:
private SqliteConnection _connection = null!;

[SetUp]
public void SetUp()
{
    _connection = new SqliteConnection("Data Source=:memory:");
    _connection.Open();
    var options = new DbContextOptionsBuilder<InstallerDbContext>()
        .UseSqlite(_connection)
        .Options;
    _db = new InstallerDbContext(options);
    _db.Database.EnsureCreated();
}

[TearDown]
public void TearDown()
{
    _db.Dispose();
    _connection.Dispose();
}
```

3. **Run tests after conversion** — some may reveal previously masked constraint violations. Fix any that do.

### Verification

- `grep -r "UseInMemoryDatabase" tests/orchestrator/` returns nothing
- All orchestrator unit tests pass with Sqlite

---

## TICKET-15: Reduce Seed Data Duplication in CurrentPackages Tests

**Priority:** P2-Medium
**Finding:** M2
**Blocked by:** TICKET-01 (tests must pass before refactoring)
**Blocks:** None

### Context

Each test in `WorkloadRunsControllerCurrentPackagesTests.cs` seeds 30-80 lines of nearly identical DB setup. Extracting a shared builder would reduce line count by ~60%.

### Implementation Steps

1. **After TICKET-01 passes**, create `tests/orchestrator/unit/TestSeedBuilder.cs`:

```csharp
public class TestSeedBuilder
{
    private readonly InstallerDbContext _db;
    private readonly List<NodeEntity> _nodes = [];
    private readonly List<WorkloadDefinitionEntity> _workloads = [];
    private readonly List<WorkloadRevisionEntity> _revisions = [];
    private readonly List<PackageEntity> _packages = [];

    public TestSeedBuilder(InstallerDbContext db) => _db = db;

    public TestSeedBuilder WithNode(Guid nodeId, string hostname = "test-node", string status = "Online")
    {
        _nodes.Add(new NodeEntity { NodeId = nodeId, Hostname = hostname, Status = status, LastSeenUtc = DateTime.UtcNow });
        return this;
    }

    public TestSeedBuilder WithWorkload(Guid workloadId, string name, string description = "")
    {
        _workloads.Add(new WorkloadDefinitionEntity { Id = workloadId, Name = name, Description = description });
        return this;
    }

    // ... WithRevision, WithPackage, etc.

    public async Task SeedAsync()
    {
        _db.Nodes.AddRange(_nodes);
        _db.WorkloadDefinitions.AddRange(_workloads);
        _db.WorkloadRevisions.AddRange(_revisions);
        _db.Packages.AddRange(_packages);
        await _db.SaveChangesAsync();
    }
}
```

2. **Refactor each test** to use the builder:

```csharp
var seed = new TestSeedBuilder(_db)
    .WithNode(nodeId)
    .WithWorkload(workloadId, "BaseInstall")
    .WithRevision(revisionId, workloadId, "1.0.0", published: true)
    .WithPackage(pkg1Id, revisionId, "EJ-Installer", "2.0.0", index: 0);
await seed.SeedAsync();
```

3. **Run:** `dotnet test tests/orchestrator/unit --filter "CurrentPackages"`

### Verification

- File line count reduced by ≥50%
- All CurrentPackages tests still pass

---

## TICKET-16: Eliminate Reflection-Based Private Method Testing

**Priority:** P2-Medium
**Finding:** M3
**Blocked by:** None
**Blocks:** None

### Context

`WorkloadImportServiceTests.cs:286-305` uses reflection to call private method `WorkloadRunDispatcher.DeserializeStringList`. Tests implementation details with no compile-time safety.

### Implementation Steps

1. **If `DeserializeStringList` should be testable**, extract it to a public utility class or make it `internal` with `[InternalsVisibleTo]`.

2. **If it's purely internal**, rewrite tests to exercise the public API that calls `DeserializeStringList` instead.

3. **Remove reflection code** from tests.

### Verification

- No `typeof(...).GetMethod(...)` in test files
- `dotnet test tests/orchestrator/unit` — all pass

---

## TICKET-17: Add Accessibility Testing

**Priority:** P2-Medium
**Finding:** H9
**Blocked by:** TICKET-08 (should use userEvent pattern first)
**Blocks:** None

### Context

Zero frontend tests use `jest-axe` or verify ARIA patterns beyond incidental `getByRole`.

### Implementation Steps

1. **Install `jest-axe`:**

```bash
cd apps/orchestrator/web
pnpm add -D jest-axe
```

Note: Since the project uses Vitest, install the vitest-compatible version or use `@axe-core/playwright` for E2E accessibility testing.

2. **Create `src/test-utils/axeUtils.ts`:**

```tsx
import { axe } from 'jest-axe'

export async function expectNoA11yViolations(container: HTMLElement) {
  const results = await axe(container)
  expect(results).toHaveNoViolations()
}
```

3. **Add accessibility assertions to each page test:**

```tsx
import { expectNoA11yViolations } from '../test-utils/axeUtils'

it('has no accessibility violations', async () => {
  const { container } = render(<Nodes />, { wrapper: TestRouterWrapper })
  await expectNoA11yViolations(container)
})
```

4. **Run:** `pnpm test`

### Verification

- `jest-axe` installed and configured for Vitest
- Each page test has ≥1 accessibility assertion
- No critical/serious violations reported

---

## TICKET-18: Fix Frontend CSS Class Assertions

**Priority:** P2-Medium
**Finding:** M7
**Blocked by:** None
**Blocks:** None

### Context

`Nodes.test.tsx` asserts on `bg-emerald-*`, `bg-slate-*` Tailwind classes. These are implementation details that break when styles change.

### Implementation Steps

1. **Find all CSS class assertions** in `Nodes.test.tsx`:

```bash
grep -n "bg-\|text-\|border-" apps/orchestrator/web/src/pages/Nodes.test.tsx
```

2. **Replace each with a semantic query**, e.g.:

Before:
```tsx
expect(screen.getByText('Online').closest('[class]')).toHaveClass('bg-emerald-500')
```

After (option A — use ARIA roles/labels):
```tsx
expect(screen.getByRole('status', { name: /online/i })).toBeInTheDocument()
```

After (option B — use data-testid):
```tsx
expect(screen.getByTestId('node-status-online')).toBeInTheDocument()
```

3. **Add `data-testid` attributes to the component** where ARIA roles don't fit naturally.

4. **Run:** `pnpm test -- Nodes`

### Verification

- No Tailwind class assertions in `Nodes.test.tsx`
- Component has appropriate `data-testid` or ARIA attributes where needed

---

## TICKET-19: Fix Dashboard Test Mock Bypass

**Priority:** P2-Medium
**Finding:** M8
**Blocked by:** None
**Blocks:** None

### Context

`Dashboard.test.tsx` mocks `getOrchestratorHomeData` to return pre-shaped data, bypassing the 100+ line transformation logic in `api.ts:1527-1635`. Bugs in that logic won't be caught.

### Implementation Steps

1. **Read the transformation logic** in `api.ts` around lines 1527-1635 to understand what it does.

2. **Add a unit test for the transformation function itself:**

```tsx
// src/services/orchestratorHome.transform.test.ts
import { transformOrchestratorHomeData } from './api'

describe('transformOrchestratorHomeData', () => {
  it('maps raw API response to dashboard shape', () => {
    const raw = { /* raw API shape */ }
    const result = transformOrchestratorHomeData(raw)
    expect(result.totalNodes).toBe(/* expected */)
    // ... assert all transformed fields
  })

  it('handles empty arrays gracefully', () => { /* ... */ })
  it('calculates aggregate metrics correctly', () => { /* ... */ })
})
```

3. **In `Dashboard.test.tsx`**, optionally add one integration test that uses the real transformation with a raw API response fixture.

### Verification

- Transformation logic has direct unit tests
- `pnpm test — Dashboard` — all pass

---

## TICKET-20: Fix Low-Priority Items

**Priority:** P3-Low
**Finding:** All LOW findings
**Blocked by:** None
**Blocks:** None

### Items

| # | Finding | Fix |
|---|---------|-----|
| L1 | `PipelineTests.cs` has tautological test `Assert.That(true, Is.True)` | Remove or replace with meaningful assertion |
| L2 | Agent: `AcquireArtifactTests` uses reflection for private methods | Same as TICKET-16 — extract to `internal` or test via public API |
| L3 | Agent: Registry tests will fail on non-Windows | Add `[Platform("WIN")]` NUnit attribute or `Assert.Ignore` on non-Windows |
| L4 | Contracts: `AssignRunPayloadTests` doesn't test JSON round-trip | Add round-trip test (covered by TICKET-03) |
| L5 | Frontend: Unused mock `advanceWorkloadRun` in WorkloadRuns tests | Remove unused mock |
| L6 | Frontend: `realtime.ts` has no direct unit tests | Add `realtime.test.ts` for Socket.IO event handlers |
| L7 | Frontend: 15+ API functions lack direct tests | Add `api.functions.test.ts` focusing on untested functions |
| L8 | Frontend: No error boundary or loading state tests | Add `ErrorBoundary.test.tsx` and loading state tests per page |
| L9 | Orchestrator: `InstallerDbContextShapeTests` tests legacy entity shapes | Mark with `[Ignore]` or remove if legacy entities are truly dead |

### Verification

- Each item addressed individually
- All test suites still pass

---

## Execution Order Recommendation

### Phase 1 — Unstick the Build (Week 1)

| Ticket | Estimate | Parallel? |
|--------|----------|-----------|
| TICKET-01 Fix 7 failing tests | 4h | Yes |
| TICKET-02 Remove dead SignalR infra | 2h | Yes |
| TICKET-10 Add MemoryRouter wrapper | 1h | Yes |

### Phase 2 — Foundation (Week 1-2)

| Ticket | Estimate | Parallel? |
|--------|----------|-----------|
| TICKET-04 Standardize test framework | 3h | Yes (but blocks T-06, T-07) |
| TICKET-05 Rewrite heartbeat tests | 2h | Yes |
| TICKET-08 Migrate to userEvent | 3h | Yes (but blocks T-09, T-17) |
| TICKET-14 Standardize DB test patterns | 2h | Yes |

### Phase 3 — Coverage Expansion (Week 2-3)

| Ticket | Estimate | Parallel? |
|--------|----------|-----------|
| TICKET-03 Contract serialization tests | 3h | Yes |
| TICKET-06 Agent pre-check tests | 3h | Depends on T-04 |
| TICKET-07 Merge DiffEngine tests | 1h | Depends on T-04 |
| TICKET-09 Expand Workloads test | 4h | Depends on T-08, T-10 |
| TICKET-12 Orchestrator coverage gaps | 8h | Yes |
| TICKET-13 Agent integration tests | 4h | Yes |

### Phase 4 — Cleanup & Polish (Week 3-4)

| Ticket | Estimate | Parallel? |
|--------|----------|-----------|
| TICKET-11 Fix InstallControllerTests | 2h | Yes |
| TICKET-15 Reduce seed data duplication | 2h | Depends on T-01 |
| TICKET-16 Eliminate reflection testing | 1h | Yes |
| TICKET-17 Add accessibility testing | 2h | Depends on T-08 |
| TICKET-18 Fix CSS class assertions | 1h | Yes |
| TICKET-19 Fix Dashboard mock bypass | 2h | Yes |
| TICKET-20 Low-priority items | 4h | Yes |

**Total estimated effort:** ~52 hours across 4 weeks