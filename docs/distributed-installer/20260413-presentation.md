---
marp: true
theme: default
paginate: true
---

# Distributed Installer PoC
## Ej Sadiarin
<!-- ## Emerson Internship — Design Overview -->

---

## What Is This?

A **distributed installer framework** for Windows machines using .NET + React

- Remote installation and upgrade of software across multiple nodes
- Real-time job tracking and observability
- Idempotent, rollback-capable execution

---

## Architecture Overview

```text
+----------------------+       +-------------------------------------------+       +------------------+
|      React UI         |       |        ASP.NET Core Orchestrator            |       | Windows Agent    |
| (Job submission,     |<----->|  +----------+  +----------+  +------+ |<------| (Windows       |
|  real-time status)  |       |  | REST API |  |SignalR Hub|  |Hangfire||       |  Service)      |
+----------+---------+       |  | (CRUD)   |  |(realtime)|  |(queue)||       |  - SignalR    |
           |              |  +-----+----+  +-----+----+  +---+--+|       |  - Job Exec   |
           | REST+SignalR   |        |          |          |      |       |  - Child Proc |
           v              |        v          v          v      |       +--------------+
+----------------------+       +----+-----------------+------+               +---------------+
|                         |       | SQL Server       | Audit/Event |              |
|                         |<------| (job state)      | (append)    |              |
+----------------------+       +-------------------+-------------+--------------+
```

---

## Agent Bootstrap

### One-time Push, Then Persistent Connect

| Step | Mechanism |
|------|-----------|
| PoC | WinRM (PowerShell remoting) |
| Enterprise AD | GPO startup scripts |
| Large fleet | SCCM/MECM |
| Linux (phase 2) | SSH |

```text
     Step 1                   Step 2                   Step 3                   Step 4/5                  Step 6
       |                      |                      |                      |                       |
       v                      v                      v                      v                       v
+------------------+   +------------------+   +------------------+   +------------------+   +------------------+
|    Operator      |   |   Orchestrator   |   | Target Machine  |   | Windows Service  |   |   Orchestrator   |
| (runs bootstrap)|-->| (source script)  |-->| (WinRM target)  |-->|   (Agent.exe)  |-->| (SignalR Hub)   |
+------------------+   +------------------+   +------------------+   +------------------+   +------------------+
                                                                                          |
                                                                                          v
                                                                              +---------------------------+
                                                                              | Ready for job assignments |
                                                                              +---------------------------+
```

---

## Communication Protocol

### SignalR

- Native .NET, no third-party dependencies
- Built-in WebSocket + automatic fallback
- Automatic reconnection with exponential backoff
- Real-time bidirectional push
- Typed hub contracts in C#

### Runtime Protocol Sequence

```
Connect → Register → AssignJob → AckClaim → 
LeaseHeartbeat → StepStatus* → Complete/Fail → LeaseClose
```

---

### Lease Defaults (PoC)

| Parameter | Value |
|-----------|-------|
| Lease TTL | 90s |
| Heartbeat interval | 15s |
| Stale threshold | 3 missed heartbeats |
| Stale timeout | 15 minutes max |

---

## Messages Over SignalR

| Direction | Message | Description |
|----------|---------|-------------|
| Orch → Agent | `AssignJob` | Job definition with manifest |
| Orch → Agent | `CancelJob` | Cancel running job |
| Orch → Agent | `Ping` | Liveness check |
| Agent → Orch | `LeaseHeartbeat` | Periodic heartbeat |
| Agent → Orch | `StepStatus` | State transition events |
| Agent → Orch | `LogStream` | Real-time log output |
| Agent → Orch | `Register` | Initial registration |

---

## Job State Machine

```
Queued → Assigned → Prechecking → 
PrecheckPassed → Downloading → Installing → 
Verifying → Succeeded
         ↓                    ↓
   PrecheckFailed       VerifyFailed
         ↓                    ↓
   Failed ──→ RollbackInProgress ──→ RolledBack
```

### Terminal States

- `Succeeded` — full success
- `Failed` — no rollback possible
- `RolledBack` — compensation successful
- `Cancelled` — manually aborted

---

## Pipeline Steps

1. **PreConditionCheck** — dry-run validation
2. **AcquireArtifact** — download from package source
3. **ValidateSignatureAndHash** — cryptographic verification
4. **DetectCurrentState** — idempotency check
5. **InstallOrUpgrade** — execute installer adapter
6. **PostInstallVerify** — confirm success
7. **EmitFinalization** — telemetry + audit

### Optional Compensation

- `RollbackOrCompensate`
- `RollbackVerify`

---

## Manifest Contract

Data-driven, not script-driven:

- Package identity + target version
- Artifact location + integrity metadata
- Execution mode (`install`, `upgrade`, `rollback`)
- Detection rules
- Install arguments profile
- Rollback strategy
- Expected return codes
- Reboot policy

---

## Agent Architecture

```
Agent.exe (Windows Service)
├── SignalR client (persistent connection)
├── BackgroundService (Channel<T> queue)
│   └── JobExecutor (spawns child processes)
│       ├── Child process 1 (isolated job)
│       └── Child process 2 (isolated job)
└── OTel instrumentation
```

**Why child processes?**
- Isolation - crashed installer ≠ killed agent
- Security - different privilege levels
- Cleanup - orphaned processes killable
- Resource limits - CPU/memory per job

---

## Observability

### Trace Model

- Root span: `installer.job`
- Child spans: one per pipeline step
- Required attributes: job.id, node.id, package.id, step.name, result.status

### Metrics

- `installer.job.duration`
- `installer.step.duration`
- `installer.job.failure.count`
- `installer.job.retry.count`
- `agent.heartbeat.latency`

### Logs

Structured JSON with correlation IDs — no secrets.

---

## Demo Scenarios

| Scenario |Sequence |
|----------|-----------|
| Happy path | Queue → assigned → precheck → install → succeeded |
| Failure + rollback | Trigger failure → observe rollback → confirm consistent state |
| Replay/idempotency | Re-submit same job → no duplicate harmful side effects |

---

## Known Limitations (PoC)

- Not designed for large-scale concurrency
- Adapter coverage limited to key installer types
- No formal multi-region/failover topology
- Linux agent is architectural placeholder

---

## Future Roadmap

1. Linux agent + cross-platform abstraction
2. Policy engine (rings, canary, maintenance windows)
3. Artifact provenance + SBOM enforcement
4. Scale-oriented queueing strategies
5. Advanced diagnostics + self-healing

