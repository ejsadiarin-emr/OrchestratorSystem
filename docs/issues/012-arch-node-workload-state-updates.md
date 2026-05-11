# 012 - Deepen Node Workload State Updates Across Hub and HTTP

## Problem

Node workload state transitions are handled in both SignalR hub paths and HTTP polling endpoints. The same concepts (claim, completion, status updates) are encoded in different shapes, risking drift between hub and HTTP flows. The module boundary is shallow because state transition rules are split across entry points.

- Shallow, tightly coupled modules: `AgentRuntimeHub`, `NodeWorkloadStateService`, `WorkloadRunsController`
- Integration risk: hub vs HTTP paths can diverge in transition rules
- Navigation friction: understanding a state change requires tracing two entry points and shared services

## Proposed Interface

TBD after interface design.

## Dependency Strategy

- **Remote but owned (ports & adapters)**: SignalR hub adapter
- **Local-substitutable**: DB persistence

## Testing Strategy

- **New boundary tests to write**: consistent state transitions across hub and HTTP entry points
- **Old tests to delete**: likely supersede `tests/orchestrator/unit/Hubs/AgentRuntimeHubTests.cs` and `tests/orchestrator/unit/NodeWorkloadStateServiceRevisionTests.cs`
- **Test environment needs**: in-memory hub adapter + EF Core InMemory

## Implementation Recommendations

- The deep module should own state transition rules and accept normalized inputs from hub or HTTP adapters
- Transport-specific details (SignalR vs HTTP) should be adapters that feed a shared interface
- Callers should submit state change events and receive a normalized update result
