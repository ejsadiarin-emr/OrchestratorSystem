# Package auto-detection and metadata enrichment

Package ingestion uses a three-tier auto-detection system: (1) Vendor API Lookup for known packages, (2) Binary Analysis for packages without vendor APIs, (3) Policy Template Selection where operators choose a risk-level template and override before final submission. Workload definitions are imported from global JSON files (2–3 workloads per PoC file).

**Status**: accepted

**Considered Options**: (1) Full manual entry only, (2) Auto-detection with operator review gate, (3) Fully automated catalog sync.

## Core Components

### PackageRegistryService — SQLite-backed local registry of known packages with vendor-sourced metadata

```sql
CREATE TABLE PackageRegistry (
    PackageId TEXT PRIMARY KEY,
    DisplayName TEXT,
    LatestVersion TEXT,
    VendorSource TEXT,
    LastSyncedAtUtc TEXT,
    MetadataJson TEXT
);

CREATE TABLE PackageVersionCache (
    PackageId TEXT,
    Version TEXT,
    MetadataJson TEXT,
    FetchedAtUtc TEXT,
    PRIMARY KEY (PackageId, Version)
);
```

Lookup returns cached metadata if fresh (configurable TTL, default 1 hour). Background refresh populates cache from vendor APIs. On cache miss, fetch-on-demand and cache result.

### VendorMetadataFetcher — Query official vendor APIs for package metadata

Phase 1 vendors:

| Package Type | API Endpoint | Retrieved Fields |
|---|---|---|
| Node.js | `https://nodejs.org/dist/index.json` | version, lts, date, files |
| .NET Runtime | `https://dotnetcli.azureedge.net/dotnet/release-metadata.json` | version, channel, supportEndDate |
| Python | `https://pypi.org/pypi/{package}/json` | version, release_date, info |
| Generic (MSI) | Binary MSI summary tables | ProductName, ProductVersion, Manufacturer |

Error handling: vendor API timeout (5s default) or 4xx/5xx → fallback to binary analysis; no internet → fallback to binary analysis; binary analysis fails → prompt manual entry.

### BinaryAnalyzer — Extract metadata from the binary artifact when vendor APIs unavailable

| Artifact Type | Extracted Fields |
|---|
| MSI | ProductName, ProductVersion, Manufacturer, ProductCode |
| PE/EXE | FileVersion, ProductVersion, CompanyName, OriginalFilename |
| ZIP/TAR.GZ | Archive comment, contained file names (for heuristics) |

Limitations: only extracts what the binary exposes; cannot determine risk level.

### PolicyTemplateService — Sensible default risk levels per package type

| Template | riskLevel | Use Case |
|---|---|---|
| Standard Install | medium | General software |
| Development Tool | low | SDKs, build tools |
| Runtime | medium | Node.js, .NET |
| Database | high | SQL Server, PostgreSQL |
| Security Patch | low | Hotfixes |
| Critical Service | high | Domain controllers |
| Custom | operator-defined | Manual override |

After auto-fill, the operator selects a template. Templates pre-fill `riskLevel` only. The operator can override and final submission is logged for audit.

**Consequences**: Reduced friction for operators ingesting known packages. More consistent risk-level tagging through templates. Audit trail for template selection and overrides. Only `riskLevel` remains in manifest `policyTags` — `retryabilityClass` and `idempotencyMode` are removed for PoC Phase 1 (Decision 5).

## Amendment

Ported from ADR-014. Changes from original:
- Removed `retryabilityClass` and `idempotencyMode` from PolicyTemplateService table and all examples (Decision 5: only riskLevel in policyTags)
- "System Administrator" → "operator" (consistent domain terminology)
- "jobId" → "workloadRunId" (Decision: workload run terminology)
- Removed policy overrides for retryabilityClass and idempotencyMode from request/response examples (Decision 5)
- Added reference to global workload JSON file for workload definitions (Decision 6)
- Policy template table simplified to show only `riskLevel` column (Decision 5)
- Removed embedded UI flow references — the Agent is headless; operator interaction is via Orchestrator UI only (Decision 1)