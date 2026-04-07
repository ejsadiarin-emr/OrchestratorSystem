# ASCII Diagram - Install Sequence

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
    |               |                |                 |              | SignalR job    |               |                |                |             |        |
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
    |               |                |                 |              |<---------------| SignalR update|               |                |             |        |
    |               |<---------------|<----------------|              | push update    |               |                |                |             |        |
    | view result   |                |                 |              |                |               |                |                |             |        |
    |<--------------|                |                 |              |                |               |                |                |             |        |

Failure path (if needed):
Child Process -> Child Process: rollback/compensation -> report failed/rolledback -> SignalR -> Hub -> UI
```
