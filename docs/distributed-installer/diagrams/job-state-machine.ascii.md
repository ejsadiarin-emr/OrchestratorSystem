# ASCII Diagram - Job State Machine

```text
               +---------+
               | Queued  |
               +----+----+
                    |
                    v
               +---------+
               |Assigned |
               +--+---+--+
                  |   |
      precheck ok |   | precheck fail
                  v   v
            +-----------+        +----------------+
            | Installing|        | PrecheckFailed |
            +--+---+----+        +----------------+
               |   |
      success  |   | fail / verify fail
               v   v
         +---------+        +--------------------+
         |Succeeded|        | RollbackInProgress |
         +----+----+        +---------+----------+
              |                       |
              v                       v
            [END]               +------------+
                                | RolledBack |
                                +------+-----+
                                       |
                                       v
                                     [END]

Cancellation path:

Queued   -> Cancelled -> [END]
Assigned -> Cancelled -> [END]
```
