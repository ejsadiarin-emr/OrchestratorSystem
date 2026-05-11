# 009 - Deepen Artifact Ingest and Package Creation

## Problem

Artifact ingest spans controller flows, zip handling, manifest pairing, validation, and package entity creation. The controller orchestrates multiple ingest paths (single zip, bulk zip, session completion) and mixes filesystem I/O with DB writes. The interface is as complex as its implementation, and testing requires constructing file streams plus DB state for each path.

- Shallow, tightly coupled modules: `ArtifactsController`, `ArtifactIngestService`, `ArtifactZipService`, `ArtifactStoreService`
- Integration risk: zip parsing, validation, and persistence logic are interleaved across endpoints
- Navigation friction: understanding one ingest rule requires stepping through multiple controller paths

## Proposed Interface

TBD after interface design.

## Dependency Strategy

- **Local-substitutable**: filesystem + DB

## Testing Strategy

- **New boundary tests to write**: ingest of zip + manifest pairing, bulk ingest path, upload session completion
- **Old tests to delete**: likely supersede `tests/orchestrator/unit/ArtifactIngestServiceSourceTests.cs`
- **Test environment needs**: temp filesystem + EF Core InMemory

## Implementation Recommendations

- The deep module should own ingest semantics and package entity creation, not the controller
- The module should hide zip handling, manifest resolution, and filesystem operations behind a small interface
- Callers should pass ingest inputs (streams + metadata) and receive created package records
