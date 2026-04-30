# 002 - Orchestrator: Import & Dispatch

## Type

AFK

## Parent PRD

[docs/prd-init-steps.md](../prd-init-steps.md)

## Blocked by

- Blocked by #001 (Contracts & Database Schema Foundation)

## What to build

Implement the orchestrator-side data flow for init steps: import JSON parsing → validation → storage → API response → dispatch payload.

**Request models** (`apps/orchestrator/backend/`):

- Add `PreInitSteps`, `PostInitSteps` to `WorkloadPackageInput`
- Add `PostWorkloadSteps`, `DefaultShell` to `CreateWorkloadRevisionRequest`

**Custom JSON converter for package entries:**

- Handle both `JsonTokenType.String` (shorthand `"nodejs-24.13.0"`) and `JsonTokenType.StartObject` (object with `preInitSteps`/`postInitSteps`)
- String packages deserialize as `{ Name: value, PreInitSteps: [], PostInitSteps: [] }`
- Object packages deserialize with typed init step arrays
- Backward compatible — existing string-shorthand workloads import unchanged

**Validation:**

- Reject empty strings (`""`) in any init step array
- Reject any command string exceeding 4096 characters
- Store validated commands as JSON arrays in entity columns

**WorkloadImportService** changes:

- Parse `preWorkloadSteps`, `postWorkloadSteps`, `defaultShell` from workload-level JSON
- Apply validation during import
- Persist to `WorkloadPackageEntity` (per-package) and `WorkloadRevisionEntity` (workload-level)

**WorkloadRunDispatcher** changes:

- Deserialize `WorkloadPackageEntity.PreInitStepsJson` / `PostInitStepsJson` into `PackageAssignment.PreInitSteps` / `PostInitSteps`
- Deserialize `WorkloadRevisionEntity.PreWorkloadStepsJson` / `PostWorkloadStepsJson` into `AssignRunPayload.PreWorkloadSteps` / `PostWorkloadSteps`
- Populate `AssignRunPayload.DefaultShell` from `WorkloadRevisionEntity.DefaultShell`

**Three-layer data flow:** Import JSON (hybrid string/object) → REST API (always object) → Runtime payload (always object).

## Acceptance criteria

- [ ] Custom JSON converter handles string shorthand: `"nodejs-24.13.0"` → `{ Name: "nodejs-24.13.0", PreInitSteps: [], PostInitSteps: [] }`
- [ ] Custom JSON converter handles object form: `{ "name": "...", "preInitSteps": [...], "postInitSteps": [...] }`
- [ ] Empty strings in init step arrays rejected at import with clear error
- [ ] Command strings over 4096 characters rejected at import with clear error
- [ ] `preWorkloadSteps`, `postWorkloadSteps`, `defaultShell` parsed from workload-level JSON
- [ ] API create revision endpoint accepts and persists all new fields
- [ ] API response includes init step data for frontend consumption
- [ ] `WorkloadRunDispatcher` correctly populates `AssignRunPayload` and `PackageAssignment` with init step data
- [ ] Existing workloads with no init steps import and dispatch unchanged
- [ ] Unit tests for import service (hybrid JSON parsing, validation rejection, correct deserialization)
- [ ] Unit tests for dispatch payload construction (correct field population from entity JSON columns)
- [ ] Integration test: import workload JSON with init steps → query API → verify response matches input

## Referenced decisions

- [Import JSON Format](../decisions/20260430-workload-init-steps/workload-init-steps-import-format-20260430-001900.md)
- [API Contract Format](../decisions/20260430-workload-init-steps/workload-init-steps-api-contract-20260430-002000.md)
- [Storage Schema](../decisions/20260430-workload-init-steps/workload-init-steps-storage-20260430-001800.md)
- [Remaining Decisions (validation section)](../decisions/20260430-workload-init-steps/workload-init-steps-remaining-20260430-003300.md)
- [Shell Configuration](../decisions/20260430-workload-init-steps/workload-init-steps-shell-config-20260430-002300.md)
