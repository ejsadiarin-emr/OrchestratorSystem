# Agent Bootstrap and Communication

Date: 2026-04-07  
Scope: How agents get onto machines and how they talk to the orchestrator

## 1. Agent bootstrap (initial provisioning)

### 1.1 Model: one-time push, then permanent pull

Agents are installed onto target machines via a **one-time push mechanism**, after which the agent takes over and connects to the orchestrator in pull mode. The push step is a provisioning concern only — the agent never relies on push for job delivery.

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

### 1.5 Agent registration

On first connection, the agent sends a registration payload:

- Machine hostname
- OS version and architecture
- Available capabilities (disk space, installed runtimes, etc.)
- Registration token (pre-shared or generated during bootstrap)

The orchestrator validates the token, creates a node record, and assigns a persistent agent ID. The token is single-use — subsequent connections use the agent ID with the SignalR connection.

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
| Agent → Orchestrator | `Heartbeat` | Periodic alive signal with metadata |
| Agent → Orchestrator | `JobStatusUpdate` | State transition events |
| Agent → Orchestrator | `LogStream` | Real-time log output from pipeline steps |
| Agent → Orchestrator | `Register` | Initial registration on first connect |

### 2.2 UI ↔ Orchestrator: REST + SignalR

- **REST API** for all CRUD operations (create jobs, query nodes, fetch manifests)
- **SignalR** for real-time UI updates (job progress, log streaming, node status changes)

The UI connects to a separate SignalR hub (or the same hub with different authorization) for live updates.

### 2.3 Connection lifecycle

```
Agent starts
  → Connects to SignalR hub
  → If first connection: sends Register, receives Agent ID
  → Enters idle state, waiting for job assignments
  → Receives AssignJob → processes job → reports status
  → Sends Heartbeat every N seconds
  → On disconnect: automatic reconnection with exponential backoff
  → On reconnect: re-sends any unacknowledged status updates
```

### 2.4 Reconnection and message durability

- SignalR handles transport-level reconnection automatically
- Job state is persisted in SQL Server on the orchestrator side
- If an agent disconnects mid-job, the orchestrator marks the job as `Assigned` (not terminal)
- When the agent reconnects, it queries for any in-progress jobs and resumes
- Heartbeat timeout (configurable, e.g., 60s) triggers agent status change to `Offline`

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
