# 008 - Deepen Node Pre-Check Probing and Reconciliation

## Problem

Node pre-checks mix detection config assembly, HTTP probing, reconciliation logic, and DB mutation inside `NodesController`. The same endpoint constructs requests, calls the agent, interprets responses, and updates stored state, creating tight coupling between transport, policy, and persistence. This makes the pre-check flow hard to test without full HTTP + DB wiring and increases risk of drift between summary and per-node responses.

- Shallow, tightly coupled modules: `NodesController`, `DetectEndpointHandler` (agent), detection DTO assembly
- Integration risk: reconciliation rules and DB updates are interleaved with HTTP callout error handling
- Navigation friction: understanding pre-check outcomes requires tracing multiple methods (`RunPreCheckSummary`, `RunSinglePreCheck`, `ProbeNodeAsync`, `ReconcileProbeResults`)

## Proposed Interface

Hybrid (1 + 4): a **minimal deep service** that owns probing + reconciliation + DB mutation, with a **probe port** for HTTP transport.

**Deep module (public boundary, behavior-preserving):**

Location:

- `apps/orchestrator/backend/Services/PreChecks/INodePreCheckService.cs`
- `apps/orchestrator/backend/Services/PreChecks/NodePreCheckService.cs`
- Namespace: `DeploymentPoC.Orchestrator.Services.PreChecks`

```csharp
public interface INodePreCheckService
{
    Task<List<NodePreCheckResponse>> RunAsync(RunPreCheckRequest request, CancellationToken ct);
    Task<PreCheckSummaryResponse> SummarizeAsync(RunPreCheckSummaryRequest request, CancellationToken ct);
    Task<NodePreCheckSummary> RunSingleAsync(Guid nodeId, Guid? workloadId, CancellationToken ct);
}
```

**Probe port (transport boundary):**

Location:

- `apps/orchestrator/backend/Services/PreChecks/IAgentProbePort.cs`
- `apps/orchestrator/backend/Services/PreChecks/HttpAgentProbeAdapter.cs`
- Namespace: `DeploymentPoC.Orchestrator.Services.PreChecks`

```csharp
public interface IAgentProbePort
{
    Task<ProbeResult> ProbeAsync(
        NodeEntity node,
        IReadOnlyList<PackageDetectionRequest> packages,
        CancellationToken ct);
}

public sealed record ProbeResult(
    NodeDetectResponse? Response,
    string? Error,
    bool Success);
```

**Usage sketch (controller):**

```csharp
var result = await _preCheckService.RunAsync(
    new PreCheckRunRequest(nodeIds, workloadId, revisionId), ct);
return Ok(result);
```

**Behavioral parity requirements (no functional change):**

- Preserve action mapping: `BlockedDowngrade`, `BlockedVersionJump`, `FreshInstall`, `Skip`, `Update`, `InstallMissing`.
- Preserve reconciliation rules: update `Current/Drifted`, remove DB state when nothing detected, **never auto-promote** `CurrentRevisionId`.
- Preserve detection config assembly rules (assigned revisions when `workloadId` is absent).
- Preserve probe error strings and handling (timeout, unreachable, non-200, deserialize).

## Detailed Blueprint

### 1) Service Surface and Contracts

The service should keep **existing API contracts** and models to avoid response drift. Use the DTOs from `apps/orchestrator/backend/Models/Node.cs` unchanged.

Service methods map 1:1 to existing endpoints:

1. `RunAsync` => `POST /api/nodes/prechecks` (current `RunPreChecks`)
2. `SummarizeAsync` => `POST /api/nodes/prechecks/summary` (current `RunPreCheckSummary`)
3. `RunSingleAsync` => `POST /api/nodes/{id}/prechecks` (current `RunSinglePreCheck`)

### 2) Probe Port and Adapter

`IAgentProbePort` replaces the in-controller HTTP call and must preserve **exact error semantics**:

- Non-200: `Agent returned status {code}`
- Deserialize failure: `Failed to deserialize agent response`
- Timeout: `Agent probe timed out`
- Unreachable: `Agent unreachable: {ex.Message}`
- Unexpected: `Unexpected error: {ex.Message}`

Probe request must **dedupe** packages by `PackageId` and use `DetectRequest` with case-insensitive JSON options. URL stays `http://{node.IpAddress}:5001/api/detect`.

Timeout source:

- Use `AgentProbeTimeoutSeconds` from configuration (same key as current controller).
- Prefer `CancellationTokenSource(TimeSpan)` to mirror `ProbeNodeAsync` behavior.

### 3) Internal Helpers to Move (no logic changes)

Move these methods out of `apps/orchestrator/backend/Controllers/NodesController.cs` into the service, keeping logic identical:

- `LoadDetectionConfigsForRevisionAsync`
- `LoadDetectionConfigsByWorkloadAsync`
- `ReconcileProbeResults`
- `BuildPackageStatesJson`
- `BuildPerPackageItems`
- `ComputeComparison`
- `FormatBytes`

Keep helper names and signatures unchanged unless required by visibility constraints.

### 4) Data Flow: `RunPreChecks`

1. Validate `RunPreCheckRequest.NodeIds` non-empty.
2. Load nodes with `NodeWorkloadStates` and `CurrentRevision`.
3. Build detection configs:
   - If `WorkloadId` provided: one shared config dictionary.
   - Else: per-node configs using assigned revisions (`LoadDetectionConfigsByWorkloadAsync(null, node)`).
4. For each node:
   - Call probe port with deduped package list.
   - On error, return `NodePreCheckResponse` with `Error` set and no summary.
   - On success, call `ReconcileProbeResults` to build `NodePreCheckSummary` and mutate DB.
5. Save changes once after loop (same as current).

### 5) Data Flow: `RunPreCheckSummary`

1. Validate `RunPreCheckSummaryRequest.NodeIds` non-empty.
2. Load nodes list (no includes).
3. Build **revision** configs with `LoadDetectionConfigsForRevisionAsync(request.RevisionId)`.
4. Load node states for the workload and published revisions list for version-jump detection.
5. For each node:
   - Determine `WorkloadStatus` from DB state (Current/Drifted/Unknown/Absent).
   - Probe via port.
   - On error: set `Action = "Unknown"` and `ActionDetail = error ?? "Probe failed"`.
   - On success: build `PreCheckSummaryPackage` list; reconcile DB (update or create state).
   - Compute action using current switch logic (BlockedDowngrade, BlockedVersionJump, FreshInstall, Skip, Update, InstallMissing).
6. Save changes once after loop (same as current).

### 6) Data Flow: `RunSinglePreCheck`

1. Load node with states + current revision; 404 if missing.
2. Build detection configs (workloadId or assigned revisions).
3. Probe via port.
4. On error: return `NodePreCheckSummary` with a single `PreCheckItem` (`Category=error`, `Name=Probe Failed`, `Status=failed`, `Detail=error ?? "Unknown error"`).
5. On success: reconcile, save changes, return summary.

## Dependency Strategy

- **Remote but owned (ports & adapters)**: `IAgentProbePort` with HTTP adapter in prod and in-memory adapter in tests
- **Local-substitutable**: EF Core DB (SQLite/InMemory) for persistence + reconciliation state
- **In-process**: action mapping, version comparison, detection config assembly
- **DI registrations** (Program.cs): add scoped `INodePreCheckService`, scoped `IAgentProbePort`

Concrete DI changes in `apps/orchestrator/backend/Program.cs`:

```csharp
builder.Services.AddScoped<INodePreCheckService, NodePreCheckService>();
builder.Services.AddScoped<IAgentProbePort, HttpAgentProbeAdapter>();
```

## Dependency Strategy

- **Remote but owned (ports & adapters)**: `IAgentProbePort` with HTTP adapter in prod, in-memory adapter in tests
- **Local-substitutable**: EF Core DB (SQLite/InMemory) for persistence + reconciliation state
- **In-process**: action mapping, version comparison, detection config assembly

## Testing Strategy

- **Regression baseline**: keep `tests/orchestrator/unit/Controllers/NodesPreCheckReconciliationTests.cs` green during refactor
- **New boundary tests**: exercise `INodePreCheckService` with in-memory `IAgentProbePort` for probe permutations (success, non-200, timeout, unreachable, deserialize)
- **Retire controller tests**: only after boundary tests fully match behavior
- **Test environment needs**: in-memory probe adapter + EF Core InMemory/SQLite

## Implementation Recommendations

- The deep module owns detection config assembly, probe execution, reconciliation outcomes, and DB mutation
- HTTP transport is injected behind `IAgentProbePort`; the core is testable without HTTP
- Controllers should only pass identifiers and return results; no probe logic in controllers
- Refactor must be behavior-preserving; use existing tests as regression baselines until boundary tests are green

## Migration Checklist

1. Add new files for the service and port (e.g., `apps/orchestrator/backend/Services/PreChecks/INodePreCheckService.cs`, `NodePreCheckService.cs`, `IAgentProbePort.cs`, `HttpAgentProbeAdapter.cs`).
2. Move helper methods from `NodesController` into `NodePreCheckService` with **no logic changes**.
3. Implement `HttpAgentProbeAdapter` using current probe behavior and **exact error strings**.
4. Implement `INodePreCheckService` methods to return existing DTOs and perform the same DB mutations as today.
5. Update `NodesController` to call the service and remove in-controller probe/reconcile logic.
6. Register new services in `apps/orchestrator/backend/Program.cs`.
7. Run regression tests and fix any behavioral drift before adding new boundary tests.
8. Add boundary tests for the new service; retire controller tests only after parity is proven.

## Code Examples (to implement)

**Service skeleton (use existing DTOs, no new public models):**

```csharp
namespace DeploymentPoC.Orchestrator.Services.PreChecks;

public sealed class NodePreCheckService : INodePreCheckService
{
    private readonly InstallerDbContext _db;
    private readonly ILogger<NodePreCheckService> _logger;
    private readonly IAgentProbePort _probePort;

    public NodePreCheckService(
        InstallerDbContext db,
        ILogger<NodePreCheckService> logger,
        IAgentProbePort probePort)
    {
        _db = db;
        _logger = logger;
        _probePort = probePort;
    }

    public async Task<List<NodePreCheckResponse>> RunAsync(RunPreCheckRequest request, CancellationToken ct)
    {
        // move logic from NodesController.RunPreChecks without behavior changes
        throw new NotImplementedException();
    }

    public async Task<PreCheckSummaryResponse> SummarizeAsync(RunPreCheckSummaryRequest request, CancellationToken ct)
    {
        // move logic from NodesController.RunPreCheckSummary without behavior changes
        throw new NotImplementedException();
    }

    public async Task<NodePreCheckSummary> RunSingleAsync(Guid nodeId, Guid? workloadId, CancellationToken ct)
    {
        // move logic from NodesController.RunSinglePreCheck without behavior changes
        throw new NotImplementedException();
    }
}
```

**Probe adapter skeleton (must preserve error strings):**

```csharp
namespace DeploymentPoC.Orchestrator.Services.PreChecks;

public sealed class HttpAgentProbeAdapter : IAgentProbePort
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpAgentProbeAdapter> _logger;
    private readonly IConfiguration _configuration;

    public HttpAgentProbeAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpAgentProbeAdapter> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ProbeResult> ProbeAsync(
        NodeEntity node,
        IReadOnlyList<PackageDetectionRequest> packages,
        CancellationToken ct)
    {
        // move logic from NodesController.ProbeNodeAsync and RunSinglePreCheck probe block
        // preserve URL, timeout, dedupe, and error strings exactly
        throw new NotImplementedException();
    }
}
```

**Controller wiring (minimal change):**

```csharp
[HttpPost("prechecks")]
public async Task<ActionResult<List<NodePreCheckResponse>>> RunPreChecks(
    [FromBody] RunPreCheckRequest request,
    CancellationToken ct)
    => Ok(await _preCheckService.RunAsync(request, ct));

[HttpPost("prechecks/summary")]
public async Task<ActionResult<PreCheckSummaryResponse>> RunPreCheckSummary(
    [FromBody] RunPreCheckSummaryRequest request,
    CancellationToken ct)
    => Ok(await _preCheckService.SummarizeAsync(request, ct));

[HttpPost("{id:guid}/prechecks")]
public async Task<ActionResult<NodePreCheckSummary>> RunSinglePreCheck(
    Guid id,
    [FromQuery] Guid? workloadId,
    CancellationToken ct)
    => Ok(await _preCheckService.RunSingleAsync(id, workloadId, ct));
```

**Concrete controller changes (NodesController):**

- Constructor dependencies remove `IHttpClientFactory` and `IConfiguration`.
- Add dependency `INodePreCheckService`.
- Remove probe/reconcile methods and inline probe block in `RunSinglePreCheck`.
- Update usings:
  - Add `using DeploymentPoC.Orchestrator.Services.PreChecks;`
  - Remove `using System.Text;`, `using System.Text.Json;`, `using DeploymentPoC.Contracts.Runtime.Probes;` if no longer used.

## Expected Deletions / Moves

**Move out of** `apps/orchestrator/backend/Controllers/NodesController.cs`:

- `ProbeNodeAsync`
- `ReconcileProbeResults`
- `LoadDetectionConfigsForRevisionAsync`
- `LoadDetectionConfigsByWorkloadAsync`
- `BuildPackageStatesJson`
- `BuildPerPackageItems`
- `ComputeComparison`
- `FormatBytes`

**Delete from controller** once moved:

- Inline probe block in `RunSinglePreCheck`
- Probe logic in `RunPreChecks` and `RunPreCheckSummary`

## Tests (add/modify/delete)

**Keep initially (regression):**

- `tests/orchestrator/unit/Controllers/NodesPreCheckReconciliationTests.cs`

**Add (new service boundary tests):**

- `tests/orchestrator/unit/Services/NodePreCheckServiceTests.cs`
  - Probe error handling (timeout, unreachable, non-200, deserialize)
  - Assigned-revision config assembly when `WorkloadId` absent
  - DB state updates for Scenario A/B/D/E and unassigned workloads
  - Summary action mapping for Unknown/Drifted/Absent

**Retire (after parity proven):**

- Optionally remove or slim controller tests once service tests cover all cases

## Guardrails (must not change)

- No route changes, no DTO shape changes, no new response fields.
- Error strings must be **exactly** preserved.
- Probe URL and package de-duplication behavior must be unchanged.
- Action mapping rules and version-jump logic must be unchanged.
- `CurrentRevisionId` must never be auto-promoted.
- `RunSinglePreCheck` must return a summary with an error item on probe failure (not an error response).
- SaveChanges timing should match current behavior (once per endpoint execution).

## Acceptance Criteria

- Existing endpoint behaviors are unchanged for success and error cases.
- `NodesPreCheckReconciliationTests` passes without modifications to assertions.
- No changes to public API contracts in `apps/orchestrator/backend/Models/Node.cs`.
- No changes to agent contract (`DetectRequest`, `NodeDetectResponse`).

## Verification Steps

1. `dotnet test tests/orchestrator/unit`
2. (Optional) `dotnet test tests/orchestrator/integration` when orchestrator is running
