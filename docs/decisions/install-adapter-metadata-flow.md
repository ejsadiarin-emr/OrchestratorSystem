# Install Adapter Metadata Flow — Grill Me Decisions Log

## Context
Discussion resolving how packages are installed on remote Windows agents via workloads.
Key questions: (1) Are installer arguments auto-generated or admin-provided? (2) Why can't agents install downloaded artifacts today?

---

## Decision 1: Admin-Provided Manifests Are the Source of Truth for Ingested Artifacts
**Date:** 2026-04-25
**Status:** Accepted

When an artifact is uploaded (as a standalone binary or inside a zip) with an accompanying manifest JSON, the `InstallAdapter` block in that manifest is used exactly as provided.

The `ArtifactIngestService.ResolveAdapter()` precedence chain is:
1. **Admin** — manifest fields explicitly set
2. **Analyzer** — file magic-byte detection
3. **Template** — package-name pattern matching
4. **Default** — file extension fallback (`msi` → `/qn`, `exe` → `/quiet`)

**Implication:** For production use, system admins are expected to upload artifacts with accurate `InstallAdapter` metadata (command, arguments, exit codes, timeout) tailored to that specific installer. No automatic "guessing" of silent flags is required for the common case.

**Example from existing artifacts:**
- `git-2.48.1` uses Inno Setup: `Git-2.48.1-64-bit.exe /VERYSILENT /NORESTART...`
- `nodejs-24.13.0` uses MSI: `node-v24.13.0-x64.msi /quiet /norestart`
- `7zip-26.00` uses NSIS: `7z2600-x64.exe /S /D=C:\Program Files\7-Zip`
- `python-3.14.4` uses WiX/EXE: `python-3.14.4-amd64.exe /quiet InstallAllUsers=1...`

Each has different arguments because each installer family (Inno, MSI, NSIS, WiX) has its own silent-install contract.

---

## Decision 2: Agents Cannot Install Because of Two Bugs, Not Missing Arguments
**Date:** 2026-04-25
**Status:** Accepted

Arguments flow correctly end-to-end:
`manifest → PackageEntity.InstallArgs → PackageAssignment.InstallAdapter.Arguments`

The actual blockers are:

### Bug A: Command points to original filename, not the downloaded temp file
`WorkloadRunsController.Create()` sets `Command = pkg.SourcePath`, which stores the original filename (e.g., `"Git-2.48.1-64-bit.exe"`). The agent downloads the artifact to a temp path like `C:\...\agent-artifacts\{runId}\git-2.48.1`. Executing the original filename fails with `command_not_found`.

**Fix:** Normalize at run-creation time:
- `msi` type → `Command = "msiexec.exe"`, prepend `/i "{artifactPath}"` to arguments
- `exe`/`zip`/other → `Command = "{artifactPath}"`, keep arguments as-is

### Bug B: `ExpectedExitCodes` and `TimeoutSeconds` are discarded during storage
`PackageEntity` only stores `SourcePath`, `InstallType`, `InstallArgs`. The manifest's `ExpectedExitCodes: [0, 3010]` and `TimeoutSeconds: 300` are dropped. `WorkloadRunsController` hardcodes `ExpectedExitCodes = [0]` and `TimeoutSeconds = 300`.

This causes false failures for installers that return 3010 (success, reboot required), such as MSI and WiX Burn bundles.

**Fix:** Add `ExpectedExitCodesJson` and `TimeoutSeconds` columns to `PackageEntity`, persist them during ingestion, and read them during run creation.

---

## Decision 3: Pattern Registry for Bulk-Imported Placeholder Packages
**Date:** 2026-04-25
**Status:** Accepted

When workloads are bulk-imported via `workloads.json`, placeholder `PackageEntity` records are created if the package does not already exist. These placeholders have `InstallType = "unknown"` and empty arguments.

For the PoC, a lightweight **in-code pattern registry** resolves the installer type and silent flags for known packages:

| Package Pattern | Installer Type | Command | Arguments | Exit Codes | Timeout |
|---|---|---|---|---|---|
| `git-*` | `exe` (Inno) | `artifact.bin` | `/VERYSILENT /NORESTART /NOCANCEL /SP-` | `[0]` | 300 |
| `nodejs-*` | `msi` | `msiexec.exe` | `/i "{artifactPath}" /quiet /norestart` | `[0, 3010]` | 300 |
| `7zip-*` | `exe` (NSIS) | `artifact.bin` | `/S` | `[0]` | 120 |
| `python-*` | `exe` (WiX) | `artifact.bin` | `/quiet InstallAllUsers=1 PrependPath=1 Include_test=0` | `[0]` | 300 |
| `dotnet-runtime-*` | `exe` | `artifact.bin` | `/quiet /norestart` | `[0, 3010]` | 300 |
| `test-agent-*` | `exe` | `artifact.bin` | `/S` | `[0]` | 60 |

**Rationale:** This is a pragmatic fallback for placeholder packages. Once an artifact is actually ingested with a proper manifest, the real manifest metadata overrides the registry.

---

## Decision 4: No Automatic File-Signature Detection Required for PoC
**Date:** 2026-04-25
**Status:** Accepted

While the codebase already has basic magic-byte detection (`TryAnalyzeFileContent` — PE vs MSI vs ZIP), expanding it to distinguish Inno Setup, NSIS, WiX Burn, and InstallShield is deferred.

**Reason:** The current artifacts all come with admin-provided manifests that already contain the correct `InstallAdapter`. File-signature detection would only help for artifacts uploaded *without* a manifest, which is not the intended workflow.

**Future:** If the system needs to support "drag-and-drop installer with no manifest," a signature analyzer can be added to the precedence chain between "analyzer" and "template."

---

## Open Decisions

- *None.*
