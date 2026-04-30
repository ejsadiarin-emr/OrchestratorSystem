# 006 - Frontend: Run Timeline Display

## Type

AFK

## Parent PRD

[docs/prd-init-steps.md](../prd-init-steps.md)

## Blocked by

- Blocked by #004 (Agent Pipeline Integration) — agent must actually report init step timeline entries before the frontend can display them

## What to build

Add init step entries to the workload run timeline view (`apps/orchestrator/web/`). Each init step command is reported as its own pipeline step with a unique step name, and the timeline must display these alongside existing pipeline steps (`AcquireArtifact`, `InstallOrUpgrade`, `PostInstallVerify`).

**Step naming convention to display:**

| Step Type | Name Format | Examples |
|-----------|-------------|----------|
| Pre-init | `PreInit_{packageIndex}_{stepIndex}` | `PreInit_0_0`, `PreInit_0_1` |
| Post-init | `PostInit_{packageIndex}_{stepIndex}` | `PostInit_1_0` |
| Pre-workload | `PreWorkload_{stepIndex}` | `PreWorkload_0` |
| Post-workload | `PostWorkload_{stepIndex}` | `PostWorkload_0`, `PostWorkload_1` |

**Display behavior:**

- Use existing step display patterns/cards for consistency
- Each init step shows: step name, status (Running/Success/Failed), and any error message with stdout/stderr on failure
- Init steps appear in execution order within the timeline (pre-workload → each package's pre-init → acquire → install → verify → post-init → post-workload)
- Package association must be clear: pre-init and post-init steps should be visually nested under or associated with their package's other steps

**Error message display:**

- On failure, the step's `Message` field contains the combined stdout/stderr output — display this in the timeline entry
- Use existing error display patterns (expandable error details, code block formatting)

## Acceptance criteria

- [ ] `PreWorkload_*` steps appear at the top of the timeline (before any package steps)
- [ ] `PreInit_*` steps appear before each package's `AcquireArtifact` step
- [ ] `PostInit_*` steps appear after each package's `PostInstallVerify` step
- [ ] `PostWorkload_*` steps appear at the bottom of the timeline (after all package steps)
- [ ] Step status indicators (Running, Success, Failed) display correctly
- [ ] Failed init steps show error message with captured stdout/stderr in expandable detail
- [ ] Pre-init and post-init steps are visually associated with their package
- [ ] Handling of empty/null init steps (no timeline entries when no commands defined)
- [ ] Visual consistency with existing timeline step cards/entries
- [ ] Timeline refreshes correctly when new init step statuses arrive

## Referenced decisions

- [Timeline Reporting](../decisions/20260430-workload-init-steps/workload-init-steps-timeline-20260430-001000.md)
- [Frontend Scope](../decisions/20260430-workload-init-steps/workload-init-steps-frontend-scope-20260430-002100.md)
- [Frontend Display](../decisions/20260430-workload-init-steps/workload-init-steps-frontend-display-20260430-002200.md)
- [Output Capture](../decisions/20260430-workload-init-steps/workload-init-steps-output-capture-20260430-003100.md)
