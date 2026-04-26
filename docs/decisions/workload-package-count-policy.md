# Workload Package Count Policy Change

## Context

The PoC originally constrained each workload revision to **2–3 packages** under the assumption that a small multi-package set was the minimal meaningful unit for demonstrating differential update and rollback. This limit was encoded in the PRD, implementation tracker, and validation rules.

## Decision

**Workload revisions must contain 1 or more packages. Zero-package workloads are disallowed.**

The previous 2–3 package upper/lower bound is relaxed to a simple **minimum of 1**.

## Rationale

1. **Demo clarity for rollback/update**: Single-package workloads make differential behavior (added, changed, removed, unchanged) easy to observe and explain. A workload with one package that changes version clearly shows "changed" without the noise of multi-package interactions.
2. **No loss of coverage**: The agent's diff algorithm and two-phase execution (uninstall first, then install) are fully exercised with a single package. Multi-package workloads remain valid and useful for stress-testing ordering and inter-package conflicts.
3. **Zero-package workloads are meaningless**: A workload with no packages has no deployable content and would break the install pipeline (nothing to execute). This is now explicitly invalid.
4. **PoC flexibility**: Operators can create small focused workloads (e.g., "Install Git only") or larger composite workloads (e.g., "Developer Workstation" with 5+ packages) without arbitrary limits.

## Consequences

### Validation
- Revision creation and global JSON import must reject workloads with an empty `packages` array.
- No upper limit is enforced in PoC Phase 1.

### Test Data
- Existing test workload files (`workloads-older.json`, `workloads-newer.json`) remain valid (they contain 3 and 2 packages respectively).
- New test cases should include a single-package workload to exercise the boundary.

### Documentation
- PRD assumption updated from "2–3 packages" to "1 or more packages (never 0)".
- Implementation tracker checklist updated.
- Context maps and catalog docs updated where they reference package count.

## Open Questions

- Should the UI warn when a workload has >10 packages? (Deferred to Hardening Phase 2.)
- Should bulk import enforce a practical upper bound to prevent accidental abuse? (Deferred to Hardening Phase 2.)
