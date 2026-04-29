# Decision: Init Steps Run Mode Behavior

**Date:** 2026-04-30
**Status:** Resolved

## Q19: Init Steps During Rollback/Update Runs

**Decision:** Option B — Mode-gated. Run init steps on `install` and `update` modes only. Skip on `rollback`.

### Additional Context: Rollback is Non-Priority

Rollback as a whole is **not a priority for now**. The intended fallback is "uninstall then reinstall" rather than true state-snapshot rollback (which would require saving state/snapshots/backups). Rollback mode can remain as-is without init step support until it's prioritized.

### Summary

| Mode | Init Steps | Notes |
|---|---|---|
| `install` | Run all (pre, post, post-workload) | Full init |
| `update` | Run all (pre, post, post-workload) | Re-run init steps on updated packages |
| `rollback` | Skip | Non-priority. Fallback: uninstall + reinstall |