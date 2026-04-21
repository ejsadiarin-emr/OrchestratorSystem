# ADR-014: Package Auto-Detection and Metadata Enrichment

**Date:** 2026-04-15  
**Status:** Accepted  
**Deciders:** System Administrator (formerly "Operator"), Architecture, Product

---

## Context

The current package ingestion flow requires System Administrators to manually construct full manifest metadata (package name, version, install adapter, detection path, policy tags, etc.) when uploading packages to the orchestrator artifact store. This creates friction for well-known packages like Node.js, .NET runtimes, and SQL Server where official metadata is publicly available.

This ADR defines an auto-detection and metadata enrichment system that:
- Reduces manual entry for known packages
- Uses vendor APIs as the authoritative source for official package metadata
- Falls back to binary analysis and policy templates for unknown packages
- Allows System Administrators to review and fill gaps before final submission

---

## Decision

Implement a **three-tier auto-detection system**:

1. **Vendor API Lookup** — Query official vendor APIs to auto-fill metadata for known packages
2. **Binary Analysis** — Extract PE version info, archive metadata for packages without vendor APIs
3. **Policy Templates** — Pre-defined risk/retry templates that System Administrators select and override

---

## Package Ingestion Flow

```
System Admin uploads binary
         │
         ▼
┌─────────────────────────┐
│  PackageRegistry lookup  │◄── Local SQLite (populated by vendor APIs)
└───────────┬─────────────┘
            │ known?
     ┌──────┴──────┐
     │             │
    yes           no
     │             │
     ▼             ▼
┌─────────────┐  ┌─────────────────┐
│ Auto-fill   │  │ Binary Analysis │
│ metadata    │  │ (PE/Archive)    │
└──────┬──────┘  └────────┬────────┘
       │                   │
       └─────────┬─────────┘
                 ▼
┌─────────────────────────────────────┐
│  Policy Template Selection          │◄── System Admin selects
│  (riskLevel, retryability, etc.)    │
└──────────────────┬──────────────────┘
                   │
                   ▼
┌─────────────────────────────────────┐
│  Admin Reviews & Fills Gaps         │
│  (custom policies, optional overrides) │
└──────────────────┬──────────────────┘
                   │
                   ▼
┌─────────────────────────────────────┐
│  Submit Finalized Manifest           │
└─────────────────────────────────────┘
```

---

## Core Components

### 1. PackageRegistryService

**Purpose:** SQLite-backed local registry of known packages with vendor-sourced metadata.

**Schema:**
```sql
CREATE TABLE PackageRegistry (
    PackageId TEXT PRIMARY KEY,          -- e.g., "nodejs", "dotnet-runtime"
    DisplayName TEXT,
    LatestVersion TEXT,
    VendorSource TEXT,                    -- e.g., "nodejs.org", "dotnetcli.azureedge.net"
    LastSyncedAtUtc TEXT,
    MetadataJson TEXT                    -- cached vendor response
);

CREATE TABLE PackageVersionCache (
    PackageId TEXT,
    Version TEXT,
    MetadataJson TEXT,
    FetchedAtUtc TEXT,
    PRIMARY KEY (PackageId, Version)
);
```

**Behavior:**
- Lookup returns cached metadata if fresh (configurable TTL, default 1 hour)
- Background refresh populates cache from vendor APIs
- On cache miss, fetch-on-demand and cache result

### 2. VendorMetadataFetcher

**Purpose:** Query official vendor APIs to retrieve package metadata.

**Supported Vendor APIs (Phase 1):**

| Package Type | API Endpoint | Retrieved Fields |
|--------------|-------------|-----------------|
| Node.js | `https://nodejs.org/dist/index.json` | version, lts, date, files |
| .NET Runtime | `https://dotnetcli.azureedge.net/dotnet/release-metadata.json` | version, channel, supportEndDate |
| Python | `https://pypi.org/pypi/{package}/json` | version, release_date, info |
| Generic (MSI) | Binary MSI summary tables | ProductName, ProductVersion, Manufacturer |

**Error Handling:**
- Vendor API timeout (5s default): fallback to binary analysis
- Vendor API 4xx/5xx: fallback to binary analysis
- No internet: fallback to binary analysis
- Binary analysis fails: prompt manual entry

### 3. BinaryAnalyzer

**Purpose:** Extract metadata from the binary artifact itself when vendor APIs unavailable.

**Extraction Targets:**

| Artifact Type | Extracted Fields |
|---------------|------------------|
| MSI | ProductName, ProductVersion, Manufacturer, ProductCode |
| PE/EXE | FileVersion, ProductVersion, CompanyName, OriginalFilename |
| ZIP/TAR.GZ | Archive comment, contained file names (for heuristics) |

**Limitations:**
- Only extracts what the binary exposes (version info, publisher)
- Cannot determine idempotency behavior or risk level
-这些 fields are hints, not authoritative

### 4. PolicyTemplateService

**Purpose:** Provide sensible default policy tags per package type, with System Administrator selection.

**Built-in Templates:**

| Template | retryabilityClass | idempotencyMode | riskLevel | Use Case |
|----------|-------------------|-----------------|-----------|----------|
| Standard Install | transient_only | version_check | medium | General software |
| Development Tool | transient_only | always | low | SDKs, build tools |
| Runtime | bounded | version_check | medium | Node.js, .NET |
| Database | bounded | detect | high | SQL Server, PostgreSQL |
| Security Patch | transient_only | version_check | low | Hotfixes |
| Critical Service | none | detect | high | Domain controllers, etc. |
| Custom | operator-defined | operator-defined | operator-defined | Manual override |

**Behavior:**
- After auto-fill, System Administrator selects template
- Template pre-fills policy tags
- System Administrator can override any field
- Custom overrides are logged for audit

### 5. IngestionOrchestrator

**Purpose:** Coordinate the full auto-detection and submission flow.

**Endpoint:** `POST /api/artifacts/ingest`

**Request:**
```json
{
  "binary": "<multipart file upload>",
  "packageIdHint": "nodejs",          // optional, helps lookup
  "selectedTemplate": "runtime",       // optional, defaults to "standard-install"
  "policyOverrides": {                  // optional, merged with template
    "riskLevel": "high"
  }
}
```

**Response:**
```json
{
  "jobId": "uuid",
  "status": "pending_review",
  "autoDetected": {
    "packageId": "nodejs",
    "displayName": "Node.js",
    "version": "22.0.0",
    "installAdapter": { ... },
    "detection": { ... }
  },
  "policyTags": {
    "retryabilityClass": "transient_only",
    "idempotencyMode": "version_check",
    "riskLevel": "medium"
  }
}
```

**Follow-up Endpoint:** `POST /api/artifacts/ingest/{jobId}/confirm`

---

## API Design

### Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/artifacts/ingest` | Upload binary, trigger auto-detection |
| GET | `/api/artifacts/ingest/{jobId}` | Get auto-detected metadata + policy |
| POST | `/api/artifacts/ingest/{jobId}/confirm` | Submit finalized manifest |
| POST | `/api/artifacts/ingest/{jobId}/reject` | Cancel and delete draft |
| GET | `/api/artifacts/registry/packages` | List known packages in registry |
| POST | `/api/artifacts/registry/refresh` | Trigger vendor API sync |

### UI Flow

1. **Upload Screen:** Drag-drop or file picker for binary
2. **Auto-Detection Screen:** Shows detected metadata, allows template selection
3. **Review Screen:** Edit policy tags, fill missing fields
4. **Confirm Screen:** Summary + submit

---

## Data Flow

```
1. Admin uploads binary
2. Orchestrator computes SHA256 digest
3. PackageRegistryService.lookup(packageIdHint or computed hash)
   ├── Cache hit + fresh → return cached metadata
   ├── Cache miss → VendorMetadataFetcher.fetch(packageId)
       ├── Success → cache result, return metadata
       └── Failure → BinaryAnalyzer.analyze(binary)
           └── Extract available fields
4. PolicyTemplateService.getTemplate(selectedTemplate)
5. Return draft manifest with auto-filled fields
6. Admin reviews, selects template, overrides fields
7. Admin confirms → finalize manifest, create artifact record
```

---

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Vendor API unreachable | Fallback to binary analysis |
| Binary analysis fails | Return empty fields, require manual entry |
| Unknown package type | Prompt manual entry for all fields |
| Duplicate upload | Detect by digest, prompt "package already exists" |
| Invalid binary | Reject with validation error |

---

## Deferred to Phase 2

- Extended vendor API support (AWS, Azure CLIs, Docker images)
- Package dependency graph resolution
- Catalog sync from internal artifact repositories
- ML-based policy suggestion based on historical data

---

## Consequences

**Positive:**
- Reduced friction for System Administrators ingesting known packages
- More consistent policy tagging through templates
- Audit trail for policy template selection and overrides

**Negative:**
- Additional complexity in ingestion pipeline
- Vendor API dependency for auto-detection (mitigated by fallback chain)
- Registry cache staleness risk (mitigated by TTL + manual refresh)

**Risks:**
- Vendor API changes break auto-detection (mitigated by graceful fallback)
- Incorrect auto-detection leads to wrong policy (mitigated by admin review gate)

---

## Implementation Notes

- **Phase 1 scope:** Node.js and .NET Runtime only for vendor API
- **Binary analysis:** MSI and PE/EXE version info extraction
- **Registry persistence:** SQLite (same DB as orchestrator)
- **Cache TTL:** Configurable, default 1 hour
- **Vendor API timeout:** 5 seconds with circuit breaker
