# Config Persistence Contract

Date: 2026-04-11  
Status: Draft (prefilled from locked decisions)

## Purpose

Define canonical backup/migration/restore behavior for upgrade safety.

---

## 1) Contract summary

Minimum required behavior:

- pre-mutation snapshot (`configSnapshotId`)
- schema version tracked on snapshot and target package
- deterministic migration path (`vN -> vN+1`)
- rollback restore from snapshot on failure
- audit linkage (`jobId`, `nodeId`, `snapshotId`, migration result)

---

## 2) Snapshot schema

| Field | Type | Required | Description |
|---|---|---|---|
| configSnapshotId | string | yes | Unique snapshot identity |
| jobId | string | yes | Job that created the snapshot |
| nodeId | string | yes | Target node |
| packageId | string | yes | Package identity |
| sourceSchemaVersion | string | yes | Config schema before migration |
| capturedAtUtc | datetime | yes | Snapshot timestamp |
| storageLocation | string | yes | Snapshot location |
| integrityHash | string | yes | Snapshot integrity verification |

---

## 3) Migration contract

### Interface draft

```csharp
public interface IConfigMigration
{
    string FromVersion { get; }
    string ToVersion { get; }
    Task<MigrationResult> ExecuteAsync(MigrationContext context, CancellationToken ct);
}

public sealed record MigrationContext(
    string JobId,
    string NodeId,
    ConfigSnapshot Snapshot,
    string TargetSchemaVersion);
```

### Rules

- Migrations are deterministic and side-effect bounded.
- Migration chain resolution is strict `vN -> vN+1`; missing hop is terminal failure (`migration_path_missing`).
- Each migration is reversible by snapshot restore path.
- Failed migration emits actionable error code and audit event.
- Migration must not proceed without verified snapshot integrity.

---

## 4) Rollback restore contract

| Trigger | Action | Expected result | Failure handling |
|---|---|---|---|
| Migration failure | Restore from `configSnapshotId` | Pre-upgrade config restored | If restore verification fails, mark terminal `Failed` with `restore_failed` |
| Verify failure | Restore from `configSnapshotId` | Consistent runtime state | Emit `ConfigRestoreApplied` or `ConfigRestoreFailed` audit event |
| Operator cancel during migration-safe checkpoint | Restore from `configSnapshotId` | No partial config state remains | If checkpoint is not yet reached, defer cancellation until checkpoint boundary |

Migration-safe checkpoint (PoC): immediately before first irreversible config mutation and after mutable targets are quiesced.

---

## 5) Audit linkage

| Event type | Required fields |
|---|---|
| ConfigSnapshotCreated | `eventId`, `timestampUtc`, `correlationId`, `actorOrSystem`, `jobId`, `nodeId`, `configSnapshotId`, `sourceSchemaVersion` |
| ConfigMigrationApplied | `eventId`, `timestampUtc`, `correlationId`, `actorOrSystem`, `jobId`, `nodeId`, `configSnapshotId`, `fromVersion`, `toVersion`, `result` |
| ConfigRestoreApplied | `eventId`, `timestampUtc`, `correlationId`, `actorOrSystem`, `jobId`, `nodeId`, `configSnapshotId`, `result` |
| ConfigMigrationFailed | `eventId`, `timestampUtc`, `correlationId`, `actorOrSystem`, `jobId`, `nodeId`, `configSnapshotId`, `errorCode`, `reason` |
| ConfigRestoreFailed | `eventId`, `timestampUtc`, `correlationId`, `actorOrSystem`, `jobId`, `nodeId`, `configSnapshotId`, `errorCode`, `reason` |

---

## 6) Open items

- Package-specific configuration path allowlist for phase-1 reference package (pending Day 3 current-state completion).

## 7) Cross-reference anchors

Primary linked requirements and decisions:

- Requirements: `FR-006`, `AC-007` in `docs/distributed-installer/08-requirements-contract.md`
- Decision lock: `D22` in `docs/distributed-installer/sessions/20260411-decision-lock-addendum.md`
- Core contract linkage: `ConfigSnapshot` entity in `docs/distributed-installer/10-core-contracts-pack.md`
- Architecture linkage: upgrade config persistence notes in `docs/distributed-installer/03-architecture-and-design.md`
