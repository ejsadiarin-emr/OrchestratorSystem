# Decision: Workload Run Creation - Multi-Node & UX Improvements

**Date:** 2026-04-26

**Context:** The workload run creation dialog had multiple UX and functional issues: a 400 Bad Request caused by an oversized `idempotencyKey`, an opaque revision GUID free-text input, and a restrictive single-node selection that used a poor `<select multiple>` control.

## Decisions

### 1. Idempotency Key Length Fix

**Problem:** The frontend-generated `idempotencyKey` in `api.ts` concatenated 4 GUIDs + mode + timestamp, producing ~149 characters. The backend `CreateWorkloadRunRequest` enforces `[StringLength(128)]`, causing a 400 Bad Request.

**Decision:** Replace the concatenated key with `crypto.randomUUID()`, producing a 36-character UUID well within the 128-character limit.

**Rationale:**
- Unblocks the primary user flow immediately
- UUIDv4 provides sufficient uniqueness for idempotency within the session window
- No backend changes required

### 2. Revision Input Replacement

**Problem:** The revision field was a free-text `<input>` displaying an opaque GUID (`23c1377d-78b0-489c-9806-79058e17c627`). Users could type invalid or unpublished revision GUIDs with no guidance.

**Decision:** Replace the free-text input with a read-only dropdown (`<select>`) populated from the selected workload's published revisions. The dropdown displays human-readable version strings (e.g., `2.0.0`) while mapping internally to the revision GUID for the API request.

**Rationale:**
- Eliminates user error from typing invalid GUIDs
- Surfaces only valid, published revisions
- Preserves backend server-side validation as a safety net
- Better discoverability of available versions

**Details:**
- Only published revisions are shown (`IsPublished=1`)
- Auto-select the most recently published revision when workload changes
- If no published revisions exist, show a disabled option and disable the Create Run button with an explanatory message

### 3. Multi-Node Run Enablement

**Problem:** The frontend artificially restricted runs to a single node (`if (request.targetNodeIds.length !== 1) throw new Error(...)`), despite the backend controller supporting multiple nodes (creates one run per node and broadcasts via SignalR). The UI used a native `<select multiple>`, which is notoriously bad UX.

**Decision:**
- **Remove** the frontend `length !== 1` restriction in `api.ts`
- **Replace** `<select multiple>` with **checkbox cards** in a **scrollable list** (max-height with overflow-y auto)
- **Disable** offline nodes visually and prevent their selection
- **Keep** backend validation that all node IDs must exist and be online

**Rationale:**
- Aligns frontend capabilities with backend support
- Checkbox cards provide clear visual selection state and enable easy multi-select without Ctrl/Cmd-click
- Scrollable container handles large node fleets gracefully
- Offline node disabling prevents invalid runs and provides immediate visual feedback

### 4. Mode Dropdown Cleanup

**Problem:** The mode dropdown included `cancel`, but the backend `TryNormalizeMode` accepts it with no actual business logic implementation. Real cancellation is done via a separate `POST /api/workload-runs/{runId}/cancel` endpoint.

**Decision:** Remove `cancel` from the mode dropdown in the creation dialog. Keep `install`, `update`, `rollback`.

**Rationale:**
- Prevents user confusion from selecting a no-op mode
- Real cancel is accessible from the runs table

### 5. Node Selection UX

**Decision:**
- No default node selection when dialog opens
- Disable the Create Run button until at least one online node is checked
- Add 'Select all online' and 'Clear all' helper text buttons above the node list
- Node cards display: checkbox, hostname (primary), status dot (green=online, gray=offline), OS badge
- Add a real-time filter input above the node list filtering by hostname substring
- Offline nodes remain visible but are disabled (not hidden)

### 6. Layout

**Decision:** Single-page modal with two visual columns on desktop:
- Left column: Workload selector + Revision dropdown (the "what")
- Right column: Scrollable node list with checkboxes (the "where")
- Stacks naturally on mobile

### 7. Confirmation & Submission Flow

**Decision:**
- Clicking "Create Run" first reveals a confirmation summary within the modal (e.g., "Deploy Utility Pack v2.0.0 (install) to 3 nodes")
- User confirms → API call with button spinner
- On success: close modal, show toast notification
- On failure: keep modal open, show error in modal context, stop spinner

## Status

Accepted