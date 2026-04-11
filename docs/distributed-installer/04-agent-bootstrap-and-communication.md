# Agent Bootstrap and Communication

Date: 2026-04-07  
Scope: How agents get onto machines and how they talk to the orchestrator

## 1. Agent bootstrap (initial provisioning)

### 1.1 Model: one-time push, then persistent connect + claimed assignment

Agents are installed onto target machines via a **one-time push mechanism**, after which the agent takes over and maintains a persistent SignalR connection to the orchestrator. The push step is a provisioning concern only.

Runtime assignment is canonicalized as agent-initiated connection plus orchestrator assignment over that connection:

`Connect -> Register/Authenticate -> AssignJob -> AckClaim -> LeaseHeartbeat -> StepStatus* -> Complete/Fail -> LeaseClose`

### 1.2 Supported push mechanisms (production)

| Mechanism | Use case | Notes |
|---|---|---|
| WinRM | PoC, small-scale | PowerShell remoting, native Windows |
| GPO | Enterprise AD environments | Group Policy startup scripts |
| SCCM/MECM | Large enterprise fleets | Existing endpoint management |
| SSH | Linux agents (phase 2) | Remote shell execution |

### 1.3 PoC bootstrap flow (WinRM)

1. Operator runs a bootstrap script from the orchestrator machine (or their workstation)
2. Script uses WinRM/PowerShell Remoting to connect to the target machine
3. Script performs:
   - Downloads the agent binary from a known artifact location
   - Writes agent configuration (orchestrator URL, agent name, registration token)
   - Registers the agent as a Windows Service (`sc create`)
   - Starts the service
4. Agent service starts, connects to orchestrator via SignalR, and registers itself
5. Bootstrap is complete — the agent is now managed entirely through the orchestrator

### 1.4 Bootstrap script responsibilities

The one-time script must:

- Validate target machine accessibility (WinRM connectivity test)
- Create necessary directories with appropriate permissions
- Install the agent binary (self-contained .NET executable)
- Configure the service with the orchestrator endpoint and registration credentials
- Start the service and verify it connects successfully
- Report success or failure back to the operator

If bootstrap fails at any point, cleanup runs in reverse order (provisioning scope only):

- Stop service (if started)
- Remove service registration
- Remove staged files/config
- Invalidate enrollment token
- Emit bootstrap audit event

### 1.5 Agent registration

On first connection, the agent sends a registration payload:

- Machine hostname
- OS version and architecture
- Available capabilities (disk space, installed runtimes, etc.)
- Registration token (pre-shared or generated during bootstrap)

The orchestrator validates the token, creates a node record, and assigns a persistent agent ID. The token is single-use.

After enrollment, steady-state connections require per-agent mTLS certificate identity bound to the persistent agent ID. Reconnect attempts without a valid bound certificate are rejected.

## 2. Communication protocol

### 2.1 Agent ↔ Orchestrator: SignalR

**Decision**: Use ASP.NET Core SignalR for all agent-to-orchestrator communication.

**Why SignalR**:

- Native .NET technology — no third-party protocol dependency
- Built-in WebSocket transport with automatic fallback (Server-Sent Events, Long Polling)
- Automatic reconnection with configurable retry policies
- Real-time bidirectional push — orchestrator can send commands, agents can stream logs
- Connection state management built-in (onConnected, onDisconnected, onReconnecting)
- Typed hub contracts in C# — compile-time safety for method signatures

**What flows over SignalR**:

| Direction | Message type | Description |
|---|---|---|
| Orchestrator → Agent | `AssignJob` | Send job definition with manifest |
| Orchestrator → Agent | `CancelJob` | Cancel a running job |
| Orchestrator → Agent | `Ping` | Liveness check |
| Agent → Orchestrator | `LeaseHeartbeat` | Periodic lease heartbeat with metadata |
| Agent → Orchestrator | `StepStatus` | State transition events |
| Agent → Orchestrator | `LogStream` | Real-time log output from pipeline steps |
| Agent → Orchestrator | `Register` | Initial registration on first connect |

During certificate rotation, old and new agent certificates may overlap for up to 15 minutes; rebind events are audited.

### 2.2 UI ↔ Orchestrator: REST + SignalR

- **REST API** for all CRUD operations (create jobs, query nodes, fetch manifests)
- **SignalR** for real-time UI updates (job progress, log streaming, node status changes)

The UI connects to a separate SignalR hub (or the same hub with different authorization) for live updates.

### 2.3 Connection lifecycle

```
Agent starts
  → Connects to SignalR hub
  → Register/Authenticate (first connection consumes enrollment token)
  → Receives AssignJob(assignmentId, leaseId, sequence)
  → Sends AckClaim
  → Processes job pipeline and emits StepStatus updates
  → Sends LeaseHeartbeat every 15s (PoC default)
  → On disconnect: automatic reconnection with exponential backoff
  → On reconnect: resume handshake from `lastAcknowledgedSequence + 1`
```

### 2.4 Reconnection and message durability

- SignalR handles transport-level reconnection automatically
- Job state is persisted in SQL Server on the orchestrator side
- If heartbeat lease expires, orchestrator marks job `AssignedStale` and applies safe reassignment policy
- Reassignment requires idempotency/replay-safe checkpoint checks
- PoC defaults: lease TTL 90s, heartbeat interval 15s, stale threshold 3 missed heartbeats

## 3. Agent architecture

### 3.1 Process model

```
Agent.exe (Windows Service)
├── SignalR client (persistent connection)
├── BackgroundService (Channel<T> consumer)
│   └── Channel<T> (in-memory job queue)
│       └── JobExecutor (spawns child processes)
│           └── Child process 1 (isolated job execution)
│           └── Child process 2 (isolated job execution)
└── OTel instrumentation
```

### 3.2 Why child processes

Each install job runs in a **separate child process** spawned by the agent:

- Isolation: a crashed installer doesn't take down the agent service
- Security: child process can run with different privilege levels
- Cleanup: orphaned processes can be killed on job cancellation
- Resource limits: CPU/memory constraints per job

### 3.3 Agent-side execution flow

1. SignalR receives `AssignJob` message
2. Job is enqueued into `Channel<T>`
3. `BackgroundService` dequeues and creates a `JobExecutor`
4. `JobExecutor` spawns a child process for the job
5. Child process executes the pipeline steps sequentially
6. Each step reports status back to the agent via IPC
7. Agent forwards status updates to orchestrator via SignalR
8. On completion, agent reports terminal state and cleans up child process
