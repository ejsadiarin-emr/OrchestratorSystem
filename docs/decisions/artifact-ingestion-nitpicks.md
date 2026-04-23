# Artifact Ingestion Nitpicks — Grill Me Decisions Log

## Context
Interview session resolving nitpicks from `README.md` line 135 (Step 1: Upload an artifact).

---

## Decision 1: Individual Zip Artifact Naming Convention
**Date:** 2026-04-23
**Status:** Accepted

Files inside an individual zip share a base name:
- Installer media: `<base>.<ext>` (e.g., `PackageA-1.0.0.msi`)
- Metadata: `<base>.manifest.json` (e.g., `PackageA-1.0.0.manifest.json`)

PackageId and Version are read from the manifest JSON contents, not the filenames.

---

## Decision 2: Bulk Zip Structure
**Date:** 2026-04-23
**Status:** Accepted

Flat root structure inside the bulk zip — no subfolders. All file pairs directly at the root:
```
PackageA-1.0.0.msi
PackageA-1.0.0.manifest.json
PackageB-2.1.0.exe
PackageB-2.1.0.manifest.json
Runtime-3.0.0.zip
Runtime-3.0.0.manifest.json
```

Files are grouped by shared base name. Unpaired files (media without manifest, or manifest without media) are rejected with a clear validation error.

---

## Decision 3: API Endpoint Design
**Date:** 2026-04-23
**Status:** Accepted

Two endpoints:
- `POST /api/artifacts` — handles standalone binary + manifest AND individual zip artifact (single pair inside). Returns single artifact result.
- `POST /api/artifacts/bulk` — accepts zip/tar archives with multiple artifact pairs. Returns batch result: `{ results: [{ artifact, status, errors? }] }`.

The single-artifact path is preserved unchanged from current behavior for standalone binaries.

---

## Decision 4: Frontend Upload Mode Selection
**Date:** 2026-04-23
**Status:** Accepted

Segmented control (radio button group) above the dropzone with three options:
1. **Standalone Installer** — accepts `.msi`, `.exe`, `.zip`. Manifest form appears below, prefilled from file analysis. Form data is serialized to JSON and sent as the `manifest` form field.
2. **Single Zip Artifact** — accepts `.zip`, `.tar.gz`. Client-side preview extracts zip names, detects the single pair, shows "Found: ..." summary. No manual manifest form needed (it's inside the zip).
3. **Bulk Zip Artifacts** — accepts `.zip`, `.tar.gz`. Client-side preview lists all detected pairs, then user clicks "Ingest All" which POSTs to `/api/artifacts/bulk`.

Defaults to **Standalone Installer**.

---

## Decision 5: Client-Side Zip Preview Library
**Date:** 2026-04-23
**Status:** Accepted

Use `fflate` (~7KB gzipped) instead of `jszip` (~80KB). Modern API, significantly faster, supports zip decompression for filename extraction and pair matching.

---

## Decision 6: Upload Progress Tracking
**Date:** 2026-04-23
**Status:** Accepted

Real `XMLHttpRequest` with `onprogress` for determinate progress bars. Refactor `uploadArtifact` and new `uploadBulkArtifacts` to use XHR and expose progress callbacks.

For bulk uploads, show overall progress + per-file status.

---

## Decision 7: Big File Upload Strategy (PoC)
**Date:** 2026-04-23
**Status:** Accepted

Single large POST with real progress tracking for the PoC. No chunked implementation yet, but the design must be ready for it.

---

## Decision 8: Upload Session Model
**Date:** 2026-04-23
**Status:** Accepted

Implement a lightweight upload session model now to make chunked uploads trivial to enable later.

Proposed backend flow:
1. `POST /api/artifacts/upload-sessions` — creates a session. Body: optional `manifest`. Returns `{ sessionId }`.
2. `POST /api/artifacts/upload-sessions/{sessionId}/chunks` — accepts a chunk. Query params: `index`, `totalChunks`. Body: raw bytes or multipart `chunk` file. Service appends to a temp file.
3. `POST /api/artifacts/upload-sessions/{sessionId}/complete` — finalizes assembly, runs ingest logic, returns result.

For the PoC demo, the frontend conceptually does a "single POST": create-session → upload chunk 0/1 → complete — all in one async flow. The frontend API facade hides this from UI code.

---

## Decision 9: Ingest Timeline UI
**Date:** 2026-04-23
**Status:** Accepted

Horizontal stepper with connecting lines for the 4 main steps (Receive → Analyze → Verify → Store). Active step pulses, completed steps get checkmarks. Appears during upload and in artifact detail modal. More professional than colored dots.

---

## Decision 10: Artifact Inventory Layout
**Date:** 2026-04-23
**Status:** Accepted

Cards-only, replacing the table. CSS grid `grid-cols-1 md:grid-cols-2 lg:grid-cols-3`. Each card shows Package ID, version, channel badge, artifact type, size, risk level, and view details.

---

## Decision 11: Demo Flow Structure
**Date:** 2026-04-23
**Status:** Accepted

Three subsections under Step 1:
- **Step 1a:** Small artifact (fast demo, Single Zip mode)
- **Step 1b:** Big artifact (progress demo, Standalone mode)
- **Step 1c:** Bulk upload (multiple artifacts, Bulk Zip mode)

---

## Decision 12: Backend Zip Extraction
**Date:** 2026-04-23
**Status:** Accepted

`System.IO.Compression.ZipArchive` (built-in). `.zip` support first; `.tar.gz` can be added later via `System.Formats.Tar`.

---

## Decision 13: Upload Session Cleanup
**Date:** 2026-04-23
**Status:** Accepted

Two-layer simplified cleanup (revised from 3-layer for PoC simplicity):

### Layers
1. **Immediate cleanup** — `try { Directory.Delete(tempDir, true); } catch { }` after `complete` succeeds. Never fails the request.
2. **Startup cleanup** — on `Program.cs` startup, iterate `_temp/` and delete all directories with try/catch.

### Rationale
No background hosted service needed. Temp files from crashed sessions survive until next restart — acceptable for PoC demos. Can add hosted service later as 10-line addition.

### Layers
1. **Immediate cleanup on success** — temp file deleted after `complete` succeeds
2. **Stale session cleanup** — background hosted service deletes sessions older than 24h
3. **Startup cleanup** — clear leftover temp files on orchestrator startup

### Simple Mitigations
- All deletes wrapped in try/catch (never fail the request)
- Temp files in `artifacts/_temp/{sessionId}/` (GUID subdirectories, no collisions)
- Stale cleanup: every 6 hours, delete folders older than 24h by `LastWriteTime`
- Startup cleanup: iterate `_temp/`, delete all directories (no DB consistency check — just purge)
- No disk-full alert for PoC (out of scope)

### Known Failure Modes (Accepted Risk)
- File locked by AV/process → caught by try/catch, cleaned up by stale/startup sweep
- DB record leak → harmless, temp file still cleaned up by stale/startup
- Temp file leak → cleaned up by startup
- Multiple instances sharing temp dir → accepted for PoC (not production)

---

## Decision 14: Frontend Progress Bar
**Date:** 2026-04-23
**Status:** Accepted

Custom Tailwind progress bar. Simple `<div>` with percentage width and transition. No library dependency needed.

---

## Decision 15: Horizontal Stepper Component
**Date:** 2026-04-23
**Status:** Accepted

Custom `Stepper` component in `components/ui/stepper.tsx`. 3–4 divs with borders and icons. Consistent with existing custom UI components.

---

## Decision 16: Upload Session State Storage
**Date:** 2026-04-23
**Status:** Accepted

In-memory `ConcurrentDictionary<string, UploadSession>`. No persistence across restarts needed for PoC. No schema migration or EF overhead. Can add SQLite later without changing the service interface.

---

## Decision 17: Error Handling for Partial Bulk Failures
**Date:** 2026-04-23
**Status:** Accepted

Per-file tracking in the response. Each file in the bulk zip gets its own `{ fileName, status: "success" | "failed", reason?: string, artifact?: { packageId, version } }` result. Client shows a summary count and a per-file breakdown.

---

## Decision 18: Artifact Card Color Palette
**Date:** 2026-04-23
**Status:** Accepted

Neutral colors only (Tailwind `slate`, `zinc`, `gray`). No per-artifact-type color coding in the PoC. Status indicators use standard semantic colors (`green`, `amber`, `red`).

---

## Decision 19: Artifact Card Icons
**Date:** 2026-04-23
**Status:** Accepted

Standard Phosphor icons matching existing app conventions. One icon for artifact type, one for status.

---

## Open Decisions

- *None remaining. All README Step 1 nitpicks resolved.*
