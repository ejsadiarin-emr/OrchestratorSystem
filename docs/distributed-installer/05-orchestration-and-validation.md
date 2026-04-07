# Orchestration and Validation

Date: 2026-04-07  
Scope: Job queue library, orchestration patterns, dry-run validation confidence

## 1. Job queue architecture

### 1.1 Orchestrator side: Hangfire

**Decision**: Use Hangfire on the orchestrator for job queue management.

**Why Hangfire**:

- Persistent queue backed by SQL Server (already a dependency)
- Built-in retry logic with configurable policies
- Dashboard for monitoring and manual intervention
- Native .NET integration — no external message broker needed
- Supports delayed jobs, recurring jobs, and job chains
- Proven in production, well-documented

**What Hangfire manages**:

- Job creation and scheduling
- Dependency resolution (if job B depends on job A)
- Retry on transient failures
- Job history and audit trail
- Dashboard for operators to monitor queue depth, failures, retries

### 1.2 Agent side: Channel<T> + BackgroundService

**Decision**: Agents use `System.Threading.Channels.Channel<T>` with a `BackgroundService` — no persistent queue library on agents.

**Why not Hangfire on agents**:

- Agents don't need persistent storage — the orchestrator is the source of truth
- No SQL dependency on agents reduces footprint
- Jobs are pushed to agents in real-time via SignalR; if the agent restarts, it re-queries the orchestrator
- Simpler deployment and configuration

**How it works**:

```csharp
// Agent service registration
services.AddSingleton(Channel.CreateBounded<JobMessage>(new BoundedChannelOptions(100)
{
    FullMode = BoundedChannelFullMode.Wait
}));

services.AddHostedService<JobExecutionService>();

// JobExecutionService
public class JobExecutionService : BackgroundService
{
    private readonly Channel<JobMessage> _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await ExecuteJob(job, stoppingToken);
        }
    }
}
```

### 1.3 End-to-end job flow

```
UI/API → Orchestrator REST API → Hangfire enqueue
                                    ↓
                            Hangfire dequeues → resolves target agent(s)
                                    ↓
                            SignalR Hub → AssignJob to agent
                                    ↓
                            Agent SignalR client → Channel<T> enqueue
                                    ↓
                            BackgroundService → dequeue → execute
                                    ↓
                            Agent → SignalR → status updates → orchestrator
                                    ↓
                            Orchestrator → updates SQL → Hangfire marks complete
```

## 2. Orchestration pattern

### 2.1 Airflow CeleryExecutor model

The orchestrator follows the **Airflow CeleryExecutor** pattern:

- The orchestrator understands the full DAG (job dependencies, ordering)
- The orchestrator resolves the DAG and enqueues individual steps to agents
- Agents only execute individual steps — they don't understand the full DAG
- The orchestrator tracks step completion and enqueues the next dependent step

This keeps agents simple and stateless regarding orchestration logic.

### 2.2 Dependency resolution

For PoC, dependency resolution is simple:

- Jobs can declare `dependsOn` references to other job IDs
- Hangfire enqueues jobs only after their dependencies have reached `Succeeded`
- If a dependency fails, dependent jobs are marked `Failed` with reason `dependency_failed`

Future phases can add more sophisticated DAG resolution (parallel branches, conditional paths).

## 3. Dry-run validation (pre-check) confidence

### 3.1 Two-phase validation

Every install job runs a two-phase validation before execution:

**Phase 1: Static validation** (100% confidence)
- Manifest schema validation
- Artifact checksum verification
- Digital signature verification
- Required field presence

These checks are deterministic — they either pass or fail with no ambiguity.

**Phase 2: Dynamic validation** (variable confidence)
- OS version compatibility
- Disk space availability
- Dependency presence (required runtimes, services)
- Port availability
- File lock detection

These checks depend on runtime state that could change between validation and execution.

### 3.2 Confidence level framework

Each pre-check reports a confidence level based on **how directly the check can verify the condition**:

| Confidence | Basis | Examples |
|---|---|---|
| **100% (deterministic)** | Direct, authoritative check with no external dependencies | File checksum matches, digital signature valid, JSON schema validates |
| **High (~95%)** | Check relies on OS-reported state that could drift | OS version check, registry key exists, .NET runtime version |
| **Medium (~70-80%)** | Check depends on external state or timing | Disk space available, network connectivity to dependency server |
| **Low (~50-60%)** | Check is indirect or heuristic | "Similar machines installed successfully", "dependency likely present based on manufacturer" |

### 3.3 Overall deployment confidence

The overall deployment confidence is the **minimum** of all individual check confidences — the weakest link determines the action:

| Overall confidence | Action |
|---|---|
| 100% | Proceed automatically |
| High (≥90%) | Proceed with warning logged |
| Medium (≥70%) | Require operator confirmation |
| Low (<70%) | Block deployment, require manual review |

This is conservative but appropriate for industrial control systems where a failed mid-install is worse than a blocked install.

### 3.4 Empirical confidence tracking

Over time, the system tracks actual success rates per check type:

- Each check records whether its prediction was correct (did the condition hold at execution time?)
- After sufficient data, confidence levels can be adjusted empirically
- If "disk space > 5GB" has been right 99.7% of the time, it graduates from Medium to High
- This data is exposed in the UI as a "validation accuracy" metric

### 3.5 Pre-check implementation

```csharp
public interface IPreCheck
{
    string Name { get; }
    ConfidenceLevel Confidence { get; }
    Task<PreCheckResult> ExecuteAsync(JobContext context, CancellationToken ct);
}

public record PreCheckResult(
    bool Passed,
    ConfidenceLevel Confidence,
    string? FailureReason,
    DateTime CheckedAt);

public enum ConfidenceLevel
{
    Deterministic = 100,
    High = 95,
    Medium = 75,
    Low = 55
}
```

Each pre-check implementation reports its own confidence level. The orchestrator aggregates results and determines the overall deployment action.
