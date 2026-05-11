# 010 - Deepen Agent Pipeline Execution

## Problem

`PipelineExecutor` is a large orchestration method that mixes diffing decisions, download, install/uninstall, verification, and status reporting. The interface is broad, with many branches that rely on filesystem, process execution, and HTTP behavior. This makes unit testing brittle and encourages broad integration tests rather than boundary tests.

- Shallow, tightly coupled modules: `PipelineExecutor`, `PipelineContext`, `PackageDetector`
- Integration risk: status reporting and control flow are interleaved, leading to edge-case drift
- Navigation friction: understanding one phase requires stepping through a long method with nested branches

## Proposed Interface

TBD after interface design.

## Dependency Strategy

- **True external (mock)**: process execution
- **Local-substitutable**: HTTP download adapter

## Testing Strategy

- **New boundary tests to write**: pipeline behavior across phase outcomes (success/failure, uninstall-first, diff gating)
- **Old tests to delete**: consolidate `tests/agent/integration/PipelineExecutorTests.cs` and `tests/agent/unit/PipelineExecutorTests.cs` into boundary tests
- **Test environment needs**: in-memory adapters for HTTP + process runner mocks

## Implementation Recommendations

- The deep module should own the pipeline phase orchestration and status emission sequencing
- External IO (download/process) should be injected, allowing tests to run without real side effects
- Callers should supply a run payload and receive a structured execution result
