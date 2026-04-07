# Testing Strategy for Distributed Installer PoC

Date: 2026-04-07

## Testing principles

- Determinism over convenience: avoid tests that depend on timing luck
- Contract-first tests for orchestrator-agent interactions
- Failure-path coverage is as important as happy-path coverage
- Every bug found should become a regression test

## Coverage targets (PoC)

- Unit tests: high coverage for orchestration logic and step orchestration decisions
- Integration tests: cover core job lifecycle and persistence/event correctness
- E2E tests: cover operator-critical flows from UI/API to terminal state

Note: "100% unit coverage" is aspirational and useful for discipline, but meaningful risk reduction comes from balanced unit + integration + E2E coverage with strong failure-path tests.

## Test layers

## 1) Unit tests

Primary targets:

- job state transition validator
- idempotency key handling
- retry policy and backoff logic
- step execution ordering
- adapter return code mapping
- manifest validation
- authorization policy checks
- pre-check confidence aggregation logic
- SignalR message serialization/deserialization
- Channel<T> job queue behavior

Tools:

- NUnit (as requested)
- Moq for mocking interfaces

## 2) Integration tests

Primary targets:

- orchestrator API + state store behavior
- Hangfire job enqueue/dequeue cycle
- SignalR hub connection and message delivery
- agent job claim and result publish cycle
- end-to-end state transitions in backend services
- telemetry emission contract checks
- audit event persistence checks

Environment:

- local orchestrator + local/VM agent
- ephemeral test data store

## 3) E2E tests (highest value)

Operator journeys to automate:

1. View node health and availability
2. Submit install request
3. Monitor step-level progress and logs
4. Observe success and generated evidence
5. Trigger known failure and observe rollback indicators

Recommended tool:

- Playwright for UI E2E

## 4) Fault-injection tests

Must include synthetic faults:

- package checksum mismatch
- precheck failure (disk space/service conflict)
- installer process non-zero exit code
- transient network interruption during download
- agent disconnect during job execution
- SignalR reconnection mid-job
- Hangfire retry exhaustion

These tests validate replayability and resiliency claims.

## Quality gates for PoC readiness

A build is demo-ready only if:

1. Unit/integration/E2E suites pass
2. At least one rollback path succeeds after forced failure
3. Telemetry contains step-level traces and duration metrics for executed jobs
4. Audit trail can answer: who triggered what, where, when, and outcome

## Suggested test case matrix

| Category | Example case | Expected result |
|---|---|---|
| Idempotency | Submit same install intent twice | Second run no harmful duplicate effects |
| Replay | Retry after transient failure | Converges to consistent terminal state |
| Rollback | Mid-step failure on install | Compensation runs and state consistent |
| Security | Unsigned artifact | Execution blocked with clear audit reason |
| AuthZ | ReadOnly user triggers install | Rejected by policy |
| Observability | Install with 3 steps | 1 root span + 3 child step spans present |
| Pre-check | Low-confidence pre-check | Deployment blocked, operator notified |
| Bootstrap | WinRM script on fresh VM | Agent service running and connected |
| Reconnection | Agent disconnects mid-job | Reconnects and resumes or reports failure |

## Anti-patterns to avoid

- brittle sleeps instead of event-driven synchronization
- mocking away all real behavior (false confidence)
- testing only successful paths
- passing tests without asserting side effects and persisted state

## Minimal CI test flow for PoC

1. Restore/build
2. Unit tests
3. Integration tests
4. E2E tests (headless)
5. Artifact and test report publication

Use branch policies so failed tests block merge.
