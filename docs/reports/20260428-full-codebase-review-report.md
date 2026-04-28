# Codebase Review Report
**Project:** DeploymentPoC  
**Date:** 2026-04-28  
**Scope:** Full stack — .NET backend, React frontend, shared contracts, tests, scripts, and root configuration

---

## Executive Summary

| Severity | Count |
|----------|-------|
| **Critical** | 8 |
| **High** | 12 |
| **Medium** | 28 |
| **Low** | 42 |

The most urgent issues are **missing authentication/authorization across the entire API surface**, **CORS misconfigurations that allow any origin**, **remote code execution vectors on the agent via unsanitized `Process.Start`**, and **frontend API calls that send no auth or CSRF tokens**.

---

## Critical Issues

### 1. Security — No Authentication or Authorization
- **Files:** `apps/orchestrator/backend/Program.cs`, `apps/orchestrator/backend/Controllers/*`, `apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs`, `apps/orchestrator/web/src/services/api.ts`
- **Lines:** `Program.cs:46-137`, `api.ts:353+`
- **Issue:** Neither the backend nor the frontend implements any authentication. There are no `AddAuthentication`, `AddAuthorization`, `UseAuthentication`, or `UseAuthorization` calls. Every controller endpoint and the SignalR hub are fully open. The frontend sends `fetch` requests with zero `Authorization` or `X-CSRF-Token` headers.
- **Recommendation:** Implement JWT or API-key authentication on the backend. Add `[Authorize]` to all sensitive controllers/hubs. Inject auth headers in the frontend API client.

### 2. Security — CORS Falls Back to `AllowAnyOrigin()`
- **File:** `apps/orchestrator/backend/Program.cs`
- **Lines:** `28-44`
- **Issue:** When `Cors:AllowedOrigins` is empty or missing, the app defaults to `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`. Combined with zero authentication, any website can invoke privileged endpoints from a victim's browser.
- **Recommendation:** Remove the permissive fallback. Fail startup if CORS origins are not explicitly configured, or default to a deny-all policy.

### 3. Security — `AllowedHosts: *`
- **File:** `apps/orchestrator/backend/appsettings.json`
- **Line:** `14`
- **Issue:** The wildcard allows host-header injection and cache-poisoning attacks.
- **Recommendation:** Restrict to explicit hostnames or remove the wildcard before production.

### 4. Security — Remote Code Execution on Agent via `Process.Start`
- **Files:** `apps/agent/backend/Steps/InstallOrUpgrade.cs`, `apps/agent/backend/Steps/UninstallPackage.cs`
- **Lines:** `InstallOrUpgrade.cs:67-75,111-118`, `UninstallPackage.cs:43-51`
- **Issue:** The agent executes `config.Command` and `config.Arguments` straight from the orchestrator manifest without validation or sandboxing. A compromised orchestrator or malicious artifact upload leads to immediate RCE on every enrolled node.
- **Recommendation:** Whitelist allowed commands, cryptographically sign manifests, and run installations in a low-privilege sandbox.

### 5. Security — `UpdateStatus` Does Not Filter by Node
- **File:** `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`
- **Lines:** `501-541`
- **Issue:** `UpdateStatus` uses `FirstOrDefaultAsync(r => r.RunId == runId)` without including `NodeId`. For multi-node runs, this arbitrarily updates only the first record, corrupting run state.
- **Recommendation:** Include `NodeId` in the query filter.

### 6. Security — Unauthenticated Enrollment Token Issuance
- **File:** `apps/orchestrator/backend/Controllers/EnrollmentController.cs`
- **Lines:** `26-52`
- **Issue:** Any anonymous caller can create enrollment tokens and enroll nodes.
- **Recommendation:** Require admin authentication on `IssueToken`. Add rate limiting.

### 7. Security — Fake Agent Executable Generation
- **File:** `apps/orchestrator/backend/Controllers/AgentDownloadController.cs`
- **Lines:** `42-49`
- **Issue:** If `agent.exe` is missing, the endpoint synthesizes a 2-byte placeholder PE file and writes it to disk. An attacker with any valid token can trigger file creation in `ContentRootPath/data`.
- **Recommendation:** Pre-build and deploy a real agent binary. Return `503` if it is missing.

### 8. Security — SignalR Hub Accepts Any Node ID
- **File:** `apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs`
- **Lines:** `28-45`
- **Issue:** `Identify(Guid nodeId)` accepts a node ID from any connection without token or certificate validation. An attacker can impersonate any node.
- **Recommendation:** Validate a signed enrollment token or client certificate before registering the connection.

---

## High Issues

### 1. Hardcoded Listen URLs
- **Files:** `apps/orchestrator/backend/Program.cs:94`, `apps/agent/backend/Program.cs:60`, `apps/agent/backend/appsettings.json:9`
- **Issue:** Orchestrator binds to `http://0.0.0.0:5000`; agent binds to `http://localhost:5001`; fallback orchestrator URL is `http://localhost:5000`. These are baked into production entry points.
- **Recommendation:** Read from environment variables (`ASPNETCORE_URLS`) or configuration.

### 2. Hardcoded Orchestrator URL in Frontend
- **File:** `apps/orchestrator/web/src/pages/Nodes.tsx`
- **Line:** `20`
- **Issue:** Default orchestrator URL is `'https://orchestrator.local:5000'`.
- **Recommendation:** Source from a runtime config endpoint or build-time env var.

### 3. Placeholder SignalR Access Token
- **File:** `apps/agent/backend/Services/HubConnectionFactory.cs`
- **Line:** `12`
- **Issue:** Hardcoded token `"placeholder-token"`.
- **Recommendation:** Replace with a configurable secret from secure storage.

### 4. Missing Security Headers
- **File:** `apps/orchestrator/backend/Program.cs`
- **Lines:** `46-137`
- **Issue:** No HSTS, CSP, X-Frame-Options, X-Content-Type-Options, or Referrer-Policy.
- **Recommendation:** Add `app.UseHsts()` and a security-headers middleware.

### 5. Frontend Missing CSRF Protection
- **File:** `apps/orchestrator/web/src/services/api.ts`
- **Lines:** `353,406,426,437,473,550,589,654,663,691,721,740,849,885,934,985,997,1056,1124,1125`
- **Issue:** All mutating `fetch` calls lack CSRF tokens.
- **Recommendation:** Add `X-CSRF-Token` header or rely on `SameSite=Lax/Strict` cookies with proper CORS.

### 6. GET Endpoint Mutates Database State
- **File:** `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs`
- **Lines:** `432-497`
- **Issue:** `GetPending` (GET) updates `node.LastSeenUtc` and saves changes. This violates HTTP semantics and can cause side effects from retries.
- **Recommendation:** Move heartbeat updates to a dedicated POST/PATCH endpoint or background job.

### 7. Placeholder Agent Binary Generation
- *(Same as Critical #7, but also classified here due to deployment risk.)*

### 8. Vite Dev Proxy Hardcoded
- **File:** `apps/orchestrator/web/vite.config.ts`
- **Line:** `16`
- **Issue:** Hardcoded proxy target `http://localhost:5124`.
- **Recommendation:** Parameterize via environment variable.

### 9. SQLite Defaults to Potentially Read-Only Path
- **File:** `apps/orchestrator/backend/Program.cs`
- **Lines:** `52-58`
- **Issue:** Falls back to `ContentRootPath/data/deployment-poc.db`. In a single-file published app, this may be a read-only temp extraction directory.
- **Recommendation:** Default to a known writable location (e.g., `%LOCALAPPDATA%` or `/var/lib`).

### 10. Frontend Effect Dependency Bugs
- **File:** `apps/orchestrator/web/src/pages/WorkloadRuns.tsx`
- **Lines:** `63-75`, `81-103`
- **Issue:** `useEffect` dependency arrays include unstable references (`refresh` function, entire `runs` array), causing rapid re-fetch loops and unnecessary subscription churn.
- **Recommendation:** Stabilize callbacks with `useCallback` and depend only on stable IDs.

### 11. Enrollment Token Copied Without Warning
- **File:** `apps/orchestrator/web/src/pages/Nodes.tsx`
- **Lines:** `150-157`
- **Issue:** `navigator.clipboard.writeText` copies sensitive tokens silently.
- **Recommendation:** Warn the user before copying secrets.

### 12. Operational Data in Root Temp/Log Files
- **Files:** `temp_*.json`, `agent-local.log`, `agent-out.txt`, `orchestrator-out.txt`
- **Issue:** GUIDs, node IDs, run IDs, and localhost URLs are present in uncommitted but tracked files in the repo root.
- **Recommendation:** Delete all `temp_*.json` and `*.log` files, then add patterns to `.gitignore`.

---

## Medium Issues

### Backend
| # | File | Lines | Issue | Recommendation |
|---|------|-------|-------|----------------|
| 1 | `orchestrator/Controllers/ArtifactsController.cs` | `70,184` | `file.OpenReadStream()` probed with `IsZipByMagicBytes`; non-seekable streams may parse from wrong position. | Copy to seekable stream or temp file first. |
| 2 | `orchestrator/Services/ArtifactStoreService.cs` | `45-68` | `DeleteArtifactAsync` swallows all exceptions. | Log and return a result enum or throw. |
| 3 | `agent/Services/AgentEnrollmentService.cs` | `102-103` | `JsonSerializer.Deserialize` not wrapped in try/catch; corrupted config crashes agent. | Catch `JsonException` and allow re-enrollment. |
| 4 | `orchestrator/Controllers/NodesController.cs` | `217-222` | Parses raw SQLite error text for duplicate hostname detection. | Use `SqliteErrorCode` instead. |
| 5 | `orchestrator/Services/ArtifactZipService.cs` | `242-261` | `EnsureSeekable` copies entire stream to `MemoryStream`; 2 GB uploads can OOM. | Stream to a temp file instead. |
| 6 | `orchestrator/Controllers/WorkloadsController.cs` | `384-389` | `DeterministicGuid` uses `MD5.Create()`. | Replace with `SHA256` or a non-cryptographic hash. |
| 7 | `orchestrator/Controllers/ArtifactsController.cs` | `618-623` | Duplicate `DeterministicGuid` implementation. | Consolidate into a shared utility. |
| 8 | `orchestrator/Services/WorkloadImportService.cs` | `76-81` | Third copy of `DeterministicGuid`. | Consolidate. |
| 9 | `orchestrator/Services/ArtifactZipService.cs` | `220-230` | Synchronous `reader.ReadToEnd()` in async service. | Use `ReadToEndAsync`. |
| 10 | `orchestrator/Services/UploadSessionService.cs` | `73-86` | Synchronous `chunk.CopyTo(assembled)` in async context. | Use `CopyToAsync`. |
| 11 | `orchestrator/Program.cs` | `108-111` | Empty catch block during startup cleanup. | Log at `Warning` level. |
| 12 | `agent/Services/AgentRuntimeService.cs` | `245-248,277-280` | `SendStepStatusAsync` and `FinalizeAsync` swallow all exceptions silently. | Log failures. |
| 13 | `orchestrator/Services/ArtifactStoreService.cs` | `266-269,279-282` | Size/digest failures swallowed with empty catches. | Log exceptions. |
| 14 | `agent/Steps/PackageDetector.cs` | `188` | `Regex.Match` instantiated on every call. | Use a static compiled `Regex`. |
| 15 | `orchestrator/Controllers/WorkloadRunsController.cs` | `225` | `LOWER()` function in SQL query is redundant for SQLite. | Remove or use collation. |
| 16 | `orchestrator/Controllers/NodesController.cs` | `199` | `RunId = Guid.Empty` used as sentinel. | Use `Guid?`. |

### Frontend
| # | File | Lines | Issue | Recommendation |
|---|------|-------|-------|----------------|
| 17 | `src/pages/Dashboard.tsx` | `17-633` | Component is ~690 lines. | Split into sub-components. |
| 18 | `src/pages/Workloads.tsx` | `46-653` | Component is ~654 lines. | Decompose into smaller components. |
| 19 | `src/pages/WorkloadRuns.tsx` | `26-717` | Component is ~718 lines. | Extract modals, filters, and table. |
| 20 | `src/pages/ArtifactStore.tsx` | `46-795` | Component is ~796 lines. | Extract uploader, inventory, and detail modal. |
| 21 | `src/pages/Install.tsx` | `28-432` | Component is ~456 lines. | Extract dropzone and manifest editor. |
| 22 | `src/pages/Nodes.tsx` | `11-474` | Component is ~475 lines. | Extract table and modals. |
| 23 | `src/pages/Nodes.tsx` | `213-221` | Double-submit risk on Enter + blur. | Guard with strict `savingName` flag. |
| 24 | `src/pages/Nodes.tsx` | `336-446,449-472` | Raw HTML modals instead of shared `Modal` component. | Replace with shared components. |
| 25 | `src/services/api.ts` | `333-403,450-541` | `uploadArtifact` and `uploadArtifactWithProgress` duplicate logic. | Extract a shared helper. |

### Shared / Config / Tests / Scripts
| # | File | Lines | Issue | Recommendation |
|---|------|-------|-------|----------------|
| 26 | `shared/contracts/Runtime/PendingPackageDto.cs` | `13` | `TODO` comment indicates incomplete contract. | Resolve before release. |
| 27 | `tests/orchestrator/unit/InstallControllerTests.cs` | `24-80` | Failure cases assert `OkObjectResult` instead of error codes. | Assert appropriate error status codes. |
| 28 | `scripts/clean-demo.ps1` | `63-66,136` | Hardcoded relative paths. | Resolve dynamically. |
| 29 | `scripts/publish-spa-smoke.sh` | `6-9` | Hardcoded project paths. | Accept as arguments. |
| 30 | `scripts/download-test-artifacts.ps1` | `31-34` | Hardcoded external download URLs. | Move to config block or JSON manifest. |

---

## Low Issues (Selected)

### Unused / Dead Code
| File | Lines | Issue | Recommendation |
|------|-------|-------|----------------|
| `orchestrator/Store/AppStore.cs` | `1-6` | Entire class is `[Obsolete]` and empty. | Delete. |
| `orchestrator/Services/WorkloadImportService.cs` | `19-45` | `MapToPackageAssignments` never called. | Remove or wire up. |
| `orchestrator/Controllers/WorkloadsController.cs` | `651-657` | `PreUpgradeActions` deserialized but never read. | Remove or implement. |
| `agent/Services/AgentRuntimeService.cs` | `54-140` | Large commented-out SignalR block. | Delete. |
| `orchestrator/Services/WorkloadRunDispatcher.cs` | `104-105` | Commented-out SignalR push. | Remove. |
| `src/pages/Packages.tsx` | `1-72` | Entire page is dead code. | Delete or wire into router. |
| `src/pages/Install.tsx` | `1-456` | Entire page is dead code. | Delete or wire into router. |
| `src/pages/CommandCenter.tsx` | `1-56` | Entire page is dead code. | Delete or wire into router. |
| `src/components/Layout.tsx` | `1-41` | Duplicate/legacy layout; never imported. | Delete. |
| `src/services/api.ts` | `1240-1248` | `getDashboardSummary()` exported but never called. | Remove. |
| `src/services/api.ts` | `1250-1252` | `listAuditEvents(limit=8)` exported but never consumed. | Remove. |
| `src/types.ts` | `291-295,297-303,347-352` | `CreateNodeRequest`, `CreatePackageRequest`, `WorkloadJsonEntry` unused. | Remove. |
| `src/components/ui/table.tsx` | `92-103` | `TableCaption` exported but unused. | Remove or document. |
| `src/components/ui/modal.tsx` | `12-22` | `ModalTrigger`, `ModalClose`, `ModalPortal` exported but unused. | Remove. |
| `src/components/ui/sheet.tsx` | `12-22` | `SheetTrigger`, `SheetClose`, `SheetPortal` exported but unused. | Remove. |

### Hardcoded Values
| File | Lines | Issue | Recommendation |
|------|-------|-------|----------------|
| `src/services/api.ts` | `36` | Hardcoded mock epoch `new Date('2026-04-16T12:00:00.000Z')`. | Derive from build env or remove. |
| `src/services/api.ts` | `74,317,631` | `timeoutSeconds: 300` repeated. | Extract constant. |
| `src/services/api.ts` | `74,317,631` | `expectedExitCodes: [0]` repeated. | Extract constant. |
| `src/services/realtime.ts` | `32` | Magic polling interval `1200`. | Extract constant. |
| `src/pages/Dashboard.tsx` | `13` | `AUTO_REFRESH_MS = 15_000`. | Move to config. |
| `src/pages/Nodes.tsx` | `21` | `requestedBy` default `'ops.admin'`. | Default to empty or session value. |
| `src/pages/Nodes.tsx` | `22` | `ttlMinutes` default `20`. | Extract constant. |
| `src/pages/Nodes.tsx` | `45-47` | `setInterval(..., 5_000)`. | Extract constant. |
| `src/pages/Nodes.tsx` | `147` | CLI command hardcodes `DeploymentPoC.Agent.exe`. | Extract constant. |
| `src/pages/Workloads.tsx` | `71` | Default revision `'1.0.0'`. | Use semantic placeholder constant. |
| `src/pages/WorkloadRuns.tsx` | `15-22,24` | Hardcoded filter status lists and run modes. | Derive from type unions. |
| `src/pages/ArtifactStore.tsx` | `14-19,407,462,493,541` | Repeated inline gradient CSS. | Create reusable component/class. |
| `src/pages/ArtifactStore.tsx` | `290` | Hardcoded file extensions `.msi,.exe,.zip,.tar.gz`. | Extract constant. |
| `src/lib/zip-preview.ts` | `66-68` | `.tar.gz` listed but only `.zip` is supported by `fflate`. | Remove unsupported extension or add parser. |
| `orchestrator/Controllers/EnrollmentController.cs` | `28,33,89,97` | TTL bounds `1`/`120`, prefix `"enroll-"`, fallback `"auto-node-"` / `"0.0.0.0"`. | Move to configuration. |
| `orchestrator/Services/ArtifactStoreService.cs` | `392,402,410` | Default names `"artifact.bin"`, `"resolved-manifest.json"`. | Define `const string`. |
| `orchestrator/Services/WorkloadRunDispatcher.cs` | `141,153-156,185,192` | Default install args, timeout `300`, channel `"stable"`. | Move to config or constants class. |
| `orchestrator/Controllers/ArtifactsController.cs` | `34,249,414` | Request size limit `2L * 1024 * 1024 * 1024` repeated. | Move to `appsettings.json`. |
| `agent/Steps/PackageDetector.cs` | `119-128` | Hardcoded Windows search paths. | Move to platform config. |
| `agent/Pipeline/PipelineExecutor.cs` | `146,158` | Temp dir `"agent-artifacts"`, chunk size `8*1024*1024`. | Move to config. |
| `agent/Services/AgentEnrollmentService.cs` | `73,76` | Hardcoded config paths for Windows/Linux. | Accept via env var with fallback. |
| `shared/web-common/layoutShell.ts` | `17` | Hardcoded app title. | Move to config. |

### Minor Quality / Performance
| File | Lines | Issue | Recommendation |
|------|-------|-------|----------------|
| `src/pages/Dashboard.tsx` | `261-265,343-347` | Inline arrow functions on table rows prevent memoization. | Extract memoized row components. |
| `src/pages/ArtifactStore.tsx` | `567-596` | Array index used as React key in `bulkResults.map`. | Use stable unique key. |
| `src/pages/CommandCenter.tsx` | `31` | Array index used as React key. | Use `metric.label`. |
| `src/services/api.ts` | `1054` | Weak UUID fallback `Date.now() + Math.random()`. | Require `crypto.randomUUID` or robust polyfill. |
| `src/pages/Nodes.tsx` | `40-52` | Missing cleanup for in-flight `refresh()` on unmount. | Use `AbortController` or mounted guard. |
| `src/pages/Install.tsx` | `41-45` | Missing cleanup for `listArtifacts` promise. | Add mounted guard. |
| `src/pages/Packages.tsx` | `26` | Missing cleanup for `fetchPackages` promise. | Add mounted guard. |
| `src/pages/ArtifactStore.tsx` | `72-84` | `fetchArtifacts` uses `.catch(() => {})`. | Surface or log errors. |
| `tests/orchestrator/unit/PipelineTests.cs` | `103` | Vacuous assertion `Assert.That(true, Is.True)`. | Verify actual logging behavior. |
| `tests/orchestrator/unit/ArtifactsControllerGetAllTests.cs` | `28` | Hardcoded Unix path `/tmp/...`. | Use `Path.GetTempPath()`. |
| `tests/orchestrator/unit/WorkloadRunsControllerCurrentPackagesTests.cs` | `96-547` | Heavy duplication of seeding boilerplate. | Extract shared seeding helpers. |
| `package.json` (root) | `1-5` | Single dependency `ecc-universal`; verify if used. | Remove if orphaned. |

---

## Immediate Action Items (Prioritized)

1. **Add authentication and authorization** to the backend and frontend before any production exposure.
2. **Fix CORS** — remove `AllowAnyOrigin()` fallback and restrict `AllowedHosts`.
3. **Sanitize agent execution** — validate/sandbox all `Process.Start` inputs; sign manifests.
4. **Fix `UpdateStatus`** to include `NodeId` in the lookup.
5. **Purge operational residue** — delete all `temp_*.json` and `*.log` files from the repo root and update `.gitignore`.
6. **Add CSRF protection** to frontend mutating requests.
7. **Fix `useEffect` dependency bugs** in `WorkloadRuns.tsx` to stop subscription churn.
8. **Delete dead pages/components** (`Packages.tsx`, `Install.tsx`, `CommandCenter.tsx`, `Layout.tsx`) or wire them into the router.
9. **Consolidate duplicated `DeterministicGuid`** implementations into a shared utility.
10. **Move hardcoded URLs, timeouts, and thresholds** into `appsettings.json`/environment variables.

---

*No code was modified during this review.*
