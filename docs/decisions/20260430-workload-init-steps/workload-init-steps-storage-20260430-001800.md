# Decision: Init Steps Storage Schema

**Date:** 2026-04-30
**Status:** Resolved

## Q13: Orchestrator Storage for Init Steps

**Decision:** Add JSON columns to existing entities. No new entity/table.

### Schema changes

**WorkloadPackageEntity** (existing table):
- Add `PreInitStepsJson` (`string`, nullable) — JSON array of command strings
- Add `PostInitStepsJson` (`string`, nullable) — JSON array of command strings

**WorkloadRevisionEntity** (existing table):
- Add `PostWorkloadStepsJson` (`string`, nullable) — JSON array of command strings
- Add `DefaultShell` (`string`, default `"powershell"`)

### Rationale

- Consistent with existing patterns: `DetectionConfigJson`, `ExpectedExitCodesJson` on `PackageEntity`
- Init steps are always read/written as a batch with their parent entity — no need for normalized table
- Avoids new entity/table, migration complexity, and join queries
- JSON columns allow flexible step arrays without schema changes when step format evolves