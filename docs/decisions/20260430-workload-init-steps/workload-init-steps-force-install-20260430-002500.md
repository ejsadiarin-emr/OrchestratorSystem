# Decision: Init Steps with ForceInstall

**Date:** 2026-04-30
**Status:** Resolved

## Q20: Init Steps Interaction with ForceInstall Flag

**Decision:** Option A — Always run init steps on ForceInstall.

- preInitSteps, postInitSteps, and postWorkloadSteps all execute regardless of `ForceInstall`
- preInit checks (verifying prerequisites) remain valuable even when forcing
- postInit configuration (connecting to DB, etc.) should always happen after install
- Burden of idempotency stays on the command author — same principle as the shell commands themselves
- No special ForceInstall bypass for any init step type