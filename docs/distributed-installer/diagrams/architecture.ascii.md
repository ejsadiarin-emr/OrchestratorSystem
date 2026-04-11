# ASCII Diagram - Architecture

```text
                            +----------------------+
                            |     System Admin     |
                            |   (React Dashboard)  |
                            +----------+-----------+
                                       |
                          REST API     |  SignalR (live updates)
                                       v
                       +-------------------------------------------+
                       |              Orchestrator Node            |
                       | +-----------+  +-----------+  +---------+ |
                       | | REST API  |  |SignalR Hub|  | Hangfire| |
                       | +-----+-----+  +-----+-----+  +----+----+ |
                       |       |              |             |      |
                       |       v              v             v      |
                       | +----------------+ +--------------------+ |
                       | | SQL Server     | | Audit/Event Store  | |
                       | | (job state)    | | (append-oriented)  | |
                       | +----------------+ +--------------------+ |
                       +---------+-----------------+---------------+
                                 |                 |
     runtime: Assign/Ack/Lease   |                 |   runtime: Assign/Ack/Lease
                                 v                 v
                    +------------------------+   +------------------------+
                    |    Remote Machine A    |   |    Remote Machine B    |
                    | +--------------------+ |   | +--------------------+ |
                    | | Agent Win Service  | |   | | Agent Win Service  | |
                    | +---------+----------+ |   | +---------+----------+ |
                    |   SignalR + mTLS       |   |   SignalR + mTLS       |
                    |           |            |   |           |            |
                    |           v            |   |           v            |
                    | +--------------------+ |   | +--------------------+ |
                    | | Channel<T> + BG Svc| |   | | Channel<T> + BG Svc| |
                    | +---------+----------+ |   | +---------+----------+ |
                    |           |            |   |           |            |
                    |           v            |   |           v            |
                    | +--------------------+ |   | +--------------------+ |
                    | | Child Process (Job)| |   | | Child Process (Job)| |
                    | +---------+----------+ |   | +---------+----------+ |
                    |           |            |   |           |            |
                    |           v            |   |           v            |
                    | +--------------------+ |   | +--------------------+ |
                    | | Full Job Pipeline  | |   | | Full Job Pipeline  | |
                    | +--+-----+-----+-----+ |   | +--+-----+-----+-----+ |
                    +----|-----|-----|-------+   +----|-----|-----|-------+
                         |     |     |                |     |     |
                         v     v     v                v     v     v
                       +----+ +----+ +-------------------------+
                       |MSI | |EXE | | Legacy Components (VB6) |
                       +----+ +----+ +-------------------------+

              +----------------------------------+
              |  Artifact Source (UNC/HTTPS Repo)|
              +---------------+------------------+
                              ^
                              |
                  agents download packages

      provisioning only (not runtime package orchestration):
      WinRM (PoC) / GPO / SCCM -> install/register/update Agent service

              +----------------------------------+
              |      OTel Collector Pipeline     |
              +----+---------------+-------------+
                   |               |
                   v               v
                +------+        +--------+
                | Logs |        | Metrics|
                +------+        +--------+
                       \         /
                        v       v
                       +---------+
                       | Traces  |
                       +---------+

    Trust boundary annotations:
    - TB-01: System Admin UI/API caller -> Orchestrator API
    - TB-02: Agent service -> SignalR Hub runtime channel
    - TB-03: Orchestrator/Agent -> Artifact Source channel
    - TB-04: Orchestrator API -> Audit/Event Store write path

    Telemetry emitted by: REST API, SignalR Hub, Agent A, Agent B
```
