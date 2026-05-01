# 002 - Uninstall Steps: Contracts, Schema & Backend Import

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

- Blocked by #001 (Foundation: Rollback→Uninstall Mode Swap) — entity and import model changes reference `uninstall` context

## What to build

Add `PreUninstallSteps` and `PostUninstallSteps` across the full data pipeline: entity columns, DB migration, shared contracts, import model, and the dispatcher that sends payloads to agents. No pipeline execution changes — that comes in #008.

**Database Entity (`apps/orchestrator/backend/Data/Entities/`):**

- `WorkloadRevisionEntity.cs`: Add `PreUninstallStepsJson` (string, default `"[]"`) and `PostUninstallStepsJson` (string, default `"[]"`), same shape as existing `PreWorkloadStepsJson`/`PostWorkloadStepsJson`

**DB Migration:**

```sql
ALTER TABLE "WorkloadRevisions" ADD COLUMN "PreUninstallStepsJson" TEXT NOT NULL DEFAULT '[]';
ALTER TABLE "WorkloadRevisions" ADD COLUMN "PostUninstallStepsJson" TEXT NOT NULL DEFAULT '[]';
```

**Shared Contracts (`shared/contracts/`):**

- `Runtime/RunPayloads/AssignRunPayload.cs`: Add `PreUninstallSteps` (List<string>, default empty) and `PostUninstallSteps` (List<string>, default empty)

**Backend Import (`apps/orchestrator/backend/`):**

- `Controllers/WorkloadsController.cs`: Add `PreUninstallSteps` and `PostUninstallSteps` to `WorkloadImportModel` with `[JsonPropertyName("preUninstallSteps")]` and `[JsonPropertyName("postUninstallSteps")]` attributes, typed as `List<JsonElement>?`
- `Controllers/WorkloadsController.cs`: In the bulk import and revision create/update logic, parse the new `JsonElement` lists into `List<string>`, validate (reject empty strings, reject commands > 4096 chars — same validation as existing init steps), and persist to `PreUninstallStepsJson`/`PostUninstallStepsJson`

**Backend Dispatch (`apps/orchestrator/backend/`):**

- `Services/WorkloadRunDispatcher.cs`: When constructing `AssignRunPayload` for a run, deserialize `PreUninstallStepsJson` and `PostUninstallStepsJson` from the revision entity and populate the payload fields

## Acceptance criteria

- [ ] `WorkloadRevisionEntity` has `PreUninstallStepsJson` and `PostUninstallStepsJson` columns with default `"[]"`
- [ ] EF migration adds both columns with `NOT NULL DEFAULT '[]'`
- [ ] `AssignRunPayload` has `PreUninstallSteps` and `PostUninstallSteps` (`List<string>`, default empty)
- [ ] `WorkloadImportModel` has `PreUninstallSteps` and `PostUninstallSteps` with correct `JsonPropertyName` attributes
- [ ] Import validates: empty strings rejected, commands > 4096 chars rejected (same rules as `PreWorkloadSteps`)
- [ ] Import persists round-trip: JSON → `JsonElement` list → string list → JSON column → deserialized back correctly
- [ ] `WorkloadRunDispatcher` includes uninstall steps in `AssignRunPayload` when creating uninstall runs
- [ ] Existing workloads without uninstall steps (null/missing in JSON) import and dispatch correctly (graceful defaults)
- [ ] `dotnet build` succeeds for orchestrator and contracts projects

## Referenced decisions

- [D11: PreUninstallSteps / PostUninstallSteps — Add Now](../../decisions/workload-run-polish-uninstall-precheck.md#d11-preuninstallsteps--postuninstallsteps--add-now)
- [D19: Uninstall Warning — Package List from Revision](../../decisions/workload-run-polish-uninstall-precheck.md#d19-uninstall-warning--package-list-from-revision)
