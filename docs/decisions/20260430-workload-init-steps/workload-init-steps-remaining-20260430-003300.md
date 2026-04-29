# Decision: Storage, Limits, Diff, Idempotency, Validation

**Date:** 2026-04-30
**Status:** Resolved

## Q25: DB Migration Strategy

**Decision: A — Add-only, no rename needed.**

The rename (`PreUpgradeActions` → `preWorkloadSteps`) only affects the runtime contract (`AssignRunPayload` in `shared/contracts/`), not the database. Storage for init steps lives on existing entities as new JSON columns.

### What this looks like

**AssignRunPayload** (shared contract — the rename target):
```csharp
// BEFORE:
public List<string> PreUpgradeActions { get; set; } = new();

// AFTER:
public List<string> PreWorkloadSteps { get; set; } = new();
public List<string> PostWorkloadSteps { get; set; } = new();
public string DefaultShell { get; set; } = "powershell";
```

**WorkloadRevisionEntity** (new JSON columns):
```csharp
public string PreWorkloadStepsJson { get; set; } = "[]";
public string PostWorkloadStepsJson { get; set; } = "[]";
public string DefaultShell { get; set; } = "powershell";
```

**WorkloadPackageEntity** (new JSON columns):
```csharp
public string PreInitStepsJson { get; set; } = "[]";
public string PostInitStepsJson { get; set; } = "[]";
```

Migration: `ALTER TABLE WorkloadRevisions ADD COLUMN ...` and `ALTER TABLE WorkloadPackages ADD COLUMN ...` with default `"[]"`. No columns renamed or dropped. No data migration needed since existing rows get the empty-array default.

**PackageAssignment** (shared contract — per-package steps in payload):
```csharp
public List<string> PreInitSteps { get; set; } = new();
public List<string> PostInitSteps { get; set; } = new();
```

**WorkloadRunDispatcher** must deserialize these JSON columns and populate `PackageAssignment.PreInitSteps`/`.PostInitSteps` and `AssignRunPayload.PreWorkloadSteps`/`.PostWorkloadSteps` when building the `AssignRunPayload`.

---

## Q26: Max Limits

**Decision: A — No limits for PoC.** Add caps later if abuse is observed. Self-limiting factors: first-failure-stop on preInit, author is a human crafting workload JSON, not an automated system.

---

## Q27: Revision Diff for Init Steps

**Decision: A — No diff UI for now.** Display-only view (Q17) is sufficient. Import JSON is the authoring tool. Revision comparison already shows package adds/removes.

---

## Q28: Idempotency on Retry

**Decision: A — Author's responsibility.** System provides no retry-idempotency logic. Commands in init steps should be safe to re-run.

**Future consideration:** `DEPLOY_RETRY_COUNT` env var (trivial to add later) would let authors write conditional init steps like `if ($env:DEPLOY_RETRY_COUNT -gt 0) { skip }`. Not required for MVP.

---

## Q29: Import Validation

**Decision: B — Reject empty strings and enforce 4096-char max at import time.**

Validated in `WorkloadImportService` when parsing the workload JSON:
1. Reject empty strings in init step arrays (prevents accidental empty commands)
2. Reject any command string exceeding 4096 characters

Rationale: Cheap to validate at import time, prevents confusing failures at deployment time. An empty string would produce a no-op `Process.Start()` or PowerShell `-Command ""` — better to catch it early.