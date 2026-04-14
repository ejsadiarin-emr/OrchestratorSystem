# Installation and Operational Storyboards (Independent Review)

Date: 2026-04-13  
Status: Superseded draft (reference only)  
Authoring mode: Independent storyboard based on `sessions/20260413-raw-notes-shared-understanding.md` and current proposal set, intentionally separate from `15-installation-and-operational-storyboards.md`

> Superseded by `docs/distributed-installer/18-installation-and-operational-storyboards-canonical.md` for Phase 1 canonical behavior.
> Keep this document as historical comparison/reference only.

## Purpose

This document provides an independent storyboard and recommendations for PoC execution so it can be compared side-by-side with the existing storyboard draft.

Scope covered:
- Packaging media decisions (ISO/EXE/ZIP)
- Fresh orchestrator install
- Fresh agent/sub-node install
- Update and workload modification flows
- Package sourcing with no external dependencies
- Retry/idempotency/risk tagging model
- Security overlays (SignalR + mTLS + package trust + child process hardening)

## Inputs and Constraints

Primary inputs:
- `docs/distributed-installer/sessions/20260413-raw-notes-shared-understanding.md`
- Core proposal docs in `docs/distributed-installer/` (`03`, `04`, `05`, `07`, `08`, `09`, `10`, `11`, `12`, `poc-phase1-prd-and-implementation-tracker.md`)
- Decision lock addendum: `docs/distributed-installer/sessions/20260411-decision-lock-addendum.md`

Hard constraints from notes:
- PoC should support internal-only package flow (no external package source dependency at runtime)
- Flow-level verification is required at each major step
- Security model must explain SignalR + mTLS and child process trust
- SQLite should be the default PoC persistence target
- Manual remote bootstrap script is acceptable in PoC; GPO/SCCM recorded as considered alternatives

## Storyboard 1: Packaging and Delivery Media

### Decision

For PoC, primary delivery is signed self-contained EXE plus ZIP bundle. ISO is optional for fully offline media handling but is not required to validate architecture.

### Flow

1. Build orchestrator and agent as self-contained signed binaries.
2. Produce package bundle with:
   - `Orchestrator.exe`
   - `Agent.exe`
   - package manifests
   - checksums and signatures
3. Distribute via internal share/USB/secure transfer.
4. Validate integrity and signer trust before execution.

### Diagram

```text
Build Pipeline -> Signed EXE/ZIP -> Internal Transfer -> Integrity Check -> Ready to Install
```

### Verification Gates

- Signature chain is valid and trusted.
- Hash matches manifest metadata.
- Version floor policy blocks known-vulnerable downgrade for framework binaries.

## Storyboard 2: Fresh Orchestrator Install (Main Node)

### Flow

1. Sysadmin stages `Orchestrator.exe` on target host.
2. Runs initialization command or first-run setup.
3. Configures:
   - listen URL/port
   - admin bootstrap auth
   - SQLite DB path
   - artifact storage path
   - OTel export mode (file or OTLP endpoint)
4. Starts orchestrator service/process.
5. Runs startup verification checks.

### Diagram

```text
Admin Host
   |
   v
Run Orchestrator.exe --init
   |
   v
Write config + SQLite path + Artifact path
   |
   v
Start Orchestrator
   |
   v
Health/API/UI checks
```

### Verification Gates

- `GET /health` returns healthy.
- UI is reachable.
- SQLite DB initializes and accepts writes.
- Artifact storage path is writable and ACL-correct.
- Initial audit event for bootstrap completion exists.

## Storyboard 3: Fresh Sub-Node Install (Agent Bootstrap)

### Flow

1. Sysadmin generates one-time enrollment token from orchestrator.
2. Runs PoC bootstrap script on remote machine (manual script is acceptable for PoC).
3. Script installs `Agent.exe`, writes bootstrap config, creates service, starts service.
4. Agent connects using enrollment token.
5. Orchestrator validates token, binds persistent agent identity, issues mTLS material.
6. Agent reconnects in steady-state using mTLS identity.

### Diagram

```text
Sysadmin -> Generate Enrollment Token -> Run Bootstrap Script on Remote Node
         -> Install Agent Service -> First Connect (token)
         -> Cert Enrollment -> Reconnect (mTLS) -> Node Online
```

### Verification Gates

- Windows service is running.
- Node appears online in orchestrator.
- Lease heartbeat is present.
- Enrollment token is consumed and invalidated.
- Reconnect without valid cert identity is rejected.

## Storyboard 4: Internal Package Ingestion (No External Source)

### Flow

1. Admin uploads package artifact and manifest to orchestrator.
2. Orchestrator computes digest and stores immutable package version entry.
3. Orchestrator records metadata (size, hash, signer, policy class).
4. Agents fetch artifacts only from orchestrator artifact endpoint.

### Diagram

```text
Admin Upload -> Orchestrator Store -> Hash/Signature Metadata -> Immutable Version Record
                                                           |
                                                           v
                                                 Agent Pull from Orchestrator Only
```

### Verification Gates

- Package upload event is audited.
- Immutable package version record exists.
- Hash and signature metadata are persisted.
- Agent is blocked from non-orchestrator package origins by policy.

## Storyboard 5: Fresh Install Job (Orchestrator -> Agent)

### Flow

1. Sysadmin submits install job with targets and manifest.
2. Orchestrator validates schema, policy, and target eligibility.
3. Orchestrator dispatches assignment over SignalR.
4. Agent executes local step pipeline:
   - `PreConditionCheck`
   - `AcquireArtifact` (HTTP/chunked if large)
   - `ValidateSignatureAndHash`
   - `DetectCurrentState`
   - `InstallOrUpgrade`
   - `PostInstallVerify`
   - `EmitFinalization`
5. Agent streams step status and logs metadata back to orchestrator.

### Diagram

```text
UI/API Submit Job
      |
      v
Orchestrator Validate + Assign (SignalR)
      |
      v
Agent Pipeline:
Precheck -> Acquire -> Validate -> Detect -> Install -> Verify -> Finalize
      |
      v
Step Status + Logs -> Orchestrator -> UI Timeline
```

### Verification Gates

- Ordered step timeline is visible.
- Terminal state is deterministic and auditable.
- Trace correlation (`jobId`, `nodeId`, `step`, `reasonCode`) is complete.

## Storyboard 6: Update Flow (Example: Node 22 -> 24)

### Flow

1. Submit update job with target version policy.
2. Agent detects current version and compatibility constraints.
3. Snapshot config/state before mutation.
4. Execute update path.
5. Verify target runtime/version after install.
6. On failure, restore from snapshot and mark outcome.

### Diagram

```text
Detect Current Version -> Snapshot Config -> Apply Update -> Verify Version
                                    |
                                    +-- if failed -> Restore Snapshot -> Fail with reason
```

### Verification Gates

- Snapshot exists before mutation.
- Migration/upgrade result is auditable.
- Post-install version check passes.
- Restore path works for forced failure test.

## Storyboard 7: Workload Modification and Downgrade Handling

### Policy Classes

Each package/job should be tagged with policy fields before execution:
- `retryabilityClass`: `none | transient_only | bounded`
- `idempotencyMode`: `detect | always | never | version_check`
- `riskLevel`: `low | medium | high`
- `approvalRequired`: `true | false`

### Flow

1. Sysadmin submits modify job (config change/version change).
2. Orchestrator evaluates policy class and risk.
3. For high-risk or downgrade operations:
   - explicit approval is required
   - snapshot/rollback path must be confirmed first
4. Agent applies modification and verification checks.

### Diagram

```text
Submit Modify Job -> Evaluate Risk Policy
                      |
                      +-- low/medium -> execute
                      |
                      +-- high/downgrade -> require approval + snapshot
                                              |
                                              v
                                           execute + verify
```

### Verification Gates

- Approval event is recorded when required.
- Risk-tag policy branch is enforced.
- Post-modification health and version checks pass.

## Storyboard 8: Retry, Self-Healing, and Idempotency

### Principles

- Retry only transient failures with bounded backoff.
- Never blind-retry high-risk non-idempotent steps.
- Use idempotency and detection keys to avoid duplicate side effects.

### Flow

1. Step fails with structured error classification.
2. Policy engine evaluates retry eligibility.
3. If eligible, retry with bounded backoff.
4. If not eligible, fail terminal with actionable reason.

### Diagram

```text
Step Fails -> Classify Error -> Check Policy
                           |
                           +-- retryable -> backoff -> retry (bounded)
                           |
                           +-- not retryable -> terminal fail
```

### Verification Gates

- Retry decision is explainable by policy and error class.
- Retry count and intervals respect manifest limits.
- Duplicate-side-effect check remains clean on replays.

## Storyboard 9: Security Overlay Across All Flows

### Channel Security

- SignalR is used for command/control/status, not large artifact payload transfer.
- Steady-state agent auth uses mTLS identity bound to agent registration.
- Sequence and stale-message protections are enforced at ingest.

### Artifact Trust

- All installable payloads require signature and hash validation before execute.
- Validation fails closed.

### Child Process Hardening

- Spawn installer child processes from trusted agent runtime only.
- Constrain privileges and enforce timeout/resource bounds.
- Sanitize invocation arguments and capture auditable execution metadata.

### Verification Gates

- Unsigned/tampered artifact is blocked.
- Unauthorized operation attempts are denied and audited.
- Child process policy violations are visible in telemetry/audit.

### Diagram

```text
Transport: TLS + mTLS
      |
      v
Message Safety: sequence + idempotent ingest
      |
      v
Artifact Safety: signature + hash (fail closed)
      |
      v
Execution Safety: constrained child process + audited events
```

## End-to-End Reference Diagram

```text
 [Admin/UI]
     |
     | REST (jobs, packages, approvals)
     v
 [Orchestrator]
   - API
   - SignalR Hub
   - SQLite (PoC state)
   - Artifact Store
     |
     | SignalR (assign/status/control)
     v
 [Agent Service]
     |
     | HTTP/HTTPS (artifact download, chunk/range)
     v
 [Local Installer Child Process]
     |
     v
 [Target Machine State Updated]
```

## Technical Clarifications from Industry Practice

- SignalR default incoming message limits are intentionally small; do not move large package payloads through SignalR commands.
- Use HTTP artifact endpoints with range/chunk support for large package retrieval.
- SQLite is suitable for PoC orchestration state if write patterns are moderate and tuned; avoid turning it into a long-term high-volume log sink.
- For OTel in PoC, file/OTLP export with rotation is usually safer and simpler than DB-backed log ingestion.

## Recommended PoC Baseline Decisions

1. Use SQLite for orchestrator control-plane persistence in PoC.
2. Keep SignalR for control/status only; use HTTP chunk/range for artifacts.
3. Require explicit package policy tags (`retryabilityClass`, `idempotencyMode`, `riskLevel`, `approvalRequired`).
4. Define signing authority/key custody process now (even if minimal for PoC).
5. Implement orchestrator self-update using staged swap/supervisor pattern, not in-place binary overwrite.

## Open Review Questions

1. Should ISO be explicitly included in PoC acceptance or treated as future hardening convenience?
2. What package-size threshold triggers mandatory range/chunk retrieval?
3. Should downgrade be globally disabled by default unless package-specific override is approved?
4. What minimum audit retention window is required for PoC signoff?
5. Do we accept file-based OTel as default PoC path and defer DB-backed log indexing?

## Comparison Notes

This independent storyboard differs in emphasis by:
- Explicitly separating control-plane transport (SignalR) from artifact transport (HTTP range/chunk)
- Making package policy/risk tags first-class in modify/update flows
- Tightening verification gates per flow for reviewer-friendly traceability
- Treating SQLite and OTel storage choices as deliberate PoC tradeoffs instead of implicit defaults
