# /nodes Page Refinement — Grill Me Decisions Log

## Context
Discussion resolving UI/UX and functionality improvements for the `/nodes` page, including node naming, renaming, deletion, metadata display, enrollment token flow, and workload-run node selection.

---

## Decision 1: Add a `DisplayName` Field as the Canonical Friendly Name
**Date:** 2026-04-26
**Status:** Accepted

A new `DisplayName` column/property is added to `NodeEntity`. It is the user-facing friendly name shown in tables, dropdowns, and dashboards. `Hostname` remains the stable technical identifier (auto-generated GUID-based string).

- `DisplayName` is **not** unique. Uniqueness is enforced on `Hostname` only.
- Backend sets `DisplayName = hostname` at creation time if no name is provided.
- Existing nodes without `DisplayName` are backfilled with their `hostname`.

**Implication:** All UI surfaces that currently show `hostname` as the primary label switch to `DisplayName`. `hostname` is shown as muted subtitle/context where useful.

---

## Decision 2: Name Provisioning — Both Agent CLI and Orchestrator UI
**Date:** 2026-04-26
**Status:** Accepted

Nodes can receive a `DisplayName` from two sources:

1. **Agent CLI at enrollment** — `DeploymentPoC.Agent.exe --enroll <token> --orchestrator-url=<url> --name "My Server"`
2. **Orchestrator UI after enrollment** — inline rename on the Registered Nodes table.

The orchestrator UI is the **source of truth**. If the agent sends a name during enrollment, it sets the initial `DisplayName`. If no name is sent, the backend defaults to `hostname`.

**Implication:** The agent enrollment request body gains an optional `displayName` field. The `PUT /api/nodes/{id}` endpoint gains a `displayName` field for renaming.

---

## Decision 3: Default Display Name = Hostname
**Date:** 2026-04-26
**Status:** Accepted

When a node is created (via enrollment token consumption or manual creation) and no `DisplayName` is explicitly provided, the backend sets `DisplayName = hostname`.

**Implication:** No node ever has a null/empty `DisplayName`. The UI can always render a meaningful label.

---

## Decision 4: Remove IP Column from Registered Nodes Table
**Date:** 2026-04-26
**Status:** Accepted

The **IP** column is removed entirely from the Registered Nodes table on the `/nodes` page.

- The column currently shows `0.0.0.0` for almost all nodes because the agent does not send a real IP during enrollment.
- IP is not used in any business logic (workload runs, heartbeats, or routing).
- The `IpAddress` field remains on the backend entity for future use but is not displayed.

**Implication:** The frontend table gains horizontal space. If IP becomes relevant later, it can be shown on a node detail drawer/page instead.

---

## Decision 5: OS and Agent Version — Sent During Enrollment and Heartbeats
**Date:** 2026-04-26
**Status:** Accepted

The agent populates `OsVersion` and `AgentVersion` metadata:

1. **During enrollment** — sent in the consume-token request body.
2. **With every `LeaseHeartbeat`** — sent as fields in the heartbeat payload, updating the node's metadata in real time.

This supports in-place agent upgrades: if the agent binary is replaced and restarted, the next heartbeat updates `AgentVersion` without re-enrollment.

**Implication:**
- `AgentEnrollmentService` sends `osVersion` and `agentVersion` in the consume-token body.
- `AgentRuntimeService` includes `osVersion` and `agentVersion` in every `LeaseHeartbeat`.
- `NodeWorkloadStateService` updates `node.OsVersion` and `node.AgentVersion` on heartbeat processing.
- The `/nodes` page Metadata column shows real values instead of blanks.

---

## Decision 6: Registered Nodes is the Hero Section; Enrollment Tokens is Secondary
**Date:** 2026-04-26
**Status:** Accepted

The `/nodes` page layout is restructured:

- **Registered Nodes** is the primary, full-width table at the top.
- **Enrollment Tokens** shrinks to a compact section below, showing only essential columns.
- The "Bootstrap script and first connect" simulation section is **removed**.
- A small note with the CLI command format replaces the bootstrap section: `DeploymentPoC.Agent.exe --enroll <token> --orchestrator-url=<url>`.

**Implication:** The page focuses on managing existing nodes. Token creation is a supporting action.

---

## Decision 7: Token Creation Flow — Modal + Copyable CLI Command
**Date:** 2026-04-26
**Status:** Accepted

Creating an enrollment token is a two-step modal flow:

1. **Form step** — User clicks "Generate Token", a modal opens with fields: `requestedBy` and `TTL` (1–120 min).
2. **Result step** — On submit, the modal switches to display the generated token and a copyable CLI command: `DeploymentPoC.Agent.exe --enroll <token> --orchestrator-url=<url> --name "Optional Name"`.

**Implication:** No inline form on the main page. The modal provides a clean, focused experience and a one-click copy action.

---

## Decision 8: Node Rename — Inline on Table Row
**Date:** 2026-04-26
**Status:** Accepted

Renaming a node is done **inline** in the Registered Nodes table:

- A pencil icon on each row triggers edit mode.
- The `DisplayName` cell becomes a text input in place.
- **Enter** saves; **Escape** cancels.
- `hostname` is shown as muted subtitle under `DisplayName` in read mode.

**Implication:** No separate rename modal. Fast, spreadsheet-like UX for quick edits.

---

## Decision 9: Node Delete — Confirmation Modal
**Date:** 2026-04-26
**Status:** Accepted

Deleting a node requires explicit confirmation:

- A trash icon on each row opens a confirmation modal.
- The modal asks: "Delete node '<DisplayName>'? This action cannot be undone."
- On confirm, the node is permanently deleted.

**Implication:** Guardrail against accidental deletion. No soft-delete or undo mechanism for PoC.

---

## Decision 10: Retain Workload Run History on Node Deletion
**Date:** 2026-04-26
**Status:** Accepted

When a node is deleted, its workload run history is **retained** for auditing.

- `WorkloadRunEntity.NodeId` is made **nullable**.
- The foreign key constraint changes from `OnDelete(Cascade)` to `OnDelete(SetNull)`.
- Historical run views show the node name captured at the time of the run (stored on `WorkloadRunEntity`), or "Unknown Node" if not available.

**Implication:** A database migration is required. Deleting a node no longer destroys audit history.

---

## Decision 11: Enrollment Tokens Table — Compact, Active-First
**Date:** 2026-04-26
**Status:** Accepted

The Enrollment Tokens table is redesigned:

- **Compact format** — fewer columns, smaller typography, less padding.
- **Active tokens first** — by default, only unconsumed and non-expired tokens are shown.
- **Toggle** — a switch or link allows showing consumed/expired tokens.
- Token revocation deletes the token **immediately from the database** (no soft delete).

**Implication:** The secondary table stays out of the way but remains accessible. Revoked tokens are gone forever.

---

## Decision 12: Workload-Runs Dropdown — DisplayName Only
**Date:** 2026-04-26
**Status:** Accepted

In the `/workload-runs` page, the target node dropdown shows **DisplayName only**.

- The current behavior (showing `hostname`) is replaced.
- If multiple nodes share the same `DisplayName`, the user must disambiguate manually (they can rename one).

**Implication:** The dropdown is cleaner and aligned with the friendly-name system.

---

## Open Decisions

- *None.*
