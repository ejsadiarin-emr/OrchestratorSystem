# Agent-initiated connection with push assignment

On-prem and segmented enterprise networks make controller-initiated connectivity brittle. Use an agent-initiated persistent connection model: the Agent initiates and maintains SignalR connectivity to the Orchestrator. The Orchestrator sends `AssignRun` messages over the established channel. The Agent must Ack/Claim with lease ownership before execution. Execution lifecycle uses lease + heartbeat + completion/failure closure semantics. The Agent is headless — all status and telemetry flow through the Orchestrator UI.

**Status**: accepted

**Considered Options**: (1) Orchestrator-push via WinRM/PowerShell remote execution, (2) Agent-poll with interval, (3) Agent-initiated SignalR with push assignment.

**Consequences**: Better resilience across constrained networks (agent initiates outbound). Lower assignment latency than polling. Cleaner ownership and recovery behavior through claim + lease semantics. Requires careful lease/claim and ordering logic to avoid split ownership. No WinRM or remote PowerShell dependency — bootstrap is browser-based.

**Amendments**: Ported from ADR-002 (distributed-installer, 2026-04-06). Re-termed: `AssignJob` → `AssignRun`. Removed WinRM-push as viable approach in favor of browser-based bootstrap (Decision 2). Agent is headless — all operator visibility flows through Orchestrator UI (Decision 1).