# Test Infrastructure Fix Plan

## Context
During implementation of the workload-run audit fixes, the test-addition subagent aborted. An in-depth investigation revealed structural issues in the agent test projects that block reliable test authoring and execution. This plan captures the findings and prescribes fixes to be executed in a **dedicated follow-up session**.

---

## Findings Summary

### 1. Mixed Test Frameworks (Primary Subagent Abort Cause)
- **Integration test project** (`tests/agent/integration/`) references **both NUnit and xUnit**.
- The file `tests/agent/integration/PipelineExecutorTests.cs` contains a class `PipelineExecutorDiffTests` with **8 tests decorated with `[Xunit.Fact]`**.
- Because the project primarily uses NUnit (`NUnit3TestAdapter`), these 8 xUnit tests are **non-deterministically discovered** — they compile but may not be found by the test runner.
- The previous subagent likely attempted to add `[Fact]` tests into the integration file, saw them not run, and aborted.

### 2. Hard-Coded Linux Shell Commands
- `UninstallPackageTests` uses `true`, `false`, `sleep`, `cat` — all fail on Windows with `command_not_found`.
- 8 integration tests also rely on Linux commands or shell behaviors.
- **Result**: 8 of 61 unit tests fail on Windows; integration tests are effectively Windows-hostile.

### 3. No Cross-Platform Command Abstraction
- There is no helper like `CrossPlatformCommands.Success`, `CrossPlatformCommands.Fail`, etc.
- Every test hard-codes its executable command, making new tests fragile.

### 4. No Shared Integration-Test Builders
- Unit tests (`InitStepPipelineTests.cs`) have clean `CreateContext(...)` and `CreatePackage(...)` helpers.
- Integration tests inline **~50 lines of `PipelineContext` boilerplate** per test.
- Adding new integration tests requires copy-pasting and manually tweaking large initialization blocks — high error rate.

### 5. Integration Test Suite Times Out
- `dotnet test` on the integration project **times out at 180s** before completing.
- This makes iterative test authoring impossible for subagents.

### 6. `DOTNET_ROOT` Misconfiguration
- Environment variable points to a .NET 8.0.25-only path, ignoring .NET 10.0.7 installed under `C:\Program Files\dotnet`.
- Tests abort with "You must install or update .NET to run this application" unless the variable is cleared.

---

## Fix Plan

### Phase A: Stabilize Test Discovery (Must Do First)
**Goal**: Make every test discoverable and runnable.

#### Step A1 — Standardize Framework Attributes in Integration Tests
- **File**: `tests/agent/integration/PipelineExecutorTests.cs`
- **Change**: Convert all 8 `[Xunit.Fact]` attributes in `PipelineExecutorDiffTests` to `[Test]` (NUnit).
- **Verification**: `dotnet test tests/agent/integration` should discover all 35+ tests.
- **Time**: 10 min

#### Step A2 — Fix `DOTNET_ROOT` Workaround
- **Action**: Document (in `AGENTS.md` or a test README) that `DOTNET_ROOT` must be unset before running tests on this machine:
  ```powershell
  $env:DOTNET_ROOT=""
  dotnet test tests/agent/integration
  ```
- **Time**: 5 min

---

### Phase B: Cross-Platform Command Abstraction
**Goal**: Eliminate Windows-only test failures so tests pass on the developer workstation.

#### Step B1 — Create `CrossPlatformCommands` Helper
- **File**: `tests/agent/unit/CrossPlatformCommands.cs` (new)
- **Content**:
  ```csharp
  public static class CrossPlatformCommands
  {
      public static string Exit(int code) => OperatingSystem.IsWindows()
          ? $"cmd /C exit {code}"
          : $"exit {code}";

      public static string Echo(string msg) => OperatingSystem.IsWindows()
          ? $"cmd /C echo {msg}"
          : $"echo {msg}";

      public static string Sleep(int seconds) => OperatingSystem.IsWindows()
          ? $"powershell -Command Start-Sleep -Seconds {seconds}"
          : $"sleep {seconds}";

      public static string Touch(string path) => OperatingSystem.IsWindows()
          ? $"cmd /C type nul > \"{path}\""
          : $"touch {path}";
  }
  ```
- **Time**: 15 min

#### Step B2 — Refactor `UninstallPackageTests` to Use `CrossPlatformCommands`
- **File**: `tests/agent/unit/UninstallPackageTests.cs`
- **Change**: Replace hard-coded `true`, `false`, `sleep`, `cat` with `CrossPlatformCommands.Exit(0)`, `Exit(1)`, `Sleep(2)`, etc.
- **Verification**: `dotnet test tests/agent/unit --filter "FullyQualifiedName~UninstallPackage"` should pass on Windows.
- **Time**: 20 min

#### Step B3 — Refactor Integration Tests to Use `CrossPlatformCommands`
- **File**: `tests/agent/integration/PipelineExecutorTests.cs`
- **Change**: Replace any Linux-specific commands in the 8 integration tests with `CrossPlatformCommands` equivalents.
- **Verification**: `dotnet test tests/agent/integration --filter "FullyQualifiedName~PipelineExecutor"` should pass (or at least not fail on `command_not_found`).
- **Time**: 20 min

---

### Phase C: Shared Test Builders
**Goal**: Reduce boilerplate so new integration tests can be written in <10 lines.

#### Step C1 — Extract `PipelineContextBuilder` to Test Helpers
- **File**: `tests/agent/integration/PipelineContextBuilder.cs` (new)
- **Approach**: Port the `CreateContext` and `CreatePackage` helpers from `tests/agent/unit/InitStepPipelineTests.cs` and adapt them for integration tests.
- **Minimal API**:
  ```csharp
  public static class PipelineContextBuilder
  {
      public static PipelineContext Update(
          List<PackageAssignment> current,
          List<PackageAssignment> target,
          List<string>? preWorkloadSteps = null,
          List<string>? postWorkloadSteps = null);

      public static PipelineContext Rollback(
          List<PackageAssignment> current,
          List<PackageAssignment> target);

      public static PackageAssignment Package(
          string name, string version,
          List<string>? preInit = null,
          List<string>? postInit = null,
          long? sizeBytes = null);
  }
  ```
- **Time**: 30 min

#### Step C2 — Refactor Existing Integration Tests to Use `PipelineContextBuilder`
- **File**: `tests/agent/integration/PipelineExecutorTests.cs`
- **Change**: Replace the ~50-line inline `PipelineContext` blocks in existing tests with `PipelineContextBuilder.Update(...)` / `Rollback(...)` calls.
- **Verification**: All existing integration tests still pass.
- **Time**: 30 min

---

### Phase D: Add Missing Test Coverage (The Original Goal)
**Goal**: Add the 4 test cases that were blocked.

#### Step D1 — Add Update/Rollback/Init-Step Tests
- **File**: `tests/agent/integration/PipelineExecutorTests.cs`
- **Cases**:
  1. `Update_ChangedPackage_RunsPreInitAndPostInit`
  2. `Rollback_UninstallsRemovedAndInstallsChanged_WithoutInitSteps`
  3. `Update_PreCheckWrongVersion_MarksChanged_RunsInitSteps`
  4. `Update_TwoPhaseWithInitSteps_RunsInOrder`
- **Approach**: Use `PipelineContextBuilder` and `CrossPlatformCommands`. Keep each test under 20 lines.
- **Verification**: `dotnet test tests/agent/integration --filter "FullyQualifiedName~Update|FullyQualifiedName~Rollback"` passes.
- **Time**: 45 min

#### Step D2 — Add Regression Test for GUID Artifact Fallback
- **File**: `tests/agent/unit/AcquireArtifactTests.cs` (or `PipelineExecutorTests.cs`)
- **Case**: `AcquireArtifact_UsesGuidBasedUrl_WhenDownloadUrlIsEmpty`
- **Verification**: Confirms URL contains `{PackageId}/download`, not `Name/Version`.
- **Time**: 15 min

---

### Phase E: Workload Pre-Check Tests
**Goal**: Test the new `WorkloadPreCheck` step once the infrastructure is stable.

#### Step E1 — Add `WorkloadPreCheck` Unit Tests
- **File**: `tests/agent/unit/WorkloadPreCheckTests.cs` (new)
- **Cases**:
  1. `Passes_WhenSpaceIsSufficient`
  2. `Fails_WhenTempDriveSpaceIsInsufficient`
  3. `OnlyCountsAddedAndChangedPackages`
- **Time**: 20 min

#### Step E2 — Add `WorkloadPreCheck` Integration Tests
- **File**: `tests/agent/integration/PipelineExecutorTests.cs`
- **Cases**:
  1. `PipelineExecutor_RunsWorkloadPreCheck_BeforePreWorkloadSteps`
  2. `PipelineExecutor_Halts_WhenWorkloadPreCheckFails`
  3. `PipelineExecutor_SkipsWorkloadPreCheck_OnRollback`
- **Time**: 30 min

---

## Dependency Graph

```
Phase A ──→ Phase B ──→ Phase C ──→ Phase D ──→ Phase E
 (A1,A2)   (B1,B2,B3)   (C1,C2)     (D1,D2)     (E1,E2)
```

- **Phase A** must finish first (otherwise tests are invisible).
- **Phases B and C** can run in parallel after A.
- **Phase D** needs C (builders) and B (commands).
- **Phase E** needs D (stable test authoring) and the WorkloadPreCheck implementation from the main audit fix.

---

## Rollback Strategy

- If standardizing on NUnit for integration tests causes xUnit adapter conflicts, temporarily remove `xunit` package references from the integration project.
- If `CrossPlatformCommands` does not cover a needed command, add an OS-guard (`if (OperatingSystem.IsWindows()) Assert.Skip(...)`).
- If integration tests still time out after fixes, mark them `[Category("Slow")]` and run them separately.

---

## Estimated Effort

| Phase | Steps | Time |
|-------|-------|------|
| A | Fix framework attributes + DOTNET_ROOT doc | 15 min |
| B | Cross-platform command helper + refactor | 55 min |
| C | Shared builders + refactor existing tests | 60 min |
| D | Add 5 missing tests | 60 min |
| E | Add 6 WorkloadPreCheck tests | 50 min |
| **Total** | | **~4 hours** |

---

## Exit Criteria for Follow-Up Session

1. `dotnet test tests/agent/unit` passes with **0 failures**.
2. `dotnet test tests/agent/integration` discovers and runs **all** tests (including the 8 previously invisible ones) without timing out.
3. All 5 audit-fix tests (Update_ChangedPackage, Rollback_UninstallsRemoved, Update_PreCheckWrongVersion, Update_TwoPhaseWithInitSteps, AcquireArtifact_UsesGuidBasedUrl) pass.
4. All 6 WorkloadPreCheck tests pass.
5. A new developer can add an integration test in <10 lines using `PipelineContextBuilder`.
