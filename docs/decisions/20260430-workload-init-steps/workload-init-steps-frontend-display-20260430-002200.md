# Decision: Init Steps Frontend Display

**Date:** 2026-04-30
**Status:** Resolved

## Q17: Frontend Display Format for Init Steps

**Decision:** Option A — Collapsible section per package.

- Each package row in the workload detail page expands to show its init steps (pre-init commands listed first, then post-init commands)
- Collapsed by default — packages without init steps show no expansion indicator
- Post-workload steps shown in a separate section at the bottom of the revision view
- Maps directly to the data model structure