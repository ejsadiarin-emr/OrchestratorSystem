# Home Drilldowns and Info Hints Design

Date: 2026-04-20
Status: Approved for implementation
Scope: Home dashboard interaction and observability refinements in `apps/orchestrator/web`.

## 1. Objective

Improve Phase-1 operator observability on Home by introducing row-click drilldowns for both primary tables, consolidating node mini logs into contextual detail view, and standardizing ambiguous metric explanations through reusable info hints.

The design must remain workload-first and align with distributed-installer documents:

- `docs/distributed-installer/poc-phase1-prd-final.md`
- `docs/distributed-installer/storyboard-phase1-workload-aligned.md`
- `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`

## 2. Approved Interaction Model

- `Nodes Live` and `Workloads Overview` rows become primary drill-down interactions.
- Both tables open a shared right-side drawer pattern.
- Existing standalone mini-log section beneath the node table is removed.
- Existing node-row log button is removed because row-click drilldown becomes canonical.
- Existing node action controls remain UI-only and non-destructive in this phase.

## 3. Drawer Information Architecture

## 3.1 Shared drawer shell

- Header: entity identity, health/status chips, close affordance.
- Body: consistent section rhythm and label semantics across node and workload views.
- Footer: only when context-specific actions are relevant.

## 3.2 Node detail content

- Workload assignment and revision/version state.
- Update signals:
  - revision update availability
  - package update availability/count
- Risk and reason with standardized explanatory hints.
- Embedded mini-log view sourced by node id.
- Existing action panel controls in the same context.

## 3.3 Workload detail content

- Workload identity and revision summary.
- Nodes running summary and concise node list.
- Revision drift indicators across nodes where present.
- Package update signals derived from node telemetry.
- Explicit copy indicating derived (not artifact-store authoritative) package signal source.

## 4. Data and Derivation Rules

- Node drawer reads directly from existing Home node payload and `logsByNodeId`.
- Workload drawer uses deterministic derivation from node rows already available in Home.
- Derived fields include:
  - nodes running count
  - mixed revision detection
  - package update signal totals
  - impacted-node snapshots
- If detailed package names are not available, display aggregate indicators and label current granularity limits.
- No backend API contract expansion is required for this design.

## 5. Info Hints and Risk Copy Refactor

- Introduce centralized glossary map for ambiguous operational fields (for example `INFO_HINTS`).
- Replace ad-hoc inline risk explanation text with reusable info indicators (`i`) tied to glossary entries.
- Initial hint coverage:
  - `Risk (Node)`
  - `Reason`
  - `Revision Updates`
  - `Package Update Signals`
  - `Nodes Running`
  - `Pending Approvals`
- Hint behavior:
  - desktop: hover/focus
  - mobile/touch: click/tap

## 6. Accessibility and Responsive Requirements

- Table rows are keyboard-operable for drilldown (`Enter`/`Space`).
- Drawer supports keyboard close (`Esc`) and focus-safe open/close behavior.
- Tooltip/hint interactions expose text to assistive tech and are not hover-only.
- Critical statuses are not color-only; text labels and chips remain explicit.

## 7. Error and Empty-State Behavior

- No logs for selected node: explicit no-data message.
- Missing selected entity after refresh: explicit unavailable-state with close/retry path.
- Workload derivation partial gaps degrade per section, not full-view failure.
- Avoid silent blank fields for operator-critical signals.

## 8. Implementation Boundaries

- Keep primary changes centered in `apps/orchestrator/web/src/pages/Dashboard.tsx`.
- Reuse existing `types.ts` and `services/api.ts` contracts; add only minimal optional typing/helpers if required.
- No route additions for detail views in this pass.
- No artifact-store integration in this pass.

## 9. Acceptance Criteria

1. Clicking a node row opens node detail drawer containing contextual mini logs and node signals.
2. Clicking a workload row opens workload detail drawer with derived revision/package signals.
3. Standalone mini-log panel and row-level log button are removed from Home.
4. Targeted ambiguous fields show reusable info indicators with consistent glossary text.
5. Risk explanation behavior is refactored to centralized hint-driven copy.
6. Existing mock action controls continue to work as UI-only interactions.
7. Home interactions are keyboard accessible and responsive.

## 10. Out of Scope

- Real artifact-store package diff checks.
- Deep-link routes for node/workload drilldown.
- Backend mutation behavior for node actions.
