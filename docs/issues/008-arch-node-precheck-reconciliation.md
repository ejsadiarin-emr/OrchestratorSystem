# 008 - Deepen Node Pre-Check Probing and Reconciliation

## Problem

Node pre-checks mix detection config assembly, HTTP probing, reconciliation logic, and DB mutation inside `NodesController`. The same endpoint constructs requests, calls the agent, interprets responses, and updates stored state, creating tight coupling between transport, policy, and persistence. This makes the pre-check flow hard to test without full HTTP + DB wiring and increases risk of drift between summary and per-node responses.

- Shallow, tightly coupled modules: `NodesController`, `DetectEndpointHandler` (agent), detection DTO assembly
- Integration risk: reconciliation rules and DB updates are interleaved with HTTP callout error handling
- Navigation friction: understanding pre-check outcomes requires tracing multiple methods (`RunPreCheckSummary`, `RunSinglePreCheck`, `ProbeNodeAsync`, `ReconcileProbeResults`)

## Proposed Interface

Hybrid (1 + 4): a **minimal deep service** that owns probing + reconciliation + DB mutation, with a **probe port** for HTTP transport.

**Deep module (public boundary):**

```csharp
public interface INodePreCheckService
{
    Task<PreCheckRunResult> RunAsync(PreCheckRunRequest request, CancellationToken ct);
    Task<PreCheckSummaryResult> SummarizeAsync(PreCheckSummaryRequest request, CancellationToken ct);
}
```

**Probe port (transport boundary):**

```csharp
public interface IAgentProbePort
{
    Task<ProbeResult> ProbeAsync(
        NodeEntity node,
        IReadOnlyList<PackageDetectionRequest> packages,
        CancellationToken ct);
}
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

## Dependency Strategy

- **Remote but owned (ports & adapters)**: `IAgentProbePort` with HTTP adapter in prod, in-memory adapter in tests
- **Local-substitutable**: EF Core DB (SQLite/InMemory) for persistence + reconciliation state
- **In-process**: action mapping, version comparison, detection config assembly

## Testing Strategy

- **New boundary tests to write**: exercise `INodePreCheckService` with in-memory `IAgentProbePort` for probe permutations (success, non-200, timeout, unreachable, deserialize)
- **Old tests to delete**: replace controller-level coverage in `tests/orchestrator/unit/Controllers/NodesPreCheckReconciliationTests.cs` once boundary tests fully match behavior
- **Test environment needs**: in-memory probe adapter + EF Core InMemory/SQLite

## Implementation Recommendations

- The deep module owns detection config assembly, probe execution, reconciliation outcomes, and DB mutation
- HTTP transport is injected behind `IAgentProbePort`; the core is testable without HTTP
- Controllers should only pass identifiers and return results; no probe logic in controllers
- Refactor must be behavior-preserving; use existing tests as regression baselines until boundary tests are green
