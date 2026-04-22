# Context Map

This repository contains a single product: the **Distributed Installer**.
Domain boundaries are split into three contexts.

## Contexts

| Context | Directory | Scope |
|---------|-----------|-------|
| Orchestrator Control Plane | `docs/orchestrator-control-plane/` | API, embedded UI, run planning, policy evaluation, enrollment, artifact store management |
| Agent Runtime | `docs/agent-runtime/` | Headless Windows service, browser-based bootstrap, execution pipeline, runtime telemetry |
| Artifact & Workload Catalog | `docs/artifact-workload-catalog/` | Artifact ingest, manifest validation, workload definition import (global JSON), versioning |

## Context Relationships

- **Orchestrator → Agent**: Enrollment tokens, run assignments (SignalR), heartbeat/telemetry ingestion. Agent has no UI; all operator visibility flows through the Orchestrator.
- **Orchestrator → Artifact Catalog**: Manifest retrieval for risk/policy checks; artifact ingest via drag-and-drop UI; workload definition import.
- **Agent → Orchestrator**: Status reports, heartbeat, telemetry via SignalR. Agent downloads artifacts via HTTP (range requests).
- **Agent → Artifact Catalog**: Direct artifact download by reference from Orchestrator assignment.

## Key Decisions (April 2026)

These decisions are documented in detail in each context's ADR directory and in `docs/prd-phase1.md`.

1. **Agent is headless** — no local web UI. All operator visibility and control flows through the Orchestrator.
2. **Browser-based bootstrap** — agents are installed by downloading a signed agent.exe via Orchestrator URL with embedded enrollment token. No WinRM/PowerShell push scripts.
3. **Single update workflow** — no major/minor version distinction in Phase 1. Update flow: pre-check → detect risk → display status → proceed automatically.
4. **Simplified manifest schema** — `retryabilityClass` and `idempotencyMode` removed from PoC Phase 1. Only `riskLevel` remains as manifest-level policy metadata.
5. **Pre-Upgrade Actions** — replaces the earlier concept of "upgradePath". Declarative requirements (e.g., "backup database") are mapped per workload revision, not per artifact.
6. **Global workload JSON** — workload definitions are imported as a single JSON file containing 2–3 workloads for PoC.
7. **Orchestrator UI is the key demo goal** — running a workload from the Orchestrator UI is the primary Phase 1 proof point. All operator visibility and control flows through this surface.

## Cross-Cutting Concerns

System-wide decisions live in `docs/adr/`:

| ADR | Decision |
|-----|----------|
| 0001 | OTel as standard observability layer |
| 0002 | Hybrid control plane (custom Orchestrator + Agent) |
| 0003 | Self-contained packaging (Orchestrator + Agent) |
| 0004 | Security baseline (signed artifacts, RBAC, least privilege) |

Context-specific ADRs:

| Context | ADR | Decision |
|---------|-----|----------|
| Agent Runtime | 0001 | Agent has no local UI (headless service) |
| Agent Runtime | 0002 | Browser-based bootstrap (replaces WinRM) |
| Agent Runtime | 0003 | Agent-initiated connection (pull-first) |
| Agent Runtime | 0004 | Installer adapter strategy (MSI + EXE first) |
| Agent Runtime | 0005 | SignalR runtime protocol |
| Agent Runtime | 0006 | Agent in-memory channel buffer |
| Agent Runtime | 0007 | Persistent agent model (not ephemeral) |
| Agent Runtime | 0008 | Enterprise bootstrap vs runtime orchestration boundary |
| Orchestrator | 0001 | Single update workflow (no major/minor split) |
| Orchestrator | 0002 | Orchestrator queue (Hangfire) |
| Orchestrator | 0003 | Dry-run confidence framework |
| Artifact Catalog | 0001 | Simplified manifest policy tags (riskLevel only) |
| Artifact Catalog | 0002 | Upgrade phasing and realism targets |
| Artifact Catalog | 0003 | Package auto-detection and metadata enrichment |

Product canon lives at `docs/prd-phase1.md` and `docs/implementation-tracker-phase1.md`.

## Migration Note

Historical docs remain in `docs/distributed-installer/` as a read-only legacy snapshot.
All canonical documentation now lives under `docs/` per this map.
The PRD (`docs/prd-phase1.md`) is the canonical policy document. If any CONTEXT.md conflicts with the PRD, the PRD wins.
