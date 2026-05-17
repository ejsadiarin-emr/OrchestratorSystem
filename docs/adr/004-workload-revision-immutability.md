# ADR-004: Workload Revision Immutability

**Status:** Accepted  
**Date:** 2026-05-17  
**Context:** DeploymentPoC workload revision lifecycle

## Problem

Workload revisions represent versioned snapshots of packages and init steps that are deployed to nodes. Once a revision is used in a deployment run, any mutation to its package list or structure would silently invalidate audit trails, break `RevisionSnapshotJson` on historic runs, and create ambiguity about what was actually deployed.

## Decision

Workload revisions are **immutable after creation**. This is enforced at two levels:

### 1. Database-level enforcement via EF Core interceptor

`InstallerDbContext.EnforceWorkloadRevisionImmutability()` is called from both `SaveChanges()` and `SaveChangesAsync()` overrides. It inspects the `ChangeTracker` for `WorkloadRevisionEntity` and `WorkloadPackageEntity` entries:

- **Deletion is blocked** — throws `InvalidOperationException` for any `EntityState.Deleted` entry
- **Modification is blocked** — throws `InvalidOperationException` if any property other than the allowed set is marked as modified

### 2. Allowed mutations (narrow exemption)

**WorkloadRevisionEntity** may modify only:
- `IsPublished` — publish/unpublish toggle
- `PreWorkloadStepsJson` / `PostWorkloadStepsJson` — pre/post workload step definitions
- `PreUninstallStepsJson` / `PostUninstallStepsJson` — pre/post uninstall step definitions
- `DefaultShell` — default shell for steps

**WorkloadPackageEntity** may modify only:
- `PreInitStepsJson` / `PostInitStepsJson` — per-package init step definitions

Rationale: step definitions and publish state are configuration that may need adjustment before a revision is published or between deployments, but the package identity (what gets installed) must never change.

### 3. No soft delete

There is no soft-delete mechanism. Revisions cannot be removed — they persist forever as audit trail. If a revision is no longer wanted, `IsPublished` is set to `false`.

## Consequences

### Positive
- **Audit integrity** — `RevisionSnapshotJson` on historic runs always points to a valid, unchanged revision
- **Race-condition safety** — a run in progress never has its definition mutated mid-execution
- **Simpler reasoning** — a revision's package list is fixed; no need to check modification timestamps or version columns
- **Explicit evolution** — to change a workload, create a new revision; the old one remains intact

### Negative
- **No typo correction** — even trivial errors in steps require creating a new revision
- **Storage growth** — every revision is retained permanently; no cleanup path exists
- **Step-only edits bypass the spirit** — modifying `PreWorkloadStepsJson` is allowed but changes what actually runs, which partially contradicts immutability intent (the package list is safe, but behavior can shift)

## Trade-offs Accepted

- Step definitions (`PreWorkloadStepsJson`, etc.) are mutable to allow operators to fix broken init scripts without version bumping the entire revision. This is a pragmatic escape valve that trades strict immutability for operational flexibility.
- Deletion is permanently blocked rather than soft-deleted because the foreign key from `WorkloadRunEntity.RevisionId` with `DeleteBehavior.Restrict` ensures historic runs can always resolve their revision.

## Related

- `InstallerDbContext.cs` lines 272–320: interceptor implementation
- `WorkloadRevisionEntity.cs`: entity definition
- `WorkloadPackageEntity.cs`: entity definition (also immutable)
- ADR-005: Run State Machine (references revision snapshots on run creation)