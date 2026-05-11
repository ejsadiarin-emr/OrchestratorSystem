# 007 - Deepen Workload Run Orchestration

## Problem

Workload run creation and preview logic is spread across controller code and service helpers. The controller owns validation, policy checks, artifact presence checks, idempotency hashing, run creation, and state updates in one flow. Understanding or testing any single rule requires setting up DB state, filesystem artifacts, and multiple related entities, which makes changes brittle and discourages boundary testing.

- Shallow, tightly coupled modules: `WorkloadRunsController`, `PolicyEvaluationService`, `ArtifactStoreService`, `NodeWorkloadStateService`
- Integration risk: policy decisions, artifact checks, and DB mutations are interleaved in a single request path
- Navigation friction: understanding one decision (e.g., downgrade handling) requires bouncing between controller branches and services

## Proposed Interface

TBD after interface design.

## Dependency Strategy

- **Local-substitutable**: DB (EF Core InMemory) + filesystem (artifact store path)

## Testing Strategy

- **New boundary tests to write**: run creation and preview behaviors (install vs update vs skip, downgrade/force handling, artifact missing)
- **Old tests to delete**: likely supersede `tests/orchestrator/unit/WorkloadRunsControllerCurrentPackagesTests.cs`
- **Test environment needs**: EF Core InMemory + temp artifact directory

## Implementation Recommendations

- The deep module should own run planning and creation decisions, including policy checks and artifact presence rules
- The module should hide DB mutation choreography and idempotency details behind a small interface
- Callers should provide minimal inputs (node/workload/revision IDs) and receive an explicit plan or created run
