# Installation and Operational Storyboards (Canonical)

Date: 2026-04-14  
Status: Canonical draft for Phase 1  
Derived from:
- `docs/distributed-installer/15-installation-and-operational-storyboards.md`
- `docs/distributed-installer/16-installation-and-operational-storyboards-independent.md`
- `docs/distributed-installer/sessions/20260413-storyboard-review-output.md`

Note:
- This is the canonical storyboard baseline for Phase 1.
- Any still-open policy decisions listed in `docs/distributed-installer/17-poc-phase1-prd-v2-capability-addendum.md` remain pending until closed.

## Canonical decisions

- Windows-first PoC scope; Linux support is future and non-blocking.
- Internal-only package source at runtime (agents download artifacts from orchestrator only).
- SignalR is command/control/status channel only.
- Artifact payload transfer is HTTP endpoint-based; use range/chunk strategy for large payloads.
- Package execution is policy-driven using tags:
  - `retryabilityClass`
  - `idempotencyMode`
  - `riskLevel`
  - `approvalRequired`
- Orchestrator self-update uses staged swap + supervisor/wrapper pattern (not in-place replacement).

## Required flows (Phase 1)

1. Packaging media
   - Signed self-contained EXE as primary.
   - ZIP supported.
   - ISO deferred unless explicitly added to Phase 1 acceptance.

2. Fresh orchestrator install
   - Initialize config (port, auth bootstrap, SQLite path, artifact path, telemetry mode).
   - Verify health/UI/API/database/artifact-path readiness.

3. Fresh agent bootstrap (sub-node)
   - Manual script bootstrap is accepted in PoC.
   - One-time token enrollment then steady-state mTLS identity.
   - Verify service status, node online status, lease heartbeat, token invalidation.

4. Internal package ingestion and deployment
   - Admin uploads package + metadata/signature/hash.
   - Orchestrator stores immutable package version record.
   - Agent pipeline executes:
     - `PreConditionCheck`
     - `AcquireArtifact`
     - `ValidateSignatureAndHash`
     - `DetectCurrentState`
     - `InstallOrUpgrade`
     - `PostInstallVerify`
     - `EmitFinalization`

5. Update and modify flows
   - Pre-mutation snapshot is mandatory for update/modify where applicable.
   - Downgrade defaults to disallowed unless explicit approval path is provided.
   - High-risk/non-idempotent operations are never blindly auto-retried.

6. Orchestrator self-update
   - Download and verify candidate package.
   - Stage candidate beside current binary.
   - Supervisor performs process handoff and rollback on startup failure.
   - Emit audit trail for each transition.

## Normative runtime semantics

### Ping vs LeaseHeartbeat

- `Ping` (orchestrator -> agent): liveness probe and connectivity status signal for operator visibility.
- `LeaseHeartbeat` (agent -> orchestrator): assignment lease renewal signal used for ownership, stale detection, and reassignment policy.
- A missing `Ping` updates connectivity posture; missing `LeaseHeartbeat` drives lease-state transitions.

## Verification gates (must exist per flow)

- Every major step has an observable pass/fail gate.
- Gate evidence is API response, service/process state, persisted state transition, or auditable event.
- Step timeline must be deterministic with correlation fields (`jobId`, `nodeId`, `step`, `reasonCode`).

## Security baseline in storyboard context

- Package validation fails closed on signature/hash mismatch.
- Child process execution is constrained (privilege, arguments, timeout/resource bounds) and auditable.
- Trust boundaries must be explicit in architecture and map to controls.

## Known deferred items

- Linux agent implementation.
- Multi-orchestrator scale/HA semantics.
- Advanced observability indexing/retention workflows beyond Phase 1.
