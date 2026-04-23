# Decision: Integration Testing for Agent Enrollment

**Date:** 2026-04-23
**Status:** Decided
**Scope:** Integration tests verifying the orchestrator-to-agent enrollment flow using Testcontainers.

## Context

We need integration tests that demonstrate how agents are installed on a "remote" node (in this case a container) and connected to the orchestrator. This is a prerequisite to running a workload on the remote agent node, initiated by the orchestrator.

## Decisions

### 1. Orchestrator Test Topology
- **Decision:** Keep orchestrator in-memory via `CustomWebApplicationFactory` (extends `WebApplicationFactory`), but configure it with a real Kestrel endpoint bound to `0.0.0.0` so the agent container can reach it.
- **Rationale:** Preserves fast in-memory SQLite database and existing test infrastructure. Only the agent is containerized.

### 2. Agent Container Image Strategy
- **Decision:** Use the real agent binary cross-compiled for `linux-x64` inside a Docker container.
- **Rationale:** The agent is pure .NET (`BackgroundService`, `HubConnection`, `HttpClient`, `PipelineExecutor`) with no native Windows dependencies. This gives the highest-fidelity test — the exact same agent runtime that would run on a node.

### 3. Enrollment Mechanism Inside the Container
- **Decision:** Add `--enroll` CLI support to the agent's `Program.cs`.
- **Rationale:** The agent should be self-enrolling, matching the README's documented flow (`agent.exe --enroll <token> --orchestrator-url <url>`). The CLI path is required for headless container testing.
- **Future:** UI-based enrollment (browser redirect or native setup wizard) is planned but out of scope for this integration test.

### 4. NodeId Persistence Mechanism
- **Decision:** Persist enrollment result (NodeId + OrchestratorUrl) to a local JSON config file.
- **Rationale:** Survives restarts. On startup, if config file exists → use stored values and connect directly. If absent → require `--enroll`.
- **Cross-platform paths:**
  - Windows: `%LOCALAPPDATA%/DeploymentPoC/agent.json`
  - Linux (containers): `/etc/deploymentpoc/agent.json` or `/var/lib/deploymentpoc/agent.json`
- **Test implication:** Verify that after enrollment + restart, the agent reconnects using persisted config without needing the token again.

### 5. Docker Networking
- **Decision:** Dynamic host IP detection at runtime.
- **Rationale:** `host.docker.internal` works on Docker Desktop (Mac/Windows) but is often missing on Linux Docker daemon. The test will detect the OS and use the correct host IP (`host.docker.internal` or bridge gateway IP like `172.17.0.1`).

### 6. Docker Image Build Strategy
- **Decision:** Hybrid — pre-build via CI/script, but auto-build on-demand if image is missing locally.
- **Rationale:** Fast in CI (pre-built), convenient for local development (auto-build prevents stale image issues).
- **Artifact:** A `Dockerfile` will be added under `tests/agent/`.

### 7. Agent Container Lifecycle per Test
- **Decision:** Fresh container per test method (`IAsyncLifetime` / `[SetUp]` / `[TearDown]`).
- **Rationale:** Enrollment is a one-time, stateful operation. A container that has already enrolled cannot meaningfully test enrollment again without wiping its persisted config. Maximum isolation is worth the Docker start/stop overhead.

### 8. Enrollment Reset Mechanism
- **Decision:** Add `--reset-enrollment` CLI flag to the agent.
- **Rationale:** Gives operators a clean way to force re-enrollment without manually hunting down config files. The test runs `docker exec agent --reset-enrollment`, then restarts the container with `--enroll <new_token>`.
- **Test flow for reset:**
  1. Issue Token A, start container with `--enroll A` → Online.
  2. `docker exec agent --reset-enrollment`.
  3. Issue Token B, restart container with `--enroll B` → Online again.
  4. Verify old NodeId is gone, new NodeId exists.

### 9. Test Scope (First Milestone)
- **Decision:** Start with **Enrollment Flow Only (A)**.
- **Verification points:**
  1. Orchestrator issues enrollment token via API.
  2. Agent container starts with `--enroll <token> --orchestrator-url <url>`.
  3. Agent consumes token, receives NodeId, persists config.
  4. Agent connects to SignalR hub and calls `Identify`.
  5. Orchestrator shows node as "Online".
- **Future scope (B):** Full workload execution (AssignRun → pipeline → Complete) will be a follow-up test.

## Decisions (continued from grill-me session)

### 10. Agent restart behavior when already enrolled
- **Decision:** Auto-reconnect is the implicit default.
- **Agent startup logic:**
  - `--reset-enrollment` → wipe config, exit.
  - `--enroll <token>` + `--orchestrator-url <url>` → consume token, write config, start runtime.
  - No enrollment flags + config file exists → read `agent.json`, start runtime (auto-reconnect).
  - No enrollment flags + no config file → exit with error: "Not enrolled. Run with --enroll <token> or --orchestrator-url <url>."
- **Rationale:** Clean container behavior: first start uses `--enroll`, restarts just run the binary directly. Test will verify restart reconnects without `--enroll`.

### 11. Asserting node "Online" status
- **Decision:** Poll `GET /api/nodes` (or `GET /api/nodes/{id}`) in a retry loop with timeout (e.g., 30s). Wait for `status == "Online"`.
- **Orchestrator change:** Update `AgentRuntimeHub.OnConnectedAsync` (or `Identify` handler) to set `Node.Status = "Online"` and `Node.LastSeenUtc` when the agent first connects, in addition to the existing heartbeat update path.
- **Rationale:** Simple, reliable, no SignalR client needed in the test. Tests the same public REST contract operators use.

### 12. UI-based enrollment (Phase 2 consideration)
- **Decision:** CLI enrollment (`--enroll`) is the Phase 1 implementation. A UI-based "device code flow" is deferred to Phase 2.
- **Proposed Phase 2 flow (device code pattern):**
  1. Operator runs `agent.exe` with no enrollment flags.
  2. Agent detects no config file, generates random `sessionId`, opens system browser to `orchestrator.com/enroll?session=<sessionId>`.
  3. Agent polls `GET /api/enrollment-sessions/{sessionId}/status` every 5s.
  4. User inputs token on orchestrator page; page POSTs token + sessionId to backend.
  5. Backend consumes token, stores nodeId against sessionId.
  6. Agent poll receives `{ status: "completed", nodeId: "..." }` → writes config → starts runtime.
- **Required orchestrator endpoints for Phase 2:**
  - `POST /api/enrollment-sessions` — agent creates session
  - `GET /api/enrollment-sessions/{id}/status` — agent polls
  - `POST /api/enrollment-sessions/{id}/complete` — UI submits token
- **Additional complexities:** session TTL/cleanup, polling timeout, CLI fallback for headless environments.
- **Rationale:** Consistent with ADR-0001 (headless agent, no local UI). All operator interaction stays in orchestrator React UI. Follows established pattern (GitHub CLI, Azure CLI, Tailscale).

## Open Questions

1. What happens if enrollment token is expired or already consumed when the agent tries to consume it?
2. What should the agent do if `--enroll` is passed but a config file already exists?
3. Should the Dockerfile use a multi-stage build or a simple `dotnet publish` + `COPY`?
4. How do we handle the agent's health endpoint (`/health`) in the container — do we use it for readiness checks in the test?

## Related Files

- `apps/agent/backend/Program.cs` — agent entry point (to be modified for `--enroll` CLI)
- `apps/agent/backend/Services/AgentRuntimeService.cs` — SignalR runtime
- `apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs` — SignalR hub
- `apps/orchestrator/backend/Controllers/EnrollmentController.cs` — token lifecycle
- `tests/orchestrator/integration/Infrastructure/CustomWebApplicationFactory.cs` — test infrastructure
- `tests/agent/Dockerfile` — to be created

## Dependencies

- Testcontainers .NET package to be added to integration test csproj.
- Agent Dockerfile to be created.
