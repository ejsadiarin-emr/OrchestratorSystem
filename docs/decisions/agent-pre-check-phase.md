# Agent Pre-Check Phase — Design Decision

## Context

### The Edge Case
When a node has a package pre-installed outside of workload management (e.g., git installed manually before the agent was enrolled), the orchestrator is completely blind to it. The orchestrator's `CurrentPackages` only tracks packages previously installed via workload runs.

During a workload run in `install` or `update` mode, the `DiffEngine` compares the target revision against `CurrentPackages`. If git is in the target revision but not in `CurrentPackages`, it is classified as **"Added"** — meaning the agent will attempt to install it, even though it's already present on the system.

This can cause:
- **Installer failures**: Some installers fail when the package is already installed.
- **Wasted time/IO**: Downloading and running an installer unnecessarily.
- **Incorrect state**: The post-install verification may fail or report misleading results.

### Why the Orchestrator Can't Solve This Alone
The orchestrator derives `CurrentPackages` from `NodeWorkloadStateEntity.CurrentRevisionId` — a database record that only exists after at least one successful workload run. Pre-installed packages have no record in the orchestrator's database.

Orchestrator-side alternatives considered and rejected:
- **Manual baseline curation**: Operationally fragile, doesn't scale, operators would need to know every pre-installed package on every node.
- **Registry sync on enrollment**: Would require a full system scan during enrollment, complex and slow.
- **"Assume present" flags**: Would require operators to manually flag packages, defeating automation.

The only place that can observe the ground truth is the agent itself at runtime.

### Existing Detection Infrastructure
The `PostInstallVerify` step already uses `DetectionConfig` to check if a package is present and what version it is. It supports:
- `file` detection: `File.Exists(path)` + optional version extraction
- `registry` detection: Registry key lookup + version extraction

However, `PostInstallVerify` only runs **after** installation. We need the same logic **before** installation to avoid redundant work.

### Related Decision
See [detection-config-persistence.md](./detection-config-persistence.md) for how `DetectionConfig` is now stored on `PackageEntity` and available at runtime.

---

## Decision

Add a **Phase 0: Pre-Check** to the agent's pipeline execution, before the existing uninstall (Phase 1) and install (Phase 2) phases.

### Pipeline Phase Sequence (Updated)

| Phase | Name | Purpose |
|---|---|---|
| 0 | **Pre-Check** | Check if target packages are already satisfied on the node |
| 1 | Uninstall | Remove packages that are no longer needed (reverse order) |
| 2 | Install | Install added/changed packages (normal order) |

### Pre-Check Behavior

For each package in the target revision:

1. **Read the `DetectionConfig`** from the package assignment (same config used by `PostInstallVerify`).
2. **Probe the node** using the detection config (file or registry check).
3. **Compare version** if `ExpectedVersion` is specified.
4. **Classify the package**:
   - **Already Satisfied**: Package is present and version matches (or no version check). → Skip installation entirely.
   - **Wrong Version**: Package is present but version differs. → Treat as "Changed" (will be uninstalled then reinstalled).
   - **Not Present**: Package is not detected. → Treat as "Added" (normal install flow).

### DiffEngine Integration

The pre-check phase feeds into the existing `DiffEngine` logic:

- Before: `DiffEngine.Compute(currentPackages, targetPackages)`
- After: `DiffEngine.Compute(currentPackages, targetPackages, preCheckResults)`

Pre-check results override the diff classification:
- A package classified as "Added" by diff, but "Already Satisfied" by pre-check → becomes **"Unchanged"** (skipped).
- A package classified as "Unchanged" by diff, but "Wrong Version" by pre-check → becomes **"Changed"** (reinstalled).

This preserves the orchestrator's intent while correcting for ground-truth observations.

### Reporting

Pre-check results are reported as `StepStatus` messages to the orchestrator:
- `PreCheckSkipped` — package already satisfied
- `PreCheckMismatch` — wrong version detected (proceeding with reinstall)
- `PreCheckNotFound` — package not detected (proceeding with install)

These are purely informational; the orchestrator does not gate the pipeline on pre-check results. The agent makes the skip/install decision locally.

### Why Not a Separate Orchestrator-Gated Phase?

A separate pre-pipeline message exchange (agent sends pre-check results, orchestrator responds with go/no-go) was considered and rejected:

| Concern | Pipeline Phase | Separate Exchange |
|---|---|---|
| Complexity | Low (single code path) | High (new SignalR contracts, orchestrator logic) |
| Latency | One round-trip | Two round-trips |
| Race conditions | None | Agent could disconnect after pre-check |
| Offline capability | Works if agent goes offline mid-run | Requires orchestrator online for pre-check |
| State machine | No change | New orchestrator state: "PreChecking" |
| Value add | Agent can self-correct | Orchestrator would just echo "proceed" |

The orchestrator doesn't have additional context to make a better decision than the agent. The agent has the detection config and the filesystem. Let it decide.

---

## Why This Is Safe

1. **Idempotent by design**: If a package is truly not present, the pre-check says "Not Present" and we install it. If it's present, we skip. There's no harmful "false positive" — a skipped package is always one we would have re-installed anyway.

2. **Version-aware**: A version mismatch is treated as "Changed", triggering uninstall+reinstall. We don't leave stale versions in place.

3. **No state mutation**: Pre-check is read-only. It probes but never modifies. If the pre-check crashes, the pipeline can fall back to the original behavior (treat everything as Added).

4. **Fallback on missing config**: If `DetectionConfig` is missing or invalid, pre-check returns "Not Present" and the install proceeds normally. This is the same as the old behavior.

---

## Resolved Decisions

### Q1: Should there be agent pre-checks at all?
**Answer:** Yes. The orchestrator cannot know what was manually installed on a node before enrollment. Agent-side detection is the only way to observe ground truth.

### Q2: Should pre-checks be a new pipeline phase or a separate orchestrator-gated exchange?
**Answer:** New pipeline phase (Phase 0: Pre-Check) within `PipelineExecutor`. Simpler, no new SignalR contracts, no race conditions, agent decides locally.

### Q3: Should pre-checks guard uninstalls too?
**Answer:** Yes. If a package marked for uninstall is already gone (pre-check says "Not Present"), skip the uninstall step and report `UninstallSkippedAlreadyGone`. This prevents uninstall failures for manually-removed packages.

### Q4: Should pre-check results be ephemeral or persisted?
**Answer:** Ephemeral (re-probed every run). Cache invalidation is hard (manual changes between runs), and probe I/O is trivial for a PoC. Persistence can be added later if profiling shows it's needed.

### Q5: Should there be a "force install" mode that bypasses pre-checks?
**Answer:** Yes — a run-level `ForceInstall` flag.
- A boolean on `CreateRunRequest` → `WorkloadRun.ForceInstall`.
- The orchestrator sends it in `AssignRunPayload`.
- The agent checks it in the pre-check phase: if `ForceInstall` is true, pre-check always returns "Not Present" for every package, causing full reinstall.
- **UI:** A "Force reinstall" checkbox on the run creation form. Checked → `forceInstall: true` in the API request.

---

## Rejected Alternatives

### Alternative A: Agent Self-Detection Registry
The agent maintains a local registry of detected packages, independent of the orchestrator's `CurrentPackages`. Rejected because it duplicates state and creates ambiguity about which list is authoritative.

### Alternative B: Orchestrator-Side Baseline Import
Operators manually specify a node's pre-installed packages during enrollment. Rejected because it's operationally fragile and doesn't handle packages installed after enrollment.

### Alternative C: Post-Install Only (Status Quo)
Keep relying only on `PostInstallVerify`. Rejected because it doesn't prevent redundant installs; it only reports success/failure after the fact.

---

## References

- [detection-config-persistence.md](./detection-config-persistence.md) — How `DetectionConfig` is stored and retrieved
- [workload-differential-update-rollback.md](./workload-differential-update-rollback.md) — How `DiffEngine` classifies packages
- `apps/agent/backend/Pipeline/PipelineExecutor.cs` — Pipeline execution logic
- `apps/agent/backend/Steps/PostInstallVerify.cs` — Existing detection logic
- `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs` — `BuildDetectionConfig` method
