# ASCII Diagram - Job State Machine

> Note: This is a derived artifact. Canonical protocol/state source is `docs/distributed-installer/03-architecture-and-design.md`.

```text
Queued
  |
  v
Assigned <-----------------------------+
  |                                    |
  | lease stale                        | safe reassignment
  v                                    |
AssignedStale -------------------------+
  |
  | stale timeout exhausted
  v
Failed

Assigned -> Prechecking -> PrecheckPassed -> Downloading -> Installing -> Verifying -> Succeeded
                     \-> PrecheckFailed -> Failed

Installing/Verifying -> VerifyFailed -> RollbackInProgress -> RolledBack
Installing -> Failed -> RollbackInProgress -> RolledBack

Cancellation path:

Queued -> Cancelled
Assigned -> Cancelled
Prechecking -> Cancelled
Downloading -> Cancelled
Installing -> Cancelled (safe checkpoint)
Verifying -> Cancelled (safe checkpoint)
```
