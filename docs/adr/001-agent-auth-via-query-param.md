# ADR-001: Agent Authentication via `agent_id` Query Parameter

**Status:** Accepted (PoC) — Production Blocker  
**Date:** 2026-05-15  
**Context:** DeploymentPoC orchestrator ↔ agent communication

## Problem

Agents need to authenticate with the orchestrator when polling for work and submitting results.

## Decision

Agents authenticate by appending `agent_id={NodeId}` (a GUID) as a query parameter on every API call. No bearer token, no agent secret, no mutual TLS.

Example: `GET /api/workload-runs/pending?agent_id=<guid>`

The `NodeId` is stored in `agent.json` on the agent machine, written during enrollment (`--enroll <token>`).

## Endpoints Using `agent_id`

The following agent-facing endpoints accept `agent_id` as a query parameter:

| Endpoint | `agent_id` Required | Updates `LastSeenUtc` | Purpose |
|---|---|---|---|
| `GET /api/workload-runs/pending` | Yes | Yes (with stale-threshold gate per ADR-003) | Heartbeat + work dispatch |
| `PATCH /api/workload-runs/{runId}` | Optional | No | Status updates |
| `POST /api/workload-runs/{runId}/timeline` | Yes | No | Step progress |

Only `/pending` is designated as the heartbeat endpoint (ADR-002).

## Consequences

### Positive
- Simple to implement — no token lifecycle, no secret rotation
- Works with HTTP GET (query params survive GET requests naturally)
- No additional infrastructure needed (no token issuer, no JWKS endpoint)

### Negative
- **Node IDs are exposed in URLs** — visible in server logs, proxy logs, browser history, network sniffers
- **No replay protection** — anyone who captures a URL can impersonate the agent
- **No authorization boundary** — an agent with another agent's NodeId can poll/submit on its behalf
- **Query parameters are less secure than headers** — more likely to be logged by intermediaries

## Production Remediation

Before production use, replace with one of:
1. **Bearer tokens (JWT)** — agent receives a signed token during enrollment, includes it in `Authorization` header
2. **mTLS** — mutual TLS with client certificates per agent
3. **Agent secret** — shared secret provisioned during enrollment, used in HMAC-signed requests

## Related

- ADR-002: HTTP polling as permanent architecture (heartbeat designation)
- ADR-003: Node Liveness Model (heartbeat mechanism and stale-threshold)
- Enrollment flow: `EnrollmentController.cs`
- Agent config: `agent.json` schema (`NodeId`, `OrchestratorUrl`)