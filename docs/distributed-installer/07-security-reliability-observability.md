# Security, Reliability, and Observability Baseline

Date: 2026-04-07

## Why this matters

A distributed installer is a high-privilege control system. If compromised or unreliable, it can create fleet-wide incidents quickly. Therefore, security and reliability are first-class architectural constraints, not bolt-on features.

## 1) Security baseline

## Identity and trust boundaries

- Authenticate operator sessions to orchestrator
- Authenticate agent-to-orchestrator channel via SignalR (mutual trust, not anonymous node submission)
- Separate roles (Admin, Operator, ReadOnly) with least privilege
- Bootstrap uses one-time registration tokens

## Artifact trust

- Enforce package signature validation
- Enforce hash verification before execute
- Fail closed on validation errors

## Execution hardening

- Prefer least-privilege execution for non-privileged steps
- Explicitly mark and audit privileged operations
- Sanitize process invocation arguments for legacy adapters
- Run install jobs in isolated child processes

## Secret and sensitive data handling

- No plaintext secrets in config files or logs
- Use platform-secure secret protection patterns (Windows DPAPI, Azure Key Vault, etc.)
- Apply log redaction for sensitive fields

## Audit integrity

- Append-only event stream semantics
- Record actor identity, role, request ID, target node, package, and outcome
- Preserve tamper-evidence strategy in design (hash-chain or integrity-compatible store pattern)

## 2) Threat model highlights (STRIDE lens)

## Spoofing

Risk: rogue node impersonates agent.  
Control: strong node identity, registration tokens, SignalR authentication.

## Tampering

Risk: altered installer artifact.  
Control: signature + checksum verification.

## Repudiation

Risk: operator denies action.  
Control: immutable-style audit and correlation IDs.

## Information disclosure

Risk: secrets leaked in logs/telemetry.  
Control: structured redaction and secret minimization.

## Denial of service

Risk: queue flood, heartbeat flood, retry storm.  
Control: rate limiting, bounded queues, retry discipline, SignalR connection limits.

## Elevation of privilege

Risk: unsafe legacy step gains excessive privileges.  
Control: constrained execution policies, adapter hardening, child process isolation.

## 3) Reliability model

## Delivery semantics

Target: **at-least-once delivery + idempotent handlers**.

Avoid claiming global exactly-once execution for complex distributed installs.

## Retry strategy

- exponential backoff with jitter
- retry caps
- classify retriable vs non-retriable errors
- protect downstream systems from retry amplification
- Hangfire manages retries on orchestrator side

## Failure handling

- explicit state machine with terminal consistency
- rollback/compensation where native rollback unavailable
- bounded timeout policies
- cancellation handling with safe checkpoints
- agent disconnect: job remains non-terminal until heartbeat timeout

## SignalR resilience

- automatic reconnection with configurable retry policy
- unacknowledged messages re-sent on reconnect
- heartbeat timeout triggers agent status change to `Offline`
- jobs in-progress are re-queried by agent on reconnect

## 4) Observability baseline (OTel-first)

## Trace model

- One root span per install job
- Child spans for each pipeline step
- Propagate context between orchestrator and agent via SignalR message metadata

## Metrics model

- `installer.job.duration`
- `installer.step.duration`
- `installer.job.success_rate`
- `installer.job.failure_rate`
- `installer.retry.count`
- `orchestrator.queue.depth`
- `agent.heartbeat.latency`
- `precheck.confidence_accuracy` — tracks how often pre-check predictions match reality

## Log model

Required fields:

- timestamp (UTC)
- level
- service/component
- job ID
- node ID
- package/version
- correlation/request ID
- step name
- result + error code

## 5) PoC minimum acceptance controls

A PoC run should be considered valid only if:

1. Unsigned/invalid artifact is blocked
2. Unauthorized role cannot trigger install
3. Failed install generates actionable telemetry and audit event
4. Retry behavior is bounded and visible
5. Rollback/compensation path is demonstrably exercised
6. Agent bootstrap succeeds on a fresh VM
7. SignalR disconnect/reconnect preserves job state

## 6) Production hardening backlog

- stronger key lifecycle governance
- supply-chain provenance attestations (SBOM)
- immutable retention and legal/audit policy alignment
- broader incident response and forensic workflows
- policy-based rollout rings and blast-radius controls
- hardware-backed key protection where available
- WORM-class audit retention

## 7) Source-backed guidance themes used

This baseline is aligned with guidance families from:

- Microsoft security and endpoint docs
- OWASP cheat sheets for logging/secrets/threat modeling
- OpenTelemetry collector and signal docs
- distributed reliability patterns (timeouts/retries/backoff/jitter)
- SignalR security and scalability best practices
