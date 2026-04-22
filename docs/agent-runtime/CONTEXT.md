# Agent Runtime — Context

The Agent Runtime is a headless Windows service that receives workload run assignments from the Orchestrator and executes them locally. It has no UI — all operator visibility flows through the Orchestrator.

## Language

**Agent**:
A headless persistent Windows service (C# backend) that receives run assignments and executes them locally.
_Avoid_: node, worker, client (those mean different things in the Orchestrator context)

**Headless**:
The Agent has no web UI of any kind — no local dashboard, no tray icon redirect, no web server. All operator interaction is via the Orchestrator UI or API.
_Avoid_: "embedded UI", "local UI", "agent dashboard"

**Enrollment**:
The process by which an Agent uses a single-use enrollment token to authenticate and establish a secure channel with the Orchestrator.
_Avoid_: registration, pairing

**Bootstrap**:
Browser-based download of a signed `agent.exe` from an Orchestrator URL endpoint, followed by enrollment-token authorization. NOT a WinRM/PowerShell push script.
_Avoid_: remote install, push install, bootstrap script

**Execution Pipeline**:
The local engine that unpacks artifacts, runs installers (MSI/EXE via typed adapters), and reports step-level status back to the Orchestrator.
_Avoid_: job runner, task executor

**Lease**:
A time-bounded assignment lock that ensures at most one active run per `(nodeId, workloadId)`. Agents must heartbeat within TTL or risk reassignment.
_Avoid_: lock, reservation

## Relationships

- **Agent → Orchestrator**: enrollment request (token + cert exchange), heartbeat, run status ingestion, telemetry
- **Agent → Artifact Catalog**: direct artifact download by reference from Orchestrator assignment (HTTP range requests)

## Example dialogue

> **Dev:** "Can the Agent show a local web page with its status?"
> **Domain expert:** "No — the Agent is headless. All status flows through the Orchestrator UI. The Agent only runs a Windows service and communicates via SignalR/HTTP."

> **Dev:** "How does an operator install the Agent on a remote node?"
> **Domain expert:** "The operator opens the Orchestrator UI, generates an enrollment token, then opens a browser-based download URL on the target node. The downloaded agent.exe is signed and includes the token. No WinRM or push script."

## Flagged ambiguities

- "Bootstrap" previously meant a WinRM/PowerShell push script (per old ADR-010). Resolved: bootstrap is now browser-based download with enrollment-token authorization. ADR-010 updated accordingly.
- "Agent UI" was in early PRD requirements (FR-009, AC-106). Resolved: the Agent has no UI. FR-009 and AC-106 were removed; all operator visibility is via the Orchestrator.

## Execution Pipeline Steps

The Agent executes workload run assignments through a fixed local pipeline:

1. **PreConditionCheck** — dry-run validation (OS version, disk space, dependencies)
2. **AcquireArtifact** — download artifact from catalog via HTTP range requests
3. **ValidateSignatureAndHash** — cryptographic verification (signature/hash fail-closed)
4. **DetectCurrentState** — check if already installed (idempotency)
5. **InstallOrUpgrade** — execute installer adapter (MSI/EXE)
6. **PostInstallVerify** — confirm installation succeeded
7. **EmitFinalization** — emit final telemetry and audit events

Steps execute in strict order. A failed step halts the pipeline and reports the failure reason.

## Interface Contracts (Draft)

These interfaces define the Agent pipeline extensibility surface:

```csharp
public interface IInstallStep
{
    string Name { get; }
    Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct);
}

public interface IPreCheck
{
    string Name { get; }
    ConfidenceLevel Confidence { get; }
    Task<PreCheckResult> ExecuteAsync(RunContext context, CancellationToken ct);
}
```

Config migration contract (for mutation safety):

```csharp
public interface IConfigMigration
{
    string FromVersion { get; }
    string ToVersion { get; }
    Task<MigrationResult> ExecuteAsync(MigrationContext context, CancellationToken ct);
}

public sealed record MigrationContext(
    string RunId,
    string NodeId,
    ConfigSnapshot Snapshot,
    string TargetSchemaVersion);
```

Migration rules: deterministic, side-effect bounded, strict `vN -> vN+1` chain, reversible by snapshot restore, and must not proceed without verified snapshot integrity.

## Bounded Context

The Agent Runtime owns:
- Windows service lifecycle (start, stop, crash recovery)
- Enrollment handshake (token validation + mTLS channel setup)
- Local execution pipeline (artifact unpack, install/upgrade via typed adapters, verify, including preUpgradeActions dispatched by Orchestrator)
- Telemetry and log streaming back to Orchestrator (workload correlation fields required)
- Config snapshot creation before mutation steps
- Lease heartbeat and reconnection behavior
- Execution follows a single update workflow (no major/minor distinction)

It does NOT own:
- UI of any kind
- Policy decisions (risk level evaluation uses sensible defaults in Phase 1; per-artifact retry and idempotency configuration is not in scope)
- Workload definition authoring or versioning
- Artifact catalog management
- Run planning or scheduling

## Invariants

1. The Agent is headless; there is no local web UI.
2. Enrollment requires a valid, unexpired enrollment token from the Orchestrator.
3. The Agent never makes policy decisions; it executes what the Orchestrator dispatches.
4. All artifact execution is local; the Agent does not proxy remote resources.
5. Bootstrap is browser-based (download signed agent.exe + enrollment token), not WinRM push.
6. The Agent communicates with the Orchestrator via SignalR (control/status) and HTTP (artifact download).
