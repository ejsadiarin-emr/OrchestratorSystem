# 011 - Deepen Workload Revision Import

## Problem

Workload revision creation and bulk import logic mixes validation, JSON parsing, adapter resolution, and DB mutation across controller and service code. The module boundary is shallow: the controller coordinates domain logic and persistence, making the import rules hard to test in isolation and forcing integration-style setups.

- Shallow, tightly coupled modules: `WorkloadsController`, `WorkloadImportService`, `ArtifactStoreService`
- Integration risk: parsing and validation rules are interleaved with DB entity creation
- Navigation friction: understanding import behavior requires bouncing between controller and service parsing rules

## Proposed Interface

TBD after interface design.

## Dependency Strategy

- **Local-substitutable**: DB + filesystem (artifact store)

## Testing Strategy

- **New boundary tests to write**: bulk import parsing, validation rejection, adapter resolution
- **Old tests to delete**: likely supersede `tests/orchestrator/unit/Controllers/WorkloadsControllerTests.cs` and `tests/orchestrator/unit/WorkloadImportServiceTests.cs`
- **Test environment needs**: EF Core InMemory + temp artifact path

## Implementation Recommendations

- The deep module should own import parsing, validation, and entity creation
- The module should hide adapter resolution details behind a small interface
- Callers should pass import JSON and receive a structured import result
