# ASCII Diagram - Install Sequence

```text
System Admin     React UI        Orchestrator API        Agent Service      Package Repo    Installer Adapter    OTel
    |               |                    |                    |                  |                 |               |
    | submit job    |                    |                    |                  |                 |               |
    |-------------->|                    |                    |                  |                 |               |
    |               | POST /jobs         |                    |                  |                 |               |
    |               |------------------->|                    |                  |                 |               |
    |               |                    | validate + queue   |                  |                 |               |
    |               |                    |------------------->| (internal)       |                 |               |
    |               |                    | job available      |                  |                 |               |
    |               |                    |<-------------------| (pull/claim loop)|                 |               |
    |               |                    |                    | claim job         |                 |               |
    |               |                    |<-------------------|                  |                 |               |
    |               |                    |                    | start spans       |                 |               |
    |               |                    |                    |--------------------------------------->|           |
    |               |                    |                    | prechecks         |                 |               |
    |               |                    |                    |------------------>| download        |               |
    |               |                    |                    | verify artifact   |                 |               |
    |               |                    |                    | execute install   |                 |               |
    |               |                    |                    |------------------------------------->|               |
    |               |                    |                    | <--- exit/log ----|                 |               |
    |               |                    |                    | post-verify       |                 |               |
    |               |                    |                    | success/failure    |                 |               |
    |               |                    |<-------------------| report terminal    |                 |               |
    |               |                    |                    | emit final telem   |                 |               |
    |               |<-------------------| stream/fetch state |                  |                 |               |
    | view result   |                    |                    |                  |                 |               |
    |<--------------|                    |                    |                  |                 |               |

Failure path (if needed):
Agent Service -> Agent Service: rollback/compensation -> report failed/rolledback
```
