# Audit Report: Proposed "Run Creator Wizard" vs Current Codebase Reality

## I. Note on Image

I cannot view Image 1 (this model doesn't support image input). Where I reference UI observations, these come from the code analysis.

---

## II. Executive Summary

Your proposed wizard is **sound in concept** and addresses real gaps, but several of your assumptions about current behavior need correction. The biggest discrepancies are:

1. **Pre-checks are NOT a gate** — they're informational only. The backend allows run creation regardless of drift.
2. **"Update" mode is implicitly handled** — no distinct UI or backend path; it becomes `effectiveMode="update"` at run creation time.
3. **DB reconciliation already exists** but is **destructive** — it deletes `NodeWorkloadState` records when agent says "not present," which contradicts your "DB follows agent reality" model for partial states.
4. **Uninstall mode has no workload-to-package execution path** — the backend only checks `CurrentRevisionId` matching but doesn't derive uninstall commands from packages.

---

## III. Pre-Checks: Proposal vs Reality

### Your Proposal
> Pre-checks: Detects already existing packages on the Agent based on a selected workload (compares workload packages with versions AND currently existing packages in Agent as "reality"). DB reconciliation = pre-checks update Agent node records in the DB based on the reality of the packages. Agent reality is the source of truth. Only run comparisons and DB reconciliation IF pre-checks run with a selected workload. After pre-checks: output a delta summary.

### Current Reality

| Aspect | Your Proposal | Current Code | Gap |
|---|---|---|---|
| **Trigger** | Only when workload is selected | Auto-runs on workload+revision selection + manual button | Your proposal is more restrictive; current auto-runs could be wasteful without a workload |
| **DB Reconciliation** | "DB follows agent reality" for ALL states | Partial: deletes `NodeWorkloadState` when agent says "not present" (line 581, `NodesController`); updates `PackageStatesJson` for drift. **Never creates** a state record during pre-checks. | A partial state (1/2 packages installed) cannot be reflected — there's no `NodeWorkloadState` to update if none exists |
| **Scope** | Workload-scoped only | Already implemented: `WorkloadId?` param — if provided, only checks that workload's packages | Aligns with your proposal |
| **Delta Summary** | Explicit comparison output | `DryRunPreviewResponse` exists at `POST /api/workload-runs/dry-run` with per-node actions (FreshInstall/Update/Reinstall/NoAction), and per-node pre-check badges in the UI | Partially exists; could be formalized as the wizard step output |
| **Pre-check as Gate** | "Pre-checks determine eligibility" | Pre-checks are **informational only** — no gate. Backend allows run creation even with drift (returns 409 only if `Reinstall=false` and drift is detected during `install` mode). The UI doesn't block submission on pre-check failure. | **Major gap** — your proposal requires pre-checks to be a prerequisite gate, which requires new logic |

### Findings on Pre-Checks

1. **Pre-checks without a workload are meaningless** — your proposal is correct. Currently, if no `workloadId` is passed, pre-checks fall back to checking packages from revisions assigned to the node via `NodeWorkloadStates`. This is close to your intent but could show stale data.

2. **DB reconciliation for partial states is missing.** Currently, `ReconcileProbeResults()` handles these cases:
   - DB has state, agent says all present → update `PackageStatesJson` (good)
   - DB has state, agent says none present → **delete the state record entirely** (potentially dangerous)
   - DB has no state → no reconciliation, just report results
   
   Your proposal to "DB reconcile to record ONLY the existing package" for partial installs (e.g., 1/2 packages) isn't supported. The current code would find no `NodeWorkloadState` record and do nothing.

3. **Downgrade detection doesn't exist in pre-checks.** The `VersionComparer.Matches` does prefix matching (e.g., `3.14` matches `3.14.4`) but has no concept of "less than." The `PreCheckStatus` enum is `{AlreadySatisfied, WrongVersion, NotPresent}` — it cannot distinguish "wrong version, and it's older" from "wrong version, and it's newer."

---

## IV. Install Mode: Proposal vs Reality

### Your Proposal
> Install mode: Select workload → Run pre-checks → Cases:
> - No existing packages → fresh install
> - Has existing (1/2 installed) → DB reconcile, skip existing, install missing, verify, then DB reconcile
> - Has existing (2/2 installed) → skip everything, pipeline succeeds
> 
> After successful install, assign workload to Agent node in DB.

### Current Reality

| Step | Your Proposal | Current Backend | Current Agent | Gap |
|---|---|---|---|---|
| **Pre-check gate** | Required before install | Not required. `WorkloadRunsController.Create` (line 164) checks drift only if `!request.Reinstall`, and returns 409 if drift found — but doesn't **require** pre-checks to have been run. | N/A | Pre-check results aren't persisted as a prerequisite flag on the run |
| **Fresh install** | If no packages present | `effectiveMode="install"` assigned when node has no state + no prior runs (line 211) | `DiffEngine` produces all packages as "added" | Aligns |
| **Partial install (1/2)** | DB reconcile, skip existing, install missing | **No reconciliation during install flow.** If `NodeWorkloadState` doesn't exist, it's created as empty `{}` during claim (line 200, `NodeWorkloadStateService`). The agent's `DiffEngine` with pre-check probes will detect existing packages as `AlreadySatisfied` and skip them. | Agent Phase 0 probes detect existing packages. `PreCheckSkip` (line 84 in `PipelineExecutor`) skips packages where probe says `AlreadySatisfied`. | **Partial alignment.** DB doesn't reconcile — agent handles skipping. Your proposal wants DB reconciliation during pre-checks, which would need new logic |
| **Full install (2/2)** | Skip everything, pipeline succeeds | No special handling. Would create run with `effectiveMode="install"`, agent probes would find all `AlreadySatisfied`, skip all packages, and the timeline would have no `InstallOrUpgrade` entries → `CurrentRevisionId` would NOT be set (since `setCurrentRevision` requires at least one success step). | Pipeline checks `AllStepsSucceeded && StepHistory.Count > 0` for finalization. If all skipped, `StepHistory.Count == 0`, pipeline reports success but no DB state update. | **Major gap.** A "fully installed, nothing to do" scenario would leave `CurrentRevisionId` as null because no steps executed. Your proposal says "assign workload to Agent node" — this would need explicit handling |
| **Post-install DB update** | Update Agent node records to assign/mapped workload | `HandleCompleteAsync` sets `CurrentRevisionId` only if timeline has `InstallOrUpgrade` success entries | Same — only on active install steps | See above — fails when all packages already present |

### Key Finding: The "Everything Already Installed" Edge Case

If a node already has all packages for a workload and the user runs install:
1. `WorkloadRunsController.Create` → if drift detected and `Reinstall=false`, returns 409
2. If `Reinstall=true` → creates run, agent re-installs everything
3. But if pre-checks say "all AlreadySatisfied" AND the user wants to just register the mapping → the current code has **no path** for this.

Your proposal's "skip everything, pipeline succeeds, then assign workload" requires:
- A new `NodeWorkloadState` upsert that sets `CurrentRevisionId` even when no steps executed
- Or a separate "register" action outside of run creation

---

## V. Update Mode: Proposal vs Reality

### Your Proposal
> Update mode: Select Agent node → Select workload (newer version) → Run pre-checks (auto) → 
> - If agent has assigned workload (DB check, older version) → update accordingly
> - If downgrade → reject (caught in pre-checks after selecting node)
> - If agent has packages but no DB record → DB reconcile during pre-checks, then update

### Current Reality

| Aspect | Your Proposal | Current Code | Gap |
|---|---|---|---|
| **UI trigger** | Explicit "Update" mode | **No "Update" button in UI.** Only Install/Uninstall toggle. Update happens implicitly when installing to a node with a different revision. | Your wizard needs a distinct update step |
| **Mode selection** | User explicitly picks "update" | Backend creates `effectiveMode="update"` when `install` mode is requested but `node.CurrentRevisionId != request.RevisionId` (line 230). User never sees this. | Works implicitly but conflicts with your wizard proposal |
| **Downgrade rejection** | Reject in pre-checks | **No downgrade protection exists.** `VersionComparer.Matches` does prefix matching but no comparison (>, <, =). There's no version ordering check anywhere — neither in pre-checks nor in run creation. | **Major gap.** Requires version comparison logic and enforcement |
| **DB reconciliation during update** | Reconcile before update selection | Not implemented. If node has `NodeWorkloadState` with mismatched `PackageStatesJson`, it just gets overwritten during the run. | Your proposal needs DB reconciliation as a separate step |

### Downgrade Detection — Technical Feasibility

`PackageEntity.Version` and `WorkloadRevisionEntity.Version` are plain strings. There is **no version ordering logic** in the codebase. `VersionComparer` only does prefix matching for detection, not comparison. To implement downgrade rejection, you'd need:

1. A `TryCompareVersions(string a, string b)` method that returns ordering
2. Integration into pre-checks or run creation validation
3. UI feedback showing "this is a downgrade from v2 → v1"

---

## VI. Uninstall Mode: Proposal vs Reality

### Your Proposal
> Uninstall mode: Select agent node → Select workload (that the agent has) → Execute uninstall commands from package manifest's `UninstallCommand` + `UninstallArgs`.

### Current Reality

| Aspect | Your Proposal | Current Code | Gap |
|---|---|---|---|
| **Node selection** | Select agent node with assigned workload | UI already filters to `uninstallNodes` (online + has workload installed) | Aligns |
| **Workload selection** | Select the specific workload | UI shows installed revisions for the workload | Aligns |
| **Backend validation** | Verify node has workload assigned | `WorkloadRunsController` (line 97): For `uninstall` mode, all nodes must have `CurrentRevisionId == request.RevisionId` in `NodeWorkloadStates` | **Stricter than your proposal.** Must match exact revision, not just workload |
| **Uninstall command source** | Package manifest's `UninstallCommand` + `UninstallArgs` | `PackageEntity` has `UninstallCommand` and `UninstallArgs` fields. `WorkloadPackageEntity` also has `PreInitSteps`/`PostInitSteps` that could be repurposed. Agent `PipelineExecutor` Phase 1 handles uninstall. | Aligns — but `AgentUninstallPackage.cs` has a **fallback registry search** (`ResolveRegistryUninstaller`) when no `UninstallCommand` is configured |
| **Package ordering** | Implied sequential | Agent uninstalls in **reverse index order** (line ~325 in `PipelineExecutor`) | Good — reverse order is correct for dependency chains |
| **Post-uninstall verify** | Not specified | Agent runs `PostUninstallVerify` with 3 retries at 5s delay to confirm package was removed. If still detected, reports failure. | Agent already has this |

### Key Gaps in Uninstall Mode

1. **Revision matching is too strict.** The current backend requires `CurrentRevisionId == request.RevisionId`, meaning you can only uninstall the exact revision. Your proposal says "select workload (that the agent has)" — implies workload-level, not revision-level. If a workload has v1 and v2, and a node has v1 assigned, you should be able to uninstall that workload regardless of whether v2 is now the published revision.

2. **"Or is there a better approach?"** — Yes. The agent already has a complete uninstall pipeline that:
   - Checks if package is present (skip if `NotPresent`)
   - Resolves uninstaller from manifest or registry
   - Downloads artifact if MSI (for MSI uninstall)
   - Executes with elevation fallback
   - Verifies removal
   
   Your proposal aligns well with the existing pipeline. The main improvement would be relaxing the revision-matching constraint.

---

## VII. Wizard Architecture Assessment

### Your Proposal's Step-by-Step Flow

| Step | Install | Update | Uninstall |
|---|---|---|---|
| 1 | Select workload | Select agent node | Select agent node |
| 2 | Run pre-checks (with workload) | Run pre-checks (auto, after node) | Select workload (from node's assigned workloads) |
| 3 | — | Select workload (newer version) | Run pre-checks (with workload) |
| 4 | Review & confirm | Review & confirm | Review & confirm |

### Assessment

**Strengths:**
- Guided flow prevents user mistakes (wrong mode, wrong node/workload combination)
- Pre-checks as a mandatory gate solves the "drift detection after submission" problem
- Step validation catches issues early (downgrade, missing workload, offline nodes)

**Concerns:**

1. **Mode-first vs Node-first ordering.** Your proposal has install starting with workload selection, but update/uninstall starting with node selection. This creates **three different wizard flows** which increases complexity. Consider: **always start with mode selection → then branch.** This is cleaner for a step-by-step wizard.

2. **"Update" as a separate mode is conceptually muddy.** Currently, "update" is just "install to a node that already has a different revision." Making it a distinct mode means:
   - You need to decide: is update a subset of install (same flow, different effective behavior) or truly separate?
   - The backend's `effectiveMode` resolution already handles this — it converts "install to node with different revision" to "update"
   - Recommendation: Keep Install/Uninstall as the two modes. Update is Install with a different outcome. Show the delta in the confirmation step instead.

3. **Pre-checks as a blocking gate needs careful UX.** If pre-checks are required before proceeding:
   - What happens for offline nodes? (Currently pre-checks call agent HTTP endpoint)
   - What's the timeout? (Agent could be unreachable)
   - Can users override/force past pre-check failures? (Consider a `ForceInstall` equivalent)

4. **The unused `Stepper` component** (`components/ui/stepper.tsx`) provides exactly the UI primitive you need for the wizard. It has `Step { id, label }`, `activeStep` state, and visual completed/active/pending states. You can build the wizard on top of this.

---

## VIII. DB Reconciliation — Deep Dive

### Your Proposal
> DB reconciliation = pre-checks update Agent node records in the DB based on the reality of the packages in Agent (Agent reality is the source of truth the the DB must follow).

### Current Reconciliation Logic (`NodesController.ReconcileProbeResults`, line 502):

| Scenario | Current Behavior | Your Proposal | Gap |
|---|---|---|---|
| DB has state, agent confirms all present | Update `PackageStatesJson` to match | Same | Aligned |
| DB has state, agent says none present | **Delete `NodeWorkloadState` record entirely** | DB follows agent reality → remove workload mapping | Aligned, but see concern below |
| DB has no state, agent has some packages | **No action** — `NodeWorkloadState` not created | DB reconcile: create record with partial `PackageStatesJson` | **Gap** — needs upsert logic |
| DB has state with revision A, agent has packages from revision B | Update `PackageStatesJson` to reflect actual versions. Does NOT change `CurrentRevisionId` | DB reconcile: update `CurrentRevisionId` to match reality? Or keep null/pending? | **Ambiguous** — your proposal doesn't specify what happens to `CurrentRevisionId` when packages don't match any known revision |

### Critical Concern: Deleting NodeWorkloadState on "Not Present"

If `ReconcileProbeResults` deletes the `NodeWorkloadState` when the agent says "not present," this means:
- Pre-checks on a workload that was never installed → **no record exists** → no action → correct
- Pre-checks on a workload where packages were manually removed from the agent → **record deleted** → workload is no longer "assigned" → correct
- Pre-checks on a workload where the agent is temporarily unreachable (HTTP timeout) → **pre-check fails** → no reconciliation → safe (probe failure is caught)
- Pre-checks on a workload where 1 of 2 packages was removed → `PackageStatesJson` updated to show 1 package `NotPresent` → **record NOT deleted** (because some packages match) → this is correct

The current deletion logic is actually reasonable for the "fully gone" case, but it's missing the "partially present, no DB record" case.

---

## IX. Unanswered Design Questions

These questions emerge from the audit but aren't answered by your proposal:

1. **What happens to `CurrentRevisionId` during DB reconciliation?** If an agent has partial packages (say from revision v2 but one is missing), should `CurrentRevisionId` be null, stay at v2, or be set to some "partial" state?

2. **Should pre-checks be mandatory or advisory?** Your proposal says "pre-checks determine eligibility," but should there be a force-override option (like `ForceInstall` already exists)?

3. **Is "Update" a separate wizard flow or a conditional path within Install?** The backend treats it as `effectiveMode`, but your proposal suggests a separate flow.

4. **What about agent-offline scenarios?** Pre-checks require agent HTTP access. Your wizard needs a clear path for offline nodes (skip? warn? block?).

5. **How should the wizard handle multi-node runs?** Currently, a single `WorkloadRun` targets one node (one `WorkloadRunEntity` per node per request). The wizard's "select nodes" step creates multiple runs. Should pre-checks be per-node with per-node gating?

6. **What about idempotency?** The current `IdempotencyKey` + `IdempotencyRequestHash` system prevents duplicate runs. Should the wizard surface this (e.g., "a run for this workload+revision+nodes is already in progress")?

---

## X. Summary of Gaps and Recommendations

### Critical Gaps (Must Address)

| # | Gap | Current State | Recommended Fix |
|---|---|---|---|
| G1 | **No downgrade protection** | `VersionComparer` only does prefix matching, no ordering | Add version comparison. Block downgrade in pre-checks and/or run creation |
| G2 | **"Already fully installed" produces no DB state update** | `CurrentRevisionId` only set on successful install/uninstall steps | Add logic: if all packages `AlreadySatisfied` and run mode=install, upsert `NodeWorkloadState` with the target revision |
| G3 | **Pre-checks are not a gate** | Informational only; no prerequisite flag on run creation | Add a `preCheckPassedAtUtc` timestamp to `NodeWorkloadState` or `WorkloadRunEntity`, or add a wizard-enforced validation step |
| G4 | **Uninstall requires exact revision match** | `CurrentRevisionId == request.RevisionId` validation | Relax to `CurrentRevisionId != null` (workload is assigned, doesn't need exact revision) |
| G5 | **No DB reconciliation for partial states** | If no `NodeWorkloadState` exists, pre-check results are just reported | Create `NodeWorkloadState` with partial `PackageStatesJson` even when `CurrentRevisionId` is null |

### Moderate Gaps (Should Address)

| # | Gap | Current State | Recommendation |
|---|---|---|---|
| M1 | **No explicit "Update" mode in UI** | Update is implicit via `effectiveMode` | Decide: either add Update as a wizard mode or make Install smart enough to show delta. Recommend the latter. |
| M2 | **`NodeWorkloadState` deletion on "not present"** | Entire record deleted | Keep behavior but document it. Consider a "soft delete" or status flag instead. |
| M3 | **`PackageStatesJson` structure** | `Dictionary<string, PackageState>` keyed by package ID string | Consider keying by both package ID and version for clarity in drift scenarios |
| M4 | **Pre-check auto-run on revision change** | Runs on every workload/revision change, even in uninstall mode | Make pre-checks a manual wizard step, not auto-run |

### Alignment (Already Works)

| Area | Note |
|---|---|
| Pre-checks scope | `WorkloadId` filtering already exists |
| Agent-side detection | Three modes (file, registry, version_manifest) with prefix matching |
| Agent-side install pipeline | Phase 0-3 flow is solid with pre-check probes, diff engine, and verify steps |
| Package uninstall commands | `UninstallCommand` + `UninstallArgs` fields exist and are used by agent |
| Agent `DiffEngine` | Handles all three modes correctly with probe-based overrides |
| Workload-to-package mapping | `WorkloadRevisionEntity` → `WorkloadPackageEntity` → `PackageEntity` chain is correct |
| Idempotency | `IdempotencyKey` + hash already prevents duplicate runs |
| Stepper UI component | Already built, just unused |

Your wizard idea is well-aligned with the codebase's architecture. The main work is in the orchestrator-side validation logic (gates, downgrade protection, reconciliation upserts) and the frontend step-by-step wizard flow — not in the agent pipeline, which is already solid.

---