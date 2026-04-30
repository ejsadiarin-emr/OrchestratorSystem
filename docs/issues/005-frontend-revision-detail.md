# 005 - Frontend: Revision Detail Display

## Type

AFK

## Parent PRD

[docs/prd-init-steps.md](../prd-init-steps.md)

## Blocked by

- Blocked by #002 (Orchestrator Import & Dispatch) — API must serve init step data

## What to build

Add init step display to the workload revision detail page (`apps/orchestrator/web/`). This is display-only (read-only) — no create/edit UI. JSON import is the authoring path.

**Per-package init steps:**

- Append a collapsible section to each package entry on the revision detail page
- Show pre-init and post-init commands as formatted code blocks
- Section is **collapsed by default**
- Packages **without** init steps show **no expansion indicator** (no caret/chevron/arrow) — only packages with at least one init step get the toggle
- Pre-init commands listed before post-init commands within the section

**Post-workload steps:**

- Displayed in a **separate section** at the bottom of the revision detail page (below all packages)
- Visually distinct from per-package sections (different heading, separate card/container)
- Same code block formatting as per-package commands

**defaultShell display:**

- Show the configured `defaultShell` value somewhere on the revision detail page

**Force Install note:**

- Near packages that show as "unchanged" on the revision detail page, display a note explaining: init steps won't run for unchanged packages unless Force Install is used

## Acceptance criteria

- [ ] Packages with `preInitSteps` or `postInitSteps` show a collapsible section (collapsed by default)
- [ ] Packages with empty init steps arrays show NO expansion indicator
- [ ] Expanding a package's section reveals pre-init commands as formatted code blocks, followed by post-init commands
- [ ] Post-workload steps displayed in a separate section at the bottom of the page
- [ ] Post-workload section is visually distinct from per-package sections
- [ ] `defaultShell` value displayed on the page
- [ ] Note explaining Force Install behavior visible near unchanged packages
- [ ] Handles empty/null init step arrays gracefully (no errors, no empty sections)
- [ ] Visual consistency with existing revision detail page styling and layout

## Referenced decisions

- [Frontend Scope](../decisions/20260430-workload-init-steps/workload-init-steps-frontend-scope-20260430-002100.md)
- [Frontend Display](../decisions/20260430-workload-init-steps/workload-init-steps-frontend-display-20260430-002200.md)
- [Diff Engine Interaction](../decisions/20260430-workload-init-steps/workload-init-steps-diff-engine-20260430-003000.md)
- [ForceInstall Interaction](../decisions/20260430-workload-init-steps/workload-init-steps-force-install-20260430-002500.md)
