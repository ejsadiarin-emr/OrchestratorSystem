> **Note:** The `rollback` mode has been removed and replaced with `uninstall`. This decision is superseded by [workload-run-polish-uninstall-precheck.md](../workload-run-polish-uninstall-precheck.md).

# Decision: Init Steps Run Mode Behavior

**Date:** 2026-04-30
**Status:** Resolved

## Q19: Init Steps During Uninstall/Update Runs

**Decision:** Option B — Mode-gated. Run init steps on `install` and `update` modes only. Skip on `uninstall`.

### Additional Context: Uninstall is Non-Priority for Init Steps

The `uninstall` mode does not require init step support. Init steps are only needed for `install` and `update` operations where packages are being deployed.

### Summary

| Mode | Init Steps | Notes |
|---|---|---|
| `install` | Run all (pre, post, post-workload) | Full init |
| `update` | Run all (pre, post, post-workload) | Re-run init steps on updated packages |
| `uninstall` | Skip | Init steps not needed for uninstall operations |