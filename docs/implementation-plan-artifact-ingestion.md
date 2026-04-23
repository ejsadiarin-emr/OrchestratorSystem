# Implementation Plan: Artifact Ingestion Improvements

Based on decisions in `docs/decisions/artifact-ingestion-nitpicks.md`.

## Architecture

### Backend

#### New Services
- `UploadSessionService` — manages `ConcurrentDictionary<string, UploadSession>` for chunked upload state, temp file assembly
- `ArtifactZipService` — extracts and validates zip contents using `System.IO.Compression.ZipArchive`

#### New Models/Contracts
- `UploadSession` — sessionId, createdAt, tempDir, manifest, totalChunks, receivedChunks
- `BulkIngestResult` — per-file results array with status/reason/artifact

#### Controller Changes
- `POST /api/artifacts/upload-sessions` — create session, returns `{ sessionId }`
- `POST /api/artifacts/upload-sessions/{id}/chunks` — accept chunk (query: index, totalChunks)
- `POST /api/artifacts/upload-sessions/{id}/complete` — assemble, ingest, cleanup
- `POST /api/artifacts/bulk` — accept bulk zip, extract pairs, ingest each, return `BulkIngestResult`
- Modify existing `POST /api/artifacts` — detect if file is a zip, extract single pair, ingest

#### Cleanup
- Immediate try/catch delete on complete success
- Startup purge of `_temp/` directories

### Frontend

#### Dependencies
- `fflate` — client-side zip decompression for preview

#### New Components
- `components/ui/stepper.tsx` — horizontal stepper with connecting lines
- `components/ui/progress.tsx` — custom Tailwind progress bar

#### New API Functions
- `createUploadSession()` — POST /api/artifacts/upload-sessions
- `uploadChunk(sessionId, index, totalChunks, chunk)` — POST chunks
- `completeUploadSession(sessionId)` — POST complete
- `uploadArtifactWithProgress()` — XHR wrapper with onprogress callback
- `uploadBulkArtifacts()` — POST bulk zip with XHR progress

#### Page Redesign: Packages.tsx → ArtifactStore.tsx
- Rename route from `/packages` to `/artifacts` (keep `/packages` redirect)
- Segmented control: Standalone Installer | Single Zip Artifact | Bulk Zip Artifacts
- Standalone mode: dropzone + manifest form (existing behavior)
- Single Zip mode: dropzone for .zip, fflate preview shows detected pair, ingest button
- Bulk Zip mode: dropzone for .zip, fflate preview lists all pairs, "Ingest All" button
- Progress: determinate progress bar during upload
- Stepper timeline: horizontal stepper for ingest steps (receive → analyze → verify → store)
- Artifact inventory: cards grid (1/2/3 cols) replacing table

## Task Breakdown

### Phase 1: Backend Foundation
1.1. Create `UploadSession` model and `UploadSessionService` with in-memory storage
1.2. Create `ArtifactZipService` for zip extraction and pair validation
1.3. Add upload session controller endpoints
1.4. Update `ArtifactsController.Ingest` to detect and handle single zip files
1.5. Add `POST /api/artifacts/bulk` endpoint
1.6. Add startup cleanup for `_temp/` in `Program.cs`

### Phase 2: Frontend Components
2.1. Install `fflate` in web project
2.2. Create `components/ui/stepper.tsx`
2.3. Create `components/ui/progress.tsx`
2.4. Create zip preview utilities (`lib/zip-preview.ts`)
2.5. Add upload session API functions to `services/api.ts`
2.6. Add XHR upload with progress to `services/api.ts`

### Phase 3: Frontend Page
3.1. Create `pages/ArtifactStore.tsx` with all three upload modes
3.2. Add card-based artifact inventory grid
3.3. Add stepper timeline to upload flow
3.4. Update `App.tsx` routes (`/artifacts` + `/packages` redirect)

### Phase 4: Tests
4.1. Integration tests for upload session endpoints
4.2. Integration tests for zip ingest (single + bulk)
4.3. Unit tests for `ArtifactZipService`

### Phase 5: Verification
5.1. Run all backend tests (`dotnet test`)
5.2. Build frontend (`pnpm build`)
5.3. Verify no TypeScript errors
