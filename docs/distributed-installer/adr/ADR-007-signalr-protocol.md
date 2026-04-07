# ADR-007: SignalR as Agent-Orchestrator Communication Protocol

Date: 2026-04-07

## Status

Accepted for PoC.

## Context

Agents need a reliable, real-time bidirectional communication channel with the orchestrator for job assignments, heartbeats, status updates, and log streaming. Options considered included HTTP polling, gRPC streaming, WebSockets directly, and SignalR.

## Decision

Use ASP.NET Core SignalR for all agent-to-orchestrator communication.

SignalR provides:
- native .NET integration with typed hub contracts
- automatic WebSocket transport with fallback (SSE, Long Polling)
- built-in reconnection with configurable retry policies
- real-time bidirectional push in both directions
- connection lifecycle events (connected, disconnected, reconnected)

REST remains for UI-to-orchestrator CRUD operations.

## Consequences

### Positive

- Single technology stack end-to-end (.NET)
- No third-party message broker or protocol dependency
- Automatic transport negotiation and reconnection
- Compile-time safety for message contracts
- Scales well for PoC scope

### Negative

- SignalR requires persistent connections — connection management at scale needs attention
- Not ideal for cross-platform agents (Linux agent in phase 2 would need SignalR client or alternative)
- WebSocket infrastructure requirements (load balancer configuration, connection limits)
