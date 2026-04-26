# DetectionConfig Persistence — Build Fix Decisions Log

## Context
While investigating what happens when a node has a pre-installed package (e.g., git installed manually) that the orchestrator doesn't know about, we discovered two bugs that rendered the differential update/rollback and post-install verification features non-functional in production.

This doc records the decisions made during the fix.

---

## Summary

| # | Topic | Decision |
|---|---|---|
| 1 | `CurrentPackages` flow | Agent runtime must assign `payload.CurrentPackages` to `PipelineContext` |
| 2 | DetectionConfig storage | Persist `ResolvedManifest.Detection` as JSON on `PackageEntity` |
| 3 | DetectionConfig source at runtime | Deserialize stored JSON with fallback to name-based defaults |

---

## Decision 1: `CurrentPackages` Must Flow from Payload to PipelineContext

**Question:** The orchestrator sends `CurrentPackages` in `AssignRunPayload`, but the agent never forwards it to `PipelineContext`. The diff always computes against an empty list — all packages are classified as "Added" on every run. How do we fix this?

**Decision:** Add `CurrentPackages = payload.CurrentPackages` to the `PipelineContext` constructor in `AgentRuntimeService.HandleAssignRunAsync()`.

**Fix location:** `apps/agent/backend/Services/AgentRuntimeService.cs:205`

**Rationale:** The `PipelineContext` initializes `CurrentPackages` as `new()` (empty list). Without explicit assignment, the differential update/rollback feature is dead code — the `DiffEngine` never has a "before" snapshot to compare against.

**Why this is not caught by tests:** The test `PipelineExecutor_UnchangedPackages_AreSkipped` sets `CurrentPackages` directly on `PipelineContext`, bypassing the runtime service bug. The test passes in isolation but the real system never populates `CurrentPackages`.

---

## Decision 2: Persist DetectionConfig on PackageEntity

**Question:** `BuildPackageAssignments` hardcodes `DetectionConfig.Path = pkg?.Name ?? ""` — the bare package name like "git", not an actual filesystem path. `PostInstallVerify` runs `File.Exists("git")` which returns false. The `ResolvedManifest.Detection` object has correct data (populated during ingest) but is saved only to the filesystem `resolved-manifest.json`, never to the database. How do we make the correct detection config available at runtime?

**Decision:** Add a `DetectionConfigJson` column to `PackageEntity` and populate it during every ingest path.

**Changes:**
1. Add `public string DetectionConfigJson { get; set; } = string.Empty` to `PackageEntity` entity
2. Add EF column mapping: `nvarchar(2048)` via `InstallerDbContext`
3. Serialize `ResolvedManifest.Detection` to JSON at all 4 ingest sites in `ArtifactsController`
4. Serialize `manifest.Detection` to JSON in `WorkloadImportService`
5. Generate EF migration `AddPackageDetectionConfig`

**Rationale:** The database is the authoritative source for package metadata. Reading the filesystem `resolved-manifest.json` at runtime would add I/O, a new coupling, and potential drift if the file is deleted or the agent runs on a different machine. Storing the detection config in the DB keeps all package metadata colocated in a single query.

---

## Decision 3: Runtime DetectionConfig — Deserialize Stored JSON with Fallback

**Question:** Once `DetectionConfigJson` exists on `PackageEntity`, how should `BuildPackageAssignments` use it?

**Decision:** Try to deserialize `pkg.DetectionConfigJson` first. If it's null/empty or deserialization fails, fall back to the original behavior: `Type = "file"`, `Path = pkg.Name`, `ExpectedVersion = pkg.Version`.

**Fix:** Added `BuildDetectionConfig(PackageEntity?)` method at `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`.

**Rationale:** Backward compatibility with existing records that have no stored detection config. The fallback preserves the (broken) historical behavior for old records while new uploads get correct detection data. Over time, old packages can be re-uploaded to populate the detection config.

---

## Why Not Detect at Runtime on the Agent

**Question:** Could the agent probe the filesystem (or registry) at runtime to determine if a package is installed, rather than relying on the orchestrator's stored config?

**Decision:** No. The orchestrator is the source of truth for deployment targets. Agent-side self-detection would:
- Require a registry of known install locations per package (brittle)
- Not know which version the orchestrator expects (only what's installed)
- Add complexity to the stateless agent design
- Duplicate detection logic across orchestrator and agent

The `PostInstallVerify` step on the agent still validates the detection config (checks file existence + version), but what to check is determined by the orchestrator's stored config.

---

## Open Decisions

- *None.*
