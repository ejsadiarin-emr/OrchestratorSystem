# ADR-006: Agent Single-Pipeline Execution

**Status:** Accepted  
**Date:** 2026-05-17  
**Context:** DeploymentPoC agent side concurrency model for workload run execution

## Problem

When an agent polls `/pending` and receives multiple workload runs, it must decide whether to process them concurrently or sequentially. Concurrent pipeline execution on a single node risks resource contention (disk I/O, registry writes, MSI locks) and makes failure attribution ambiguous.

## Decision

**Each agent processes at most one workload run pipeline at a time.** This is enforced by a `SemaphoreSlim(1, 1)` in `AgentRuntimeService`.

### Execution flow

1. Agent polls `GET /api/workload-runs/pending?agent_id={nodeId}` every 10 seconds (configurable via `Agent:PollIntervalSeconds`)
2. Response is an ordered list of pending runs
3. Agent iterates through the list. For each run:
   - **Try-enter semaphore** (`WaitAsync(0)`): non-blocking; if the semaphore is held, the agent breaks out of the foreach and defers the remaining runs to the next poll cycle
   - **Atomic claim**: `PATCH /api/workload-runs/{runId}` with `{status: "Running"}`. If the claim fails (e.g., another agent or stale run), the semaphore is released and the agent continues to the next run in the list
   - **Fire-and-forget pipeline**: `Task.Run` executes `PipelineExecutor.ExecuteAsync` with a linked cancellation token that times out after `Agent:PipelineTimeoutMinutes` (default: 30)
   - **Final status report**: on pipeline completion, the agent reports `Completed` or `Failed` via `PATCH` with `CancellationToken.None` (best-effort — no retry on failure)

4. Semaphore is released in the `finally` block of `Task.Run`, guaranteeing release even on exception

### Key invariants

- **No acquire-wait on semaphore**: `WaitAsync(0)` means the agent never blocks waiting for a pipeline slot. If one is running, it simply defers.
- **Poll loop always runs**: even when a pipeline is executing in the background, the outer `while (!stoppingToken.IsCancellationRequested)` loop continues polling `/pending`. The `/pending` endpoint serves dual duty as both work dispatch and heartbeat (ADR-002).
- **Pipeline timeout is independent of cancellation token**: `CancellationTokenSource.CreateLinkedTokenSource(ct)` plus `.CancelAfter(timeout)` means the pipeline is killed after 30 minutes even if the service is still running.
- **Best-effort final status**: `CancellationToken.None` on the final PATCH means the agent at least attempts to report status even when the host is shutting down.

## Consequences

### Positive
- **No resource contention** — only one installer process at a time avoids MSI conflicts, file locks, and registry races on Windows
- **Simple failure attribution** — if a pipeline fails, there's no ambiguity about which run caused it
- **Non-blocking poll loop** — the agent never stops checking for work, even while a pipeline runs. The heartbeat continues (ADR-002).
- **Guaranteed semaphore release** — `finally` block ensures the slot is freed even on crashes within `Task.Run`

### Negative
- **Serial throughput** — if an agent has 5 pending runs, they execute one at a time with 10-second delays between completions (one poll cycle to claim the next)
- **No work stealing** — if agent A has a long-running pipeline and agent B is idle, agent B cannot steal agent A's queued runs. The filtered unique index (ADR-005) prevents this.
- **Fire-and-forget pipeline** — `Task.Run` with no `await` means exceptions in the outer try block before `_pipelineExecutor.ExecuteAsync` throw on the thread pool rather than being observed by the poll loop. The inner try/catch handles pipeline failures.
- **No backpressure signal** — the agent doesn't tell the orchestrator "I'm busy" before claiming. The claim (PATCH to Running) is atomic and will fail if the run was already claimed, but the agent still attempts it on every poll.

## Trade-offs Accepted

- Serial execution is intentional for deployment software. MSI installers, registry writes, and file operations are not safe to parallelize on a single Windows node.
- The 10-second minimum gap between pipeline starts (next poll cycle) is acceptable for deployment workloads. Production software rollout is not a latency-sensitive operation.
- Best-effort final status reporting (`CancellationToken.None`) means the orchestrator may leave a run in `Running` state if the agent crashes after pipeline completion but before the PATCH. The `NodeHeartbeatMonitorService` will eventually mark the node offline, but no automated run-state cleanup exists for orphaned runs.

## Related

- `AgentRuntimeService.cs`: full implementation of poll loop, semaphore, and pipeline dispatch
- ADR-002: HTTP Polling (the `/pending` endpoint that drives this loop)
- ADR-005: Run State Machine & Concurrency Control (server-side complement — filtered unique index prevents duplicate runs)
- `PipelineExecutor.cs`: the actual pipeline execution logic