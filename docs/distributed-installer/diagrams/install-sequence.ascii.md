# ASCII Diagram - Install Sequence

> Note: This is a derived artifact. Canonical protocol/state source is `docs/distributed-installer/03-architecture-and-design.md`.

```text
System Admin     React UI     Orchestrator API    Hangfire     SignalR Hub    Agent Service    Channel<T>    Child Process    Package Repo    Installer    OTel
    |               |                |                 |              |                |               |                |                |             |        |
    | submit job    |                |                 |              |                |               |                |                |             |        |
    |-------------->|                |                 |              |                |               |                |                |             |        |
    |               | POST /jobs     |                 |              |                |               |                |                |             |        |
    |               |--------------->|                 |              |                |               |                |                |             |        |
    |               | validate       |                 |              |                |               |                |                |             |        |
    |               |--------------->|                 |              |                |               |                |                |             |        |
    |               |                | enqueue         |              |                |               |                |                |             |        |
    |               |                |---------------->|              |                |               |                |                |             |        |
    |               |                |                 | resolve+disp |                |               |                |                |             |        |
    |               |                |                 |------------->|                |               |                |                |             |        |
    |               |                |                 |              | AssignJob      |               |                |                |             |        |
    |               |                |                 |              |--------------->|               |                |                |             |        |
    |               |                |                 |              |                | enqueue       |                |                |             |        |
    |               |                |                 |              |                |-------------->|                |                |             |        |
    |               |                |                 |              |                |               | spawn child    |                |             |        |
    |               |                |                 |              |                |               |--------------->|                |             |        |
    |               |                |                 |              |                |               |                | start spans    |             |        |
    |               |                |                 |              |                |               |                |------------------------------->|        |
    |               |                |                 |              |                |               |                | prechecks      |             |        |
    |               |                |                 |              |                |               |                | download       |             |        |
    |               |                |                 |              |                |               |                |--------------->|             |        |
    |               |                |                 |              |                |               |                | verify artifact|             |        |
    |               |                |                 |              |                |               |                | execute install|             |        |
    |               |                |                 |              |                |               |                |------------------------------->|-------->|
    |               |                |                 |              |                |               |                | <--- exit/log -|             |        |
    |               |                |                 |              |                |               |                | post-verify    |             |        |
    |               |                |                 |              |                |               |<---------------| status update  |             |        |
    |               |                |                 |              |                |<--------------|                |                |             |        |
    |               |                |                 |              |<---------------| StepStatus    |               |                |             |        |
    |               |<---------------|<----------------|              | push update    |               |                |                |             |        |
    | view result   |                |                 |              |                |               |                |                |             |        |
    |<--------------|                |                 |              |                |               |                |                |             |        |

Lease/idempotency notes:
- Agent acknowledges assignment with `AckClaim(assignmentId, leaseId, sequenceAck)`
- Agent sends `LeaseHeartbeat(leaseId)` every 15s (PoC default)
- Agent emits `StepStatus(jobId, stepId, sequence, status)`
- Orchestrator applies idempotent upsert on `(jobId, stepId, sequence)` and rejects stale/out-of-order updates
- Agent emits terminal `Complete/Fail` then `LeaseClose(leaseId)`

Failure path (if needed):
Child Process -> Child Process: rollback/compensation -> report failed/rolledback -> SignalR -> Hub -> UI
```
