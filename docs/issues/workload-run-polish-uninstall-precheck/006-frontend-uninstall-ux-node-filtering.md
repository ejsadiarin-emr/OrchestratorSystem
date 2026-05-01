# 006 - Frontend: Uninstall UX & Node Filtering

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

- Blocked by #005 (Frontend: Run Creator Core Revamp) — builds on the action buttons and form state established in #005

## What to build

Add the uninstall-specific UX: a confirmation step with warning banner and required checkbox, node filtering that only shows nodes with the workload installed, and per-node package lists in the confirmation. All uninstall-only UI behavior.

**Uninstall Node Filtering (D18):**

- In Uninstall mode, the node picker only shows nodes that have `NodeWorkloadState` for the selected workload
- Nodes without the workload are hidden entirely (not grayed out — absent from the list)
- If no nodes qualify, the Uninstall button is disabled with tooltip: "No nodes have this workload installed"
- Install mode continues to show all online nodes (no change)

**Uninstall Confirmation (D13, D19):**

When the user reaches the summary/confirm step and `form.mode === 'uninstall'`:

- Display a red warning banner:
  - Title: "Warning: This will permanently remove packages from nodes."
  - Subtitle: "The following packages will be uninstalled from N node(s):"
  - Bulleted list of packages (name + version) sourced from the selected revision's `WorkloadPackageEntity` rows
  - If selected nodes have different currently-installed revisions, group packages by node with per-node labels
- Required confirmation checkbox:
  - Label: "I understand that this action cannot be undone."
  - Submit button is **disabled** until this checkbox is checked
- The warning banner is visually prominent (red border, red background tint)

**Per-node package detail in confirmation:**
- Each selected node shows which revision it has and which packages will be removed
- Example:
  ```
  win-prod-01 (v1.0.0): nodejs 22.14.0, python 3.13.3, dbeaver 24.3.0
  win-prod-02 (v2.0.0): nodejs 24.0.0, python 3.14.0
  ```

## Acceptance criteria

- [ ] In Uninstall mode, node picker only shows nodes with `NodeWorkloadState` for the selected workload
- [ ] Nodes without the workload are hidden (not visible in the list)
- [ ] Uninstall button disabled with tooltip when no nodes qualify
- [ ] Warning banner appears in summary step when mode is `'uninstall'` (red styling)
- [ ] Banner lists all packages that will be removed, sourced from revision's package entities
- [ ] Banner shows per-node package lists when nodes have different installed revisions
- [ ] Confirmation checkbox with "I understand that this action cannot be undone." text
- [ ] Submit button disabled until confirmation checkbox is checked
- [ ] Submit button disabled if no nodes selected
- [ ] Warning banner does NOT appear in Install mode
- [ ] `pnpm run typecheck` succeeds
- [ ] Manual smoke: select workload, switch to Uninstall, verify only workload-bearing nodes appear, verify confirmation flow

## Referenced decisions

- [D13: Uninstall Confirmation — Required](../../decisions/workload-run-polish-uninstall-precheck.md#d13-uninstall-confirmation--required)
- [D18: Uninstall Node Filtering — Hide Nodes Without Workload](../../decisions/workload-run-polish-uninstall-precheck.md#d18-uninstall-node-filtering--hide-nodes-without-workload)
- [D19: Uninstall Warning — Package List from Revision](../../decisions/workload-run-polish-uninstall-precheck.md#d19-uninstall-warning--package-list-from-revision)
