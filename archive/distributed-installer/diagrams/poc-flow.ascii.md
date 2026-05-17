# ASCII Diagram - PoC Flow (WinRM)

```text
     Step 1                   Step 2                   Step 3                   Step 4/5                  Step 6
       |                      |                      |                      |                       |
       v                      v                      v                      v                       v
+------------------+   +------------------+   +------------------+   +------------------+   +------------------+
|    Operator      |   |   Orchestrator   |   | Target Machine  |   | Windows Service  |   |   Orchestrator   |
| (runs bootstrap)|-->| (source script)  |-->| (WinRM target)  |-->|   (Agent.exe)  |-->| (SignalR Hub)   |
+------------------+   +------------------+   +------------------+   +------------------+   +------------------+
                                                                                          |
                                                                                          v
                                                                              +---------------------------+
                                                                              | Ready for job assignments |
                                                                              +---------------------------+
```