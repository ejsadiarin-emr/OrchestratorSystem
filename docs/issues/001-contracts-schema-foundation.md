# 001 - Contracts & Database Schema Foundation

## Type

AFK

## Parent PRD

[docs/prd-init-steps.md](../prd-init-steps.md)

## Blocked by

None — can start immediately.

## What to build

Extend the shared contracts and orchestrator database schema to support init steps. This is the data foundation — no behavior changes yet.

**Shared Contracts** (`shared/contracts/`):

- Rename `AssignRunPayload.PreUpgradeActions` → `PreWorkloadSteps`
- Add `PostWorkloadSteps` (`List<string>`) to `AssignRunPayload`
- Add `DefaultShell` (`string`, default `"powershell"`) to `AssignRunPayload`
- Add `PreInitSteps` (`List<string>`) to `PackageAssignment`
- Add `PostInitSteps` (`List<string>`) to `PackageAssignment`

**Database Entities** (`apps/orchestrator/backend/Data/Entities`):

- Add `PreInitStepsJson` (`string?`, default `"[]"`) to `WorkloadPackageEntity`
- Add `PostInitStepsJson` (`string?`, default `"[]"`) to `WorkloadPackageEntity`
- Add `PreWorkloadStepsJson` (`string?`, default `"[]"`) to `WorkloadRevisionEntity`
- Add `PostWorkloadStepsJson` (`string?`, default `"[]"`) to `WorkloadRevisionEntity`
- Add `DefaultShell` (`string`, default `"powershell"`) to `WorkloadRevisionEntity`

**Database Migration**: Add-only ALTER TABLE. No columns renamed or dropped. Existing rows get default values. No data migration needed. Uses existing `DetectionConfigJson` storage pattern.

## Acceptance criteria

- [ ] `AssignRunPayload` has `PreWorkloadSteps` (renamed), `PostWorkloadSteps`, and `DefaultShell`
- [ ] `PackageAssignment` has `PreInitSteps` and `PostInitSteps`
- [ ] All existing references to `PreUpgradeActions` updated to `PreWorkloadSteps`
- [ ] `WorkloadPackageEntity` has `PreInitStepsJson` and `PostInitStepsJson` columns with default `"[]"`
- [ ] `WorkloadRevisionEntity` has `PreWorkloadStepsJson`, `PostWorkloadStepsJson`, and `DefaultShell` columns
- [ ] EF migration is add-only; existing rows retain values
- [ ] Tests verify data round-trips: contract object → entity JSON serialization → DB persistence → deserialization → contract object
- [ ] Backward compatibility: existing workloads with no init steps (null/empty arrays) continue to serialize/deserialize correctly

## Referenced decisions

- [Schema Placement](../decisions/20260430-workload-init-steps/workload-init-steps-schema-20260430-000543.md)
- [API Contract Format](../decisions/20260430-workload-init-steps/workload-init-steps-api-contract-20260430-002000.md)
- [Storage Schema](../decisions/20260430-workload-init-steps/workload-init-steps-storage-20260430-001800.md)
- [PreWorkloadSteps Rename](../decisions/20260430-workload-init-steps/workload-init-steps-pre-workload-20260430-003200.md)
- [Remaining Decisions (storage section)](../decisions/20260430-workload-init-steps/workload-init-steps-remaining-20260430-003300.md)
