# Artifact & Workload Catalog — Context

The Artifact & Workload Catalog owns every concern around what gets deployed — from binary artifact storage to workload definition schemas — but not how or where it runs.

## Language

**Artifact**:
A versioned binary package (MSI, EXE, ZIP) stored in the catalog.
_Avoid_: package, binary, installer (use those terms only for specific roles)

**Manifest**:
A JSON document describing an artifact — its type, version, detection rules, install adapter, and risk level.
_Avoid_: metadata, descriptor

**Risk Level**:
Declarative risk classification on a manifest (`low | medium | high`). Set by operator or elevated by signature verification warnings. Cannot be overridden at runtime.
_Avoid_: severity, priority

**Workload Definition**:
A versioned JSON document declaring an ordered set of packages (by manifest reference) and optional pre-upgrade actions. Imported as a global JSON file containing 2–3 workload definitions for PoC.
_Avoid_: workload config, deployment template

**Workload Revision**:
An immutable published snapshot of a workload definition's package list and ordering. Once published, content cannot change.
_Avoid_: workload version (use revision for immutability emphasis)

**Pre-Upgrade Action**:
A declarative requirement that must be satisfied before an install or upgrade proceeds — e.g., "backup database" or "stop service". Mapped per workload revision, not per artifact.
_Avoid_: upgrade path, upgrade step, pre-condition

**Global Workload File**:
A single JSON file (e.g., `20260421-001-workloads.json`) containing multiple workload definitions, imported via drag-and-drop or API in the Orchestrator UI.
_Avoid_: workload bundle, deployment file

## Relationships

- A **Workload Definition** references one or more **Manifests** by package ID/slug.
- A **Pre-Upgrade Action** belongs to a **Workload Revision**, not to an individual artifact.
- An **Artifact** and its **Manifest** are ingested together (binary + metadata).
- The **Orchestrator** interprets **Risk Level** and **Pre-Upgrade Actions** during run planning; the catalog does not evaluate policy.

## Example dialogue

> **Dev:** "When a **Workload Definition** is imported, do we validate that every referenced **Manifest** exists?"
> **Domain expert:** "Yes — unresolved manifest references block publication. You can create a draft, but publishing requires all packages to be in the catalog."

> **Dev:** "What happens if a **Risk Level** is `high` because of a signature warning?"
> **Domain expert:** "The Orchestrator displays that risk in the UI, but the update proceeds automatically — no manual approval gate."

## Representative schemas

### Manifest (simplified — PoC Phase 1)

```json
{
  "slug": "grafana-13.0.1-24542347077-windows-amd64",
  "type": "exe",
  "version": "13.0.1",
  "detection": {
    "method": "registry",
    "key": "HKLM\\Software\\Grafana",
    "value": "Version"
  },
  "policyTags": {
    "riskLevel": "medium"
  },
  "install": {
    "adapter": "msiexec",
    "args": "/i grafana-13.0.1.msi /quiet /norestart"
  }
}
```

### Workload definition (global JSON import)

```json
{
  "workloads": [
    {
      "name": "DeltaV Observability Stack",
      "version": "2",
      "slug": "deltav-observability-stack",
      "packages": [
        "grafana-13.0.1-24542347077-windows-amd64",
        "loki-windows-amd64"
      ],
      "preUpgradeActions": [
        {
          "type": "backup",
          "description": "Backup Loki data directory before upgrade",
          "path": "C:\\ProgramData\\loki\\data"
        }
      ]
    },
    {
      "name": "DeltaV Core Runtime",
      "version": "1",
      "slug": "deltav-core-runtime",
      "packages": [
        "ej-installer-1.12.0"
      ]
    }
  ]
}
```

## Flagged ambiguities

- "upgradePath" was used in early meeting notes to mean pre-upgrade instructions. Resolved: the canonical term is **Pre-Upgrade Action** — it describes what must happen before the upgrade, not the path itself.
- "policyTags" originally included `retryabilityClass` and `idempotencyMode`. Resolved for PoC Phase 1: these are removed from the manifest schema to focus on core delivery features. Only `riskLevel` remains.

## Bounded Context

The Artifact & Workload Catalog owns:
- Artifact storage and retrieval
- Manifest ingest and validation
- Workload definition import (global JSON file, 2–3 workloads for PoC)
- Versioning and immutability of manifests and workload definitions

It does NOT own:
- Policy evaluation (Orchestrator context)
- Execution (Agent Runtime context)
- Enrollment or node management (Orchestrator context)

## Invariants

1. Every artifact has a manifest; manifests are immutable after publish.
2. Risk level is set by operator or elevated by signature verification; it cannot be overridden at runtime.
3. Pre-upgrade actions are declarative; the Orchestrator interprets them during run planning.
4. Workload definitions are imported from global JSON; the catalog does not author them inline.
5. A workload definition cannot be published unless all referenced manifests exist in the catalog.

## Artifact Ingest Pipeline (Implementation Components)

The catalog's artifact ingest uses a two-step flow with deterministic field resolution:

**Step 1 — Submit**: `POST /api/artifacts` receives `multipart/form-data` with `file` (binary) and `manifest` (JSON). Minimal required admin fields are validated. If any required field is missing or unresolvable, ingest is rejected with field-level errors.

**Step 2 — Confirm**: After deterministic default resolution, the persisted resolved manifest includes per-field source provenance (`admin | template | analyzer | default`).

Key services in the resolution chain:
- **PolicyTemplateService** — matches binary metadata to known policy profiles; produces default `riskLevel` values
- **Binary analysis** — probes uploaded binary for type, version, publisher, and install adapter hints
- **Vendor metadata fetch** — queries vendor APIs for additional manifest enrichment (Phase 2 capability)

Priority resolution chain: explicit admin value → policy template match → binary analyzer → platform hard defaults.
