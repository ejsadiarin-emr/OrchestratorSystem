# Phase 1 Implementation Plan Review Report

## GAPS — MVP Requirements Not Covered by Any Ticket

### GAP-1: `AgentNodeStatus` Missing `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE`
**Severity: HIGH** | P1-002

MVP Plan Section 12 defines the `AgentNode` state machine as:
```
UNREGISTERED → REGISTERED → WORKLOAD_ASSIGNED → NEEDS_UPDATE
```

The implementation plan's enum (line 259) only lists:
```csharp
AgentNodeStatus: REGISTERED, UNREGISTERED, LOST
```

Missing: `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE`. These states are fundamental to Phase 2 (task dispatch — an agent cannot receive workload runs if it can't be marked `WORKLOAD_ASSIGNED`) and Phase 3 (update mode — `NEEDS_UPDATE` triggers the update flow). Without these, the state machine is broken from the start.

Section 7 lists only 3 statuses, but Section 12 contradicts this with 5. **Resolve in favor of Section 12.**

### GAP-2: No EF Core FK Configuration for String Business Keys
**Severity: HIGH** | P1-002

`OnModelCreating` (lines 396–437) configures composite keys and indexes but **never configures foreign key relationships**. The following navigations will silently create FK columns pointing to the integer `Id` PKs instead of the string business keys:

| Entity | Navigation | Should FK to | Would default to |
|---|---|---|---|
| `WorkloadPackage.Workload` | `Workload.WorkloadId` (string) | `Workload.Id` (int) |
| `AgentPackage.Agent` | `AgentNode.AgentId` (string) | `AgentNode.Id` (int) |
| `EnrollmentToken.UsedByAgentId` | `AgentNode.AgentId` (string) | `AgentNode.Id` (int) |
| `WorkloadRun.AgentId` | `AgentNode.AgentId` (string) | `AgentNode.Id` (int) |

Explicit `.HasOne().WithMany().HasForeignKey(...)` calls are needed for all string-based FK relationships, or the migration will create wrong column types and shadow FK properties.

### GAP-3: `AgentNode.AgentId` and `AgentNode.AgentSecret` Missing Unique Constraints
**Severity: HIGH** | P1-002

`AgentId` is used for auth (bearer token lookup) and agent identification. `OnModelCreating` creates an index but **not a unique index**:
```csharp
entity.HasIndex(a => a.AgentId);    // should be .IsUnique()
```
Without `.IsUnique()`, duplicate `AgentId` values could be inserted, breaking auth lookups. Same issue for `AgentSecret` — it needs a unique index for O(1) bearer token validation during every API call.

### GAP-4: No `WorkloadDefinitionStore` Configuration Section
**Severity: MEDIUM** | P1-001, P1-003, P1-011

P1-011 stores workload definition JSON to `{BasePath}/workload-definitions/{workloadId}/{version}.json`, reusing `ArtifactStore.BasePath`. This co-mingles artifact binaries and workload JSON under the same root. The MVP Plan Section 2 directory structure shows them as separate: `dist/artifacts/` and `dist/workload-definitions/`. A dedicated configuration section (e.g., `WorkloadDefinitionStore.BasePath`) is needed.

### GAP-5: Workload Package JSON Field Mapping Mismatch
**Severity: HIGH** | P1-011

MVP Plan Section 6.2 shows workload packages with a `"version"` field:
```json
{ "packageId": "ssms-2019", "version": "15.0.18390.0", "preInitSteps": [...], "postInitSteps": [...] }
```

P1-011's `WorkloadPackageManifest` model uses `PackageVersion`:
```csharp
[Required] public string PackageVersion { get; set; } = string.Empty;
```

System.Text.Json will serialize/deserialize this as `packageVersion`, not `version`. Without `[JsonPropertyName("version")]`, deserialization of incoming workload JSON will silently fail to populate this field, resulting in empty strings in the database. Same issue applies to `WorkloadId` vs `workloadId` if camelCase is not globally configured.

---

## BUGS — Technical Issues in Code Examples

### BUG-1: Agent.csproj Includes Unnecessary `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
**Severity: MEDIUM** | P1-001 (line 130)

The Agent is a .NET Worker Service that polls via `HttpClient`. It is **not** an ASP.NET application. Including `Microsoft.AspNetCore.App` bloats the self-contained single-file executable by ~30–50MB unnecessarily. The `IHttpClientFactory` is available via `Microsoft.Extensions.Http`, which is already included in the Worker SDK.

**Fix:** Remove the `<FrameworkReference>` line. If `AddHttpClient()` is needed, add `<PackageReference Include="Microsoft.Extensions.Http" Version="8.0" />` explicitly.

### BUG-2: EF Core Packages in Agent.csproj
**Severity: LOW** | P1-001 (lines 128–129)

The Agent project includes `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design`. The Agent does **not** use a database — it reads `agent.json` and makes HTTP calls. These packages add unnecessary bloat to the self-contained executable.

**Fix:** Remove `EntityFrameworkCore` packages from Agent.csproj.

### BUG-3: P/Invoke `StartService` Missing `SetLastError = true`
**Severity: HIGH** | P1-007 (line 1008)

```csharp
[DllImport(Advapi32)]
public static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);
```

Missing `SetLastError = true`. Without it, `Marshal.GetLastWin32Error()` returns 0 even on failure, making error diagnosis impossible. This applies to ALL `advapi32.dll` P/Invoke declarations in the file.

**Fix:** Add `SetLastError = true` to all `[DllImport]` declarations for `advapi32.dll`.

### BUG-4: Missing P/Invoke Declarations for `--reset` Flow
**Severity: HIGH** | P1-008

The reset flow requires `OpenService`, `ControlService` (to send `SERVICE_CONTROL_STOP`), `QueryServiceStatus`, and `DeleteService`. These are not present in the `ScmNativeMethods` class from P1-007. Without them, the SCM operations in P1-008 cannot be implemented.

**Fix:** Add P/Invoke declarations for:
- `OpenService` (with `CharSet = CharSet.Unicode`)
- `ControlService`
- `QueryServiceStatus`
- `DeleteService`

### BUG-5: `ZipFile.ExtractToDirectory` with `IFormFile.OpenReadStream()`
**Severity: LOW** | P1-010 (line 1330)

While .NET 6+ added a `Stream` overload for `ZipFile.ExtractToDirectory`, `IFormFile.OpenReadStream()` may return a non-seekable stream depending on the hosting configuration. Using `ZipArchive` explicitly on the stream is more robust:

```csharp
using var archive = new ZipArchive(archive.OpenReadStream(), ZipArchiveMode.Read);
archive.ExtractToDirectory(tempDir);
```

### BUG-6: `Web Options` Class Missing from P1-003
**Severity: LOW** | P1-003

P1-003 tasks mention `WebHostOptions (Port)` but the code examples only show `AgentOptions`, `ArtifactOptions`, and `EnrollmentOptions`. The `WebHostOptions` class is never defined, and `Program.cs` in P1-001 hardcodes `UseUrls("http://0.0.0.0:5000")` instead of reading from configuration.

**Fix:** Define `WebHostOptions`, register it, and use it in `Program.cs` to configure Kestrel's listen port from `appsettings.json`.

---

## INCONSISTENCIES — Contradictions Between Tickets or MVP Plan

### INC-1: `AgentNodeStatus` — Section 7 vs Section 12
**Severity: HIGH** | P1-002

MVP Plan Section 7 lists 3 statuses: `REGISTERED | UNREGISTERED | LOST`. Section 12's state machine shows 5: `UNREGISTERED → REGISTERED → WORKLOAD_ASSIGNED → NEEDS_UPDATE` plus transitions to/from `LOST`.

P1-002 follows Section 7 (3 values). The correct set is the Section 12 superset. See GAP-1.

### INC-2: `WorkloadRunStepAction` — SKIP as Action vs Status
**Severity: MEDIUM** | P1-002

MVP Plan Section 7 defines the `action` field with values: `DETECT | PRE_INIT_STEP | INSTALL | POST_INIT_STEP | SKIP | UPDATE | UNINSTALL | VERIFY`. The state machine in Section 12 defines `SKIPPED` as a `WorkloadRunStatus` (not a step action).

P1-002 correctly includes `SKIP` in `WorkloadRunStepAction` matching Section 7, and separately defines `SKIPPED` in `WorkloadRunStepStatus`. This is internally consistent with Section 7 but creates semantic overlap: a step with `Action=SKIP` and `Status=SKIPPED` is redundant. Consider whether `Action=SKIP` is needed or if `Action=INSTALL` with `Status=SKIPPED` suffices.

### INC-3: Artifact Store Path — Development vs Production
**Severity: LOW** | P1-001, P1-003

`appsettings.json` in P1-001 shows `"BasePath": "./artifacts"` (relative). MVP Plan Section 3 shows `"BasePath": "C:\\OrchestratorData\\Artifacts"` (absolute Windows path). Minor — develop uses relative, deploy uses absolute — but worth documenting.

### INC-4: `ArtifactOptions` Class Name Doesn't Match Config Section
**Severity: LOW** | P1-003

Config section is `"ArtifactStore"` but the options class is `ArtifactOptions`. Convention would expect `ArtifactStoreOptions`. DI registration: `services.Configure<ArtifactOptions>(configuration.GetSection("ArtifactStore"))` — the name mismatch won't break functionality (property binding is by name), but is confusing.

---

## MISSING DETAILS — Items Mentioned but Not Fully Specified

### MISS-1: Workload Upsert Logic — No Code Example
**Severity: MEDIUM** | P1-011

P1-011 states "upsert on conflict (update changed fields, leave unchanged fields as-is)" but provides no code example for the upsert operation. The EF Core pattern requires either:
- `Update()` with selective field modification
- `AddOrUpdate()` pattern
- Raw SQL `INSERT OR REPLACE`

The accept criteria says "Upsert: same `workloadId + version` → updates changed fields, preserves unchanged fields" but there's no implementation showing which fields get updated vs. preserved. This is especially important for `WorkloadPackages` — does upserting a workload delete and recreate all its packages, or merge them?

### MISS-2: `ScmService` Implementation Not Shown
**Severity: MEDIUM** | P1-007, P1-008

`ScmService` is referenced throughout P1-007 and P1-008 but never implemented. Only the raw P/Invoke declarations are shown. The class needs methods like `RegisterService()`, `StartService()`, `StopService()`, `DeleteService()`, each wrapping multiple API calls (open SCM, open service, call operation, close handles) with proper error handling and `GetLastError` reporting.

### MISS-3: Agent CLI Entry Point Architecture
**Severity: MEDIUM** | P1-007, P1-008

Both tickets show inline code in `Program.cs` with `if (enrollMode)` / `if (resetMode)` blocks. No architecture is specified for:
- How `--enroll` and `--reset` modes interact with the `UseWindowsService()` host builder
- What happens when `Agent.exe` runs without flags (normal polling mode)
- How the Agent exits after CLI operations (it should `return`, not start the host)

The `Program.cs` in P1-001 sets up `UseWindowsService()` but the CLI modes need different entry paths.

### MISS-4: No `PackageManifest` Model Defined
**Severity: MEDIUM** | P1-009

`ArtifactService.UploadAsync` deserializes `PackageManifest` (line 1232) but this model is never defined in P1-002 or anywhere. It needs fields matching Section 6.1: `packageId`, `packageName`, `version`, `installCommand`, `installArgs`, `uninstallCommand`, `uninstallArgs`, `updateStrategy`, `detection` (with sub-fields).

### MISS-5: No Auth Middleware for Agent Endpoints
**Severity: HIGH** | P1-006

P1-006 implements `POST /api/agents/{agentId}/unregister` with `Authorization: Bearer <agentSecret>`, but no middleware or filter is defined for validating `agentSecret` across all agent endpoints. The MVP Plan Section 8 states: "Every request after enrollment is authenticated with the `agentSecret` as a bearer token." This requires:
- An auth middleware or action filter
- A lookup of `agentSecret` against `AgentNode.AgentSecret` in the DB
- Applying this to all `/api/agents/{agentId}/*` endpoints

This is a cross-cutting concern that should be in P1-003 (startup wiring) or P1-006, but appears in neither.

### MISS-6: No Seed Data or Migration Application Strategy
**Severity: LOW** | P1-002, P1-003

P1-003 mentions `IDataMigrationService` that applies pending migrations, and P1-002 generates the initial migration. But no strategy is specified for:
- Whether to use `EnsureCreated()` (no migrations history) or `Migrate()` (with migrations table)
- Whether to seed any initial data
- How subsequent schema changes will be handled (additive migrations)

---

## READINESS ASSESSMENT

### Is Phase 1 Ready for Implementation?

**No — 3 blocking issues must be fixed first.**

### Blocking Issues (Must Fix Before Implementation)

| # | Issue | Impact | Ticket |
|---|---|---|---|
| GAP-1 | `AgentNodeStatus` missing `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE` | State machine broken; Phase 2 cannot assign workloads to agents | P1-002 |
| GAP-2 | Missing EF Core FK configuration for string business keys | Wrong schema — FKs will point to integer `Id` columns instead of string business keys, data integrity lost | P1-002 |
| MISS-5 | No auth middleware for `agentSecret` bearer validation | All agent endpoints would be unauthenticated; security gap | P1-003/P1-006 |

### High-Priority Fixes (Should Fix Before Implementation)

| # | Issue | Impact | Ticket |
|---|---|---|---|
| BUG-3 | P/Invoke missing `SetLastError = true` | SCM error diagnostics impossible | P1-007 |
| BUG-4 | Missing P/Invoke declarations for reset flow | P1-008 cannot be implemented | P1-008 |
| BUG-1 | Agent.csproj includes unnecessary ASP.NET FrameworkReference | ~30-50MB bloat in self-contained Agent executable | P1-001 |
| GAP-5 | JSON property name mismatch `version` vs `packageVersion` | Workload uploads will silently fail to populate version field | P1-011 |
| GAP-3 | `AgentId` and `AgentSecret` missing unique constraints | Duplicate agent IDs could break auth; O(n) lookups on bearer validation | P1-002 |

### Recommended Before Implementation

| # | Issue | Ticket |
|---|---|---|
| MISS-1 | Define workload upsert logic (field-level merge vs. replace) | P1-011 |
| MISS-2 | Implement `ScmService` class with proper handle lifecycle | P1-007/P1-008 |
| MISS-3 | Define Agent CLI entry point architecture | P1-007/P1-008 |
| MISS-4 | Define `PackageManifest` model with all Section 6.1 fields | P1-009 |
| BUG-2 | Remove unnecessary EF Core packages from Agent.csproj | P1-001 |
| BUG-6 | Add `WebHostOptions` class and wire port from config | P1-003 |
| GAP-4 | Add `WorkloadDefinitionStore` config section | P1-003/P1-011 |

### Acceptable to Defer

| # | Issue | Reason |
|---|---|---|
| INC-2 | `Action=SKIP` semantic overlap with `Status=SKIPPED` | Phase 2/3 can refine |
| INC-3 | Artifact store path (relative vs absolute) | Config-driven, easy to change |
| BUG-5 | `ZipFile.ExtractToDirectory` stream robustness | Works in .NET 10, minor risk |
| MISS-6 | Migration strategy details | Can be decided during P1-003 implementation |