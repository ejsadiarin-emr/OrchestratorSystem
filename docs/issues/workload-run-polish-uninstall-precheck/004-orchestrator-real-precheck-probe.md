# 004 - Orchestrator Real Pre-Check Probe & Reconciliation

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

- Blocked by #003 (Agent Detect Endpoint) — agent must expose `POST /api/detect`

## What to build

Replace the hardcoded pre-check stub in the orchestrator with real agent HTTP probes. When the orchestrator receives a pre-check request, it calls each agent's `/api/detect` endpoint, reconciles the results against DB state, updates `NodeWorkloadState` where drift is detected, and returns accurate per-node status.

**Orchestrator Proxy Endpoint (revised):**

`POST /api/nodes/{nodeIds}/prechecks`:
- Accepts a list of node IDs and optional `workloadId`
- Orchestrator loads detection configs for all packages in all published revisions of the workload (or all workloads if no `workloadId`)
- For each target node, calls `POST http://{node.IpAddress}:5001/api/detect` via `IHttpClientFactory`
- Collects per-package results and runs reconciliation logic
- Returns updated per-node pre-check summaries to the UI

**Backend (`apps/orchestrator/backend/`):**

- `Controllers/NodesController.cs:251-268`: Replace `RunPreChecks()` stub with real HTTP probe implementation
- `Controllers/NodesController.cs:270-315`: Replace `BuildPreCheckSummary()` with `ReconcileProbeResults()` that applies the reconciliation rules below
- `appsettings.json`: Add `"AgentProbeTimeoutSeconds": 30` config value
- Register `IHttpClientFactory` in DI if not already registered; configure a named or typed client with the timeout from config

**Reconciliation Logic (D5, D16):**

| Scenario | DB says | Agent says | Action |
|---|---|---|---|
| A — Match | Node has revision v1 | Same version, all packages present | No-op |
| B — Missing | Node has v1 | No packages found | Clear `NodeWorkloadState`, show "not installed" |
| C — Pre-existing | Node has nothing | All v1 packages present | Create `NodeWorkloadState` with `CurrentRevisionId = v1.Id`, populate `PackageStatesJson` with detected versions |
| D — Drift (version mismatch) | Node has v1 (dbeaver 24.3.0) | dbeaver 24.3.0 missing, dbeaver 26.0.3 present | Update `PackageStatesJson` to reflect actual state; keep `CurrentRevisionId = v1`; show "drift detected" |
| E — Drift (partial) | Node has v1 (python+dbeaver) | python OK, dbeaver missing | Update `PackageStatesJson`: mark dbeaver as missing; show "drift: 1/2 packages present" |

Key constraint: **Never auto-promote `CurrentRevisionId`.** When drift is detected, update `PackageStatesJson` but leave `CurrentRevisionId` unchanged. The UI surfaces drift as a yellow badge.

**Probe error handling:**
- Agent unreachable (timeout, connection refused): return error status per-node in the pre-check summary, do NOT modify DB state
- Agent returns non-200: log warning, return error to UI, do NOT modify DB state
- Partial results (some packages probed, some failed): return what we have with a warning indicator

## Acceptance criteria

- [ ] `POST /api/nodes/{nodeIds}/prechecks` calls agent `/api/detect` for each target node via `IHttpClientFactory`
- [ ] Detection configs are loaded from all published revision packages for the target workload(s)
- [ ] Reconciliation correctly handles all five scenarios (A-E) with correct DB updates
- [ ] `CurrentRevisionId` is NEVER auto-promoted — left unchanged during drift
- [ ] `PackageStatesJson` is updated to reflect actual detected state
- [ ] Agent unreachable returns error per-node without modifying DB
- [ ] Probe respects `AgentProbeTimeoutSeconds` config (default 30s)
- [ ] Agent returns non-200: error surfaced, DB untouched
- [ ] `dotnet build` succeeds for orchestrator project
- [ ] Manual smoke: call pre-check API, verify real detection results (no more hardcoded `"passed"`)

## Referenced decisions

- [D2: Pre-Check — Hybrid Source of Truth](../../decisions/workload-run-polish-uninstall-precheck.md#d2-pre-check--hybrid-source-of-truth)
- [D4: Pre-Check Trigger — Auto-Load + Manual Refresh](../../decisions/workload-run-polish-uninstall-precheck.md#d4-pre-check-trigger--auto-load--manual-refresh)
- [D5: Reconciliation — Agent Truth Wins, Don't Auto-Guess](../../decisions/workload-run-polish-uninstall-precheck.md#d5-reconciliation--agent-truth-wins-dont-auto-guess)
- [D15: Probe Communication — Direct HTTP](../../decisions/workload-run-polish-uninstall-precheck.md#d15-probe-communication--direct-http)
- [D16: Reconciliation Detail — Surface Truth, Don't Auto-Guess](../../decisions/workload-run-polish-uninstall-precheck.md#d16-reconciliation-detail--surface-truth-dont-auto-guess)
