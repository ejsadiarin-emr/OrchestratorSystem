# Decision: Init Steps Timeout Configuration

**Date:** 2026-04-30
**Status:** Resolved

## Q12: Timeout for Init Step Commands

**Decision:** Per-step-type defaults with no config surface for now.

| Step Type | Default Timeout |
|---|---|
| preInitSteps | 60 seconds |
| postInitSteps | 120 seconds |
| postWorkloadSteps | 180 seconds |

### Rationale

- preInit: Fast checks (version probes, existence checks) — 60s is generous
- postInit: May configure services, register with DBs — 120s allows slower operations
- postWorkload: May verify entire stack, ping external endpoints — 180s for completeness
- No config surface needed now — can add a workload-level override later if needed
- If a command exceeds its timeout, `Process.Kill()` and treat as failure (respective failure semantics from Q8/Q9 apply)