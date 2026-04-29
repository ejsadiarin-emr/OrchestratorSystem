# Decision: Init Steps Shell Configuration

**Date:** 2026-04-30
**Status:** Resolved

## Q18: Per-Step Shell Override

**Decision:** Option B — `defaultShell` only. All init steps use the same shell specified at the workload level.

- Steps remain simple `[]string` — no schema change needed
- If a future need arises for per-step shell override, the `shell:` prefix syntax (Option A) can be added as a non-breaking extension
- YAGNI for PoC phase