# Implementation Plan — Phase 1: Foundation

> **MVP Plan Ref:** Section 15, Phase 1  
> **Depends on:** None (this is the starting phase)

## Dependency Graph

```
P1-001 ──┬── P1-002 ── P1-003 ──┬── P1-005 ── P1-006 ──┬── P1-007
          │                       │                        └── P1-008
          └── P1-004 ────────────┤
                                  ├── P1-009 ── P1-010
                                  └── P1-011
P1-012 (independent — can run in parallel with P1-005+)
```

---

## TICKET P1-001: Solution Scaffolding & Project Setup

**MVP Plan Ref:** Section 2 (Project Structure & Deployment)  
**Depends on:** None

### Description

Create the .NET solution with both projects (Orchestrator and Agent) and the React frontend shell. Set up the repo directory structure, project files, and build/publish pipeline. Ensure both .NET projects compile and the React dev server starts.

### Tasks

- [ ] Create .NET solution file at repo root: `DeploymentPoC.sln`
- [ ] Create `orchestrator/backend/Orchestrator.csproj` — ASP.NET Core Web API targeting `net8.0-windows`
- [ ] Create `agent/backend/Agent.csproj` — .NET Worker Service with `UseWindowsService()` targeting `net8.0-windows`
- [ ] Add NuGet references to Orchestrator project:
  - `Microsoft.EntityFrameworkCore.Sqlite`
  - `Microsoft.EntityFrameworkCore.Design`
  - `Microsoft.EntityFrameworkCore.Tools` (for migrations)
- [ ] Add NuGet references to Agent project:
  - `Microsoft.Extensions.Http` (for `IHttpClientFactory`)
  - `System.CommandLine` (for CLI argument parsing)
  - **Note:** Agent does NOT need EF Core or ASP.NET references
- [ ] Create `orchestrator/backend/Program.cs` with minimal Kestrel setup (port 5000, HTTP only)
- [ ] Create `agent/backend/Program.cs` with `UseWindowsService()` host builder
- [ ] Create `.gitignore` entries for `dist/`, `.artifact-cache/`, `bin/`, `obj/`, `wwwroot/` (built assets)
- [ ] Create build script (`scripts/build.ps1` and `scripts/build.sh`) that:
  1. Runs `npm install` and `npm run build` in `orchestrator/web/`
  2. Copies React build output to `orchestrator/backend/wwwroot/`
  3. Runs `dotnet publish` for Orchestrator (self-contained, win-x64, single-file)
  4. Runs `dotnet publish` for Agent (self-contained, win-x64, single-file)
  5. Copies both `.exe` files to `dist/`
  6. Copies `appsettings.json` to `dist/`
  7. Creates `dist/artifacts/` and `dist/workload-definitions/` directories
- [ ] Add `Directory.Build.props` for consistent versioning across projects

### Code Example — Orchestrator Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel((ctx, kestrel) =>
{
    var hostOpts = ctx.Configuration.GetSection("WebHost").Get<WebHostOptions>()!;
    kestrel.Listen(System.Net.IPAddress.Parse(hostOpts.BindAddress), hostOpts.Port);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.MapControllers();

app.Run();
```

### Code Example — Agent Program.cs (Entry Point Architecture)

```csharp
// agent/backend/Program.cs
using Agent.Services;
using System.CommandLine;

var rootCommand = new RootCommand("Orchestrator Agent");

var enrollOption = new Option<string>("--enroll", "Enrollment token from Orchestrator");
var urlOption = new Option<string>("--url", "Orchestrator URL (e.g., http://host:5000)");
var resetOption = new Option<bool>("--reset", "Unregister agent, remove config, and uninstall service");

rootCommand.AddOption(enrollOption);
rootCommand.AddOption(urlOption);
rootCommand.AddOption(resetOption);

rootCommand.SetHandler(async (string? enrollToken, string? url, bool reset) =>
{
    if (reset)
    {
        // --reset mode: unregister with Orchestrator, stop/delete service, delete agent.json, exit
        var resetService = new AgentResetService();
        return await resetService.ExecuteResetAsync();
    }

    if (!string.IsNullOrEmpty(enrollToken))
    {
        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Error: --url is required when using --enroll");
            return 1;
        }
        // --enroll mode: call enroll API, write agent.json, register/start service, exit
        var enrollService = new AgentEnrollService();
        return await enrollService.ExecuteEnrollAsync(enrollToken, url);
    }

    // Default mode: run as Windows Service
    return 0; // fall through to host builder below
}, enrollOption, urlOption, resetOption);

var commandResult = await rootCommand.InvokeAsync(args);

if (commandResult != 0 || args.Contains("--enroll") || args.Contains("--reset"))
{
    return commandResult;
}

// Default: run as Windows Service
var builder = Host.CreateDefaultBuilder(args);
builder.UseWindowsService();
builder.ConfigureServices((hostContext, services) =>
{
    services.AddHostedService<AgentPollingService>();
    services.AddHttpClient();
});

var host = builder.Build();
await host.RunAsync();
```

### Code Example — Orchestrator.csproj (key sections)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.0" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5" />
  </ItemGroup>
</Project>
```

### Code Example — Agent.csproj (key sections)

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
  </ItemGroup>
</Project>
```

> **Note:** Agent does not use EF Core or ASP.NET — those packages belong only in Orchestrator. The Agent only needs `Microsoft.Extensions.Http` for `IHttpClientFactory` and the Worker SDK (already referenced via the project SDK).

### Code Example — appsettings.json (Orchestrator)

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=./orchestrator.db"
  },
  "Agent": {
    "DefaultPollingIntervalSeconds": 30,
    "LostThresholdMultiplier": 3
  },
  "ArtifactStore": {
    "BasePath": "./artifacts"
  },
  "Enrollment": {
    "TokenTtlHours": 24
  },
  "WebHost": {
    "Port": 5000,
    "BindAddress": "0.0.0.0"
  },
  "WorkloadDefinitionStore": {
    "Path": "dist/workload-definitions/",
    "WatchForChanges": true
  }
}
```

### Code Example — Repo Directory Structure

```
DeploymentPoC-revamp/
├── DeploymentPoC.sln
├── orchestrator/
│   ├── backend/
│   │   ├── Orchestrator.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Controllers/
│   │   ├── Services/
│   │   ├── Models/
│   │   ├── Data/
│   │   └── wwwroot/          (gitignored, built from frontend)
│   └── web/
│       ├── package.json
│       ├── vite.config.ts
│       ├── tsconfig.json
│       ├── tailwind.config.js
│       ├── postcss.config.js
│       ├── components.json     (shadcn/ui config)
│       └── src/
│           ├── App.tsx
│           ├── main.tsx
│           ├── routes.tsx
│           ├── lib/
│           │   ├── api.ts      (TanStack Query + fetch client)
│           │   └── utils.ts
│           ├── components/     (shadcn/ui components)
│           ├── pages/
│           └── types/
├── agent/
│   └── backend/
│       ├── Agent.csproj
│       ├── Program.cs
│       ├── Services/
│       ├── Configuration/
│       └── PInvoke/
├── dist/                        (gitignored, deployment directory)
│   ├── Orchestrator.exe
│   ├── Agent.exe
│   ├── appsettings.json
│   ├── orchestrator.db          (created at runtime)
│   ├── artifacts/               (artifact store)
│   └── workload-definitions/    (workload definition JSONs on disk)
├── .artifact-cache/              (gitignored, downloaded installer media)
├── scripts/
│   ├── build.ps1
│   ├── build.sh
│   ├── download-artifacts.ps1
│   └── download-artifacts.sh
├── MVP_Plan_PackageOrchestration.md
└── impl-plan-phase*.md
```

### Acceptance Criteria

- [ ] `dotnet build` succeeds for both Orchestrator and Agent projects
- [ ] `dotnet run` for Orchestrator starts Kestrel on port 5000 and responds to `GET /`
- [ ] `dotnet run` for Agent starts without error (exits immediately since no `--enroll` or `--reset` flag; falls through to `UseWindowsService()` host which runs briefly then exits on non-service host)
- [ ] React dev server starts (`npm run dev`) and shows a placeholder page in the browser
- [ ] Build script successfully produces `dist/Orchestrator.exe` and `dist/Agent.exe`
- [ ] `.gitignore` properly excludes `dist/`, `.artifact-cache/`, `bin/`, `obj/`, `wwwroot/` (rebuilt)
- [ ] Kestrel configured for HTTP on port 5000 (no HTTPS for MVP)

### Verification Steps

1. Clone repo, run `dotnet build` at solution root — both projects compile with zero errors
2. Run `dotnet run --project orchestrator/backend` — server starts on port 5000, `curl http://localhost:5000/api/health` returns 200 (add a minimal health endpoint)
3. Run `cd orchestrator/web && npm run dev` — React app loads in browser at `http://localhost:5173`
4. Run `scripts/build.ps1` (or `.sh`) — both `.exe` files appear in `dist/`
5. Verify `dist/` contains expected directories (`artifacts/`, `workload-definitions/`)

---

## TICKET P1-002: Database Schema & EF Core Models

**MVP Plan Ref:** Section 7 (Database Schema)  
**Depends on:** P1-001

### Description

Create all EF Core entity models and the `AppDbContext`. Define all tables, relationships, and constraints from the MVP Plan. Generate and apply the initial migration.

### Tasks

- [ ] Create entity models for all 7 tables:
  - `EnrollmentToken` (id, token, createdAt, expiresAt, used, usedAt, usedByAgentId)
  - `Artifact` (id, packageId, packageName, version, installerFile, manifestPath, binaryPath, uploadedAt)
  - `Workload` (id, workloadId, workloadName, version, definitionPath, uploadedAt)
  - `WorkloadPackage` (workloadId, workloadVersion, packageId, packageVersion, preInitSteps, postInitSteps, downloadUrl, hash, updateStrategy) — composite key `(WorkloadId, WorkloadVersion, PackageId)`
  - `AgentNode` (id, agentId, hostname, ipAddress, agentSecret, lastSeenAt, registeredAt, status, assignedWorkloadId, assignedWorkloadVersion, pollingIntervalSeconds)
  - `AgentPackage` (agentId, packageId, installedVersion, detectedAt, status) — composite key
  - `WorkloadRun` (id, agentId, workloadId, workloadVersion, mode, status, createdAt, startedAt, completedAt)
  - `WorkloadRunStep` (id, runId, packageId, packageVersion, stepOrder, action, status, message, exitCode, startedAt, completedAt)
- [ ] Create `AppDbContext` with `DbSet<>` for each entity and `OnModelCreating` for:
  - Composite keys for `WorkloadPackage` (WorkloadId + WorkloadVersion + PackageId) and `AgentPackage`
  - Explicit FK configuration using `.HasOne().WithMany().HasForeignKey()` for string business keys:
    - `WorkloadPackage → Workload` on `(WorkloadId, WorkloadVersion)` matching `Workload.WorkloadId + Version`
    - `AgentNode → Workload` on `AssignedWorkloadId` referencing `Workload.WorkloadId`
    - `WorkloadRun → Workload` on `(WorkloadId, WorkloadVersion)` matching `Workload.WorkloadId + Version`
    - `WorkloadRunStep → WorkloadRun` on `RunId`
    - `WorkloadPackage → WorkloadPackage` (self-referencing lookup) on `PackageId` (optional)
  - Enum → string conversions for `AgentNodeStatus`, `WorkloadRunMode`, `WorkloadRunStatus`, `WorkloadRunStepAction`, `WorkloadRunStepStatus`, `AgentPackageStatus`
  - JSON column type for `preInitSteps` and `postInitSteps` (stored as TEXT in SQLite)
- [ ] Create unique indexes on `AgentNode.AgentId` and `AgentNode.AgentSecret`
- [ ] Create indexes on: `AgentNode.Status`, `WorkloadRun.AgentId`, `WorkloadRun.Status`, `Artifact.PackageId+Version` (unique composite), `Workload.WorkloadId+Version` (unique composite), `EnrollmentToken.Token`
- [ ] Generate initial EF Core migration: `dotnet ef migrations add InitialCreate`
- [ ] Add `EnsureCreatedAsync()` call in Program.cs startup for development simplicity
- [ ] No seed data required for MVP — workloads are uploaded via API, agents register via enrollment
- [ ] Create enum types matching the state machines from Section 12:
  - `AgentNodeStatus`: UNREGISTERED, REGISTERED, LOST, WORKLOAD_ASSIGNED, NEEDS_UPDATE
  - `WorkloadRunMode`: PRE_CHECK, INSTALL, UPDATE, UNINSTALL
  - `WorkloadRunStatus`: PENDING, RUNNING, SUCCESS, FAILED, SKIPPED, AWAITING_CONFIRMATION
  - `WorkloadRunStepAction`: DETECT, PRE_INIT_STEP, INSTALL, POST_INIT_STEP, SKIP, UPDATE, UNINSTALL, VERIFY
  - `WorkloadRunStepStatus`: PENDING, RUNNING, SUCCESS, FAILED, SKIPPED, PARTIAL_SUCCESS
  - `AgentPackageStatus`: INSTALLED, MISSING, UNKNOWN

> **State Machine — AgentNodeStatus:** UNREGISTERED → REGISTERED → WORKLOAD_ASSIGNED → NEEDS_UPDATE; REGISTERED/WORKLOAD_ASSIGNED → LOST; LOST → REGISTERED | WORKLOAD_ASSIGNED (re-enrollment or reconnection restores state). This resolves the Section 7 vs Section 12 contradiction (INC-1)

> **Terminology — SKIP vs SKIPPED (INC-2):** `SKIP` is an ACTION sent from Orchestrator → Agent in the task payload (tells the agent to skip this package). `SKIPPED` is a RUN STATUS for a `WorkloadRun` (indicates the entire run was skipped because all packages were already installed). These are different concepts at different levels and are not redundant.

### Code Example — Entity Models

```csharp
// Models/EnrollmentToken.cs
public class EnrollmentToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? UsedByAgentId { get; set; }
}

// Models/AgentNode.cs
public class AgentNode
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string AgentSecret { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public AgentNodeStatus Status { get; set; } = AgentNodeStatus.REGISTERED;
    public string? AssignedWorkloadId { get; set; }
    public string? AssignedWorkloadVersion { get; set; }
    public int PollingIntervalSeconds { get; set; } = 30;

    // Navigation
    public ICollection<AgentPackage> InstalledPackages { get; set; } = [];
    public ICollection<WorkloadRun> Runs { get; set; } = [];
}

// Models/Artifact.cs
public class Artifact
{
    public int Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallerFile { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string BinaryPath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

// Models/Workload.cs
public class Workload
{
    public int Id { get; set; }
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DefinitionPath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<WorkloadPackage> Packages { get; set; } = [];
}

// Models/WorkloadPackage.cs
public class WorkloadPackage
{
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadVersion { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string? PreInitSteps { get; set; }   // JSON array stored as TEXT
    public string? PostInitSteps { get; set; }    // JSON array stored as TEXT
    public string? DownloadUrl { get; set; }
    public string? Hash { get; set; }
    public string UpdateStrategy { get; set; } = "reinstall";

    // Navigation
    public Workload Workload { get; set; } = null!;
    public WorkloadPackage? Package { get; set; } = null!;   // FK to Artifact (optional, for download resolution)
}

// Models/WorkloadRun.cs
public class WorkloadRun
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadVersion { get; set; } = string.Empty;
    public WorkloadRunMode Mode { get; set; }
    public WorkloadRunStatus Status { get; set; } = WorkloadRunStatus.PENDING;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public ICollection<WorkloadRunStep> Steps { get; set; } = [];
}

// Models/WorkloadRunStep.cs
public class WorkloadRunStep
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public WorkloadRunStepAction Action { get; set; }
    public WorkloadRunStepStatus Status { get; set; }
    public string? Message { get; set; }
    public int? ExitCode { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public WorkloadRun Run { get; set; } = null!;
}

// Models/AgentPackage.cs
public class AgentPackage
{
    public string AgentId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public AgentPackageStatus Status { get; set; }

    // Navigation
    public AgentNode Agent { get; set; } = null!;
}
```

### Code Example — DbContext Configuration

```csharp
// Data/AppDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<WorkloadPackage>(entity =>
    {
        entity.HasKey(wp => new { wp.WorkloadId, wp.WorkloadVersion, wp.PackageId });
        entity.Property(wp => wp.PreInitSteps).HasColumnType("TEXT");
        entity.Property(wp => wp.PostInitSteps).HasColumnType("TEXT");
        entity.HasOne(wp => wp.Workload)
              .WithMany(w => w.Packages)
              .HasForeignKey(wp => new { wp.WorkloadId, wp.WorkloadVersion })
              .HasPrincipalKey(w => new { w.WorkloadId, w.Version });
    });

    modelBuilder.Entity<AgentPackage>(entity =>
    {
        entity.HasKey(ap => new { ap.AgentId, ap.PackageId });
    });

    modelBuilder.Entity<Artifact>(entity =>
    {
        entity.HasIndex(a => new { a.PackageId, a.Version }).IsUnique();
    });

    modelBuilder.Entity<Workload>(entity =>
    {
        entity.HasIndex(w => new { w.WorkloadId, w.Version }).IsUnique();
    });

    modelBuilder.Entity<EnrollmentToken>(entity =>
    {
        entity.HasIndex(e => e.Token);
    });

    modelBuilder.Entity<AgentNode>(entity =>
    {
        entity.HasIndex(a => a.AgentId).IsUnique();
        entity.HasIndex(a => a.AgentSecret).IsUnique();
        entity.HasIndex(a => a.Status);
        entity.HasOne(a => a.Workload)
              .WithMany()
              .HasForeignKey(a => a.AssignedWorkloadId)
              .HasPrincipalKey(w => w.WorkloadId);
    });

    modelBuilder.Entity<WorkloadRun>(entity =>
    {
        entity.HasIndex(r => r.AgentId);
        entity.HasIndex(r => r.Status);
        entity.HasOne(r => r.Workload)
              .WithMany()
              .HasForeignKey(r => new { r.WorkloadId, r.WorkloadVersion })
              .HasPrincipalKey(w => new { w.WorkloadId, w.Version });
    });

    modelBuilder.Entity<WorkloadRunStep>(entity =>
    {
        entity.HasOne(rs => rs.Run)
              .WithMany(r => r.Steps)
              .HasForeignKey(rs => rs.RunId);
    });
}
```

> **Note (GAP-2):** EF Core requires explicit `.HasOne().WithMany().HasForeignKey()` configuration for string-based foreign keys. The composite FK on `WorkloadPackage → Workload` uses `(WorkloadId, WorkloadVersion)` matching the unique index on `Workload`. Similarly, `WorkloadRun → Workload` uses `(WorkloadId, WorkloadVersion)`, `WorkloadRunStep → WorkloadRun` on `RunId`, and `AgentNode → Workload` on `AssignedWorkloadId`.

### Acceptance Criteria

- [ ] All 8 entity models created with correct properties matching Section 7 schema
- [ ] `AgentNode` model includes `PollingIntervalSeconds` (default 30) and `WorkloadPackage` includes `WorkloadVersion`
- [ ] `WorkloadRunStep` model includes `StepOrder` for deterministic execution order
- [ ] All enum types created matching Section 12 state machines (including `WORKLOAD_ASSIGNED` and `NEEDS_UPDATE` in `AgentNodeStatus`, `AWAITING_CONFIRMATION` in `WorkloadRunStatus`)
- [ ] `AppDbContext` configured with composite keys, unique indexes, FK relationships, and enum → string conversions
- [ ] Explicit FK configuration for string-based business keys: `WorkloadPackage → Workload`, `AgentNode → Workload`, `WorkloadRun → Workload`, `WorkloadRunStep → WorkloadRun`
- [ ] Unique indexes on `AgentNode.AgentId` and `AgentNode.AgentSecret`
- [ ] Composite key for `WorkloadPackage` is `(WorkloadId, WorkloadVersion, PackageId)`
- [ ] Initial migration generated without errors: `dotnet ef migrations add InitialCreate`
- [ ] Migration applies cleanly at runtime: database file created, all tables present
- [ ] `EnsureCreatedAsync()` called on startup for development simplicity
- [ ] Unique constraints enforced: `Artifact.PackageId+Version`, `Workload.WorkloadId+Version`, `AgentNode.AgentId`, `AgentNode.AgentSecret`
- [ ] JSON text columns for `preInitSteps`/`postInitSteps` store and retrieve correctly

### Verification Steps

1. Run `dotnet ef migrations add InitialCreate --project orchestrator/backend` — succeeds
2. Start Orchestrator — `orchestrator.db` created in working directory
3. Open `orchestrator.db` with SQLite browser — verify all 7 tables exist with correct columns
4. Verify unique indexes: attempt INSERT of duplicate `Artifact` with same `packageId+version` → fails with constraint error
5. Verify JSON columns: insert a `WorkloadPackage` with `preInitSteps` = `["net stop SQLBrowser"]`, query it back — string matches

---

## TICKET P1-003: Orchestrator Configuration & Startup Wiring

**MVP Plan Ref:** Section 3 (Configuration)  
**Depends on:** P1-001, P1-002

### Description

Wire up the configuration system with strongly-typed options classes for all `appsettings.json` sections. Configure dependency injection, database initialization (migrate on startup), CORS for development, and static file serving for the React frontend.

### Tasks

- [ ] Create strongly-typed options classes:
  - `AgentOptions` (DefaultPollingIntervalSeconds, LostThresholdMultiplier)
  - `ArtifactStoreOptions` (BasePath)
  - `EnrollmentOptions` (TokenTtlHours)
  - `WebHostOptions` (Port, BindAddress)
  - `WorkloadDefinitionStoreOptions` (Path, WatchForChanges)
  - `DatabaseOptions` (no custom config needed — uses ConnectionStrings:Default)
- [ ] Register options via `builder.Services.Configure<T>(builder.Configuration.GetSection("..."))` in `Program.cs`
- [ ] Register `AppDbContext` with SQLite connection string from configuration
- [ ] Create `StartupExtensions` class for clean DI registration
- [ ] Add CORS policy for development (allow `http://localhost:5173` for Vite dev server)
- [ ] Configure static files to serve React SPA from `wwwroot/`
- [ ] Add SPA fallback routing (return `index.html` for non-API, non-file routes)
- [ ] Add health check endpoint: `GET /api/health` → 200 OK
- [ ] Add `IDataMigrationService` that applies pending migrations on startup
- [ ] Ensure `ArtifactStore.BasePath` directory is created on startup if it doesn't exist

### Code Example — Options Classes

```csharp
// Configuration/AgentOptions.cs
public class AgentOptions
{
    public int DefaultPollingIntervalSeconds { get; set; } = 30;
    public int LostThresholdMultiplier { get; set; } = 3;
}

// Configuration/ArtifactStoreOptions.cs
public class ArtifactStoreOptions
{
    public string BasePath { get; set; } = "./artifacts";

    public string ResolvePath() =>
        Path.IsPathRooted(BasePath) ? BasePath : Path.Combine(AppContext.BaseDirectory, BasePath);
}

// Configuration/EnrollmentOptions.cs
public class EnrollmentOptions
{
    public int TokenTtlHours { get; set; } = 24;
}

// Configuration/WebHostOptions.cs
public class WebHostOptions
{
    public int Port { get; set; } = 5000;
    public string BindAddress { get; set; } = "0.0.0.0";
}

// Configuration/WorkloadDefinitionStoreOptions.cs
public class WorkloadDefinitionStoreOptions
{
    public string Path { get; set; } = "dist/workload-definitions/";
    public bool WatchForChanges { get; set; } = true;
}
```

> **Note (INC-3):** `ArtifactStoreOptions.BasePath` is configurable via `appsettings.json`. Development uses `./artifacts/` (relative to working directory). Production uses `dist/artifacts/` (relative to deployment root). The `ResolvePath()` utility method converts relative paths to absolute based on `AppContext.BaseDirectory`, ensuring consistent behavior in both environments.

> **Note (INC-4):** The options class is named `ArtifactStoreOptions` (matching the `ArtifactStore` config section in `appsettings.json`), not `ArtifactOptions`.

### Code Example — DI Registration

```csharp
// Extensions/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrchestratorServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection("Agent"));
        services.Configure<ArtifactStoreOptions>(configuration.GetSection("ArtifactStore"));
        services.Configure<EnrollmentOptions>(configuration.GetSection("Enrollment"));
        services.Configure<WebHostOptions>(configuration.GetSection("WebHost"));
        services.Configure<WorkloadDefinitionStoreOptions>(configuration.GetSection("WorkloadDefinitionStore"));

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Default")));

        services.AddScoped<IArtifactService, ArtifactService>();
        services.AddScoped<IWorkloadService, WorkloadService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IRunService, RunService>();

        return services;
    }
}
```

### Code Example — Program.cs SPA Fallback

```csharp
app.MapControllers();
app.MapFallbackToFile("index.html");
```

### Acceptance Criteria

- [ ] All options classes created with correct default values matching Section 3 (including `WebHostOptions`, `WorkloadDefinitionStoreOptions`)
- [ ] `ArtifactStoreOptions` (not `ArtifactOptions`) matches `appsettings.json` section name `ArtifactStore`
- [ ] `ArtifactStoreOptions.ResolvePath()` handles both absolute and relative paths correctly
- [ ] `IOptions<T>` injectable in controllers and services
- [ ] Kestrel port and bind address configured from `WebHostOptions`
- [ ] Database migrations applied automatically on startup (via `EnsureCreatedAsync()`)
- [ ] `GET /api/health` returns 200
- [ ] `ArtifactStore.BasePath` directory created on startup
- [ ] `WorkloadDefinitionStore.Path` directory created on startup
- [ ] CORS allows Vite dev server in Development environment
- [ ] Static files served from `wwwroot/`
- [ ] SPA routing: any non-API route returns `index.html`

### Verification Steps

1. Start Orchestrator — verify `artifacts/` directory created relative to working directory
2. `curl http://localhost:5000/api/health` → 200 OK
3. Inject `IOptions<AgentOptions>` into a test controller — read `DefaultPollingIntervalSeconds` → 30
4. Delete `orchestrator.db`, restart Orchestrator — DB recreated automatically
5. Place a test HTML file in `wwwroot/`, restart — file accessible at `http://localhost:5000/test.html`

---

## TICKET P1-004: React Frontend Shell Setup

**MVP Plan Ref:** Section 15, Item 18–26 (Web UI phase) — setup only  
**Depends on:** P1-001

### Description

Scaffold the React frontend with Vite, TypeScript, TailwindCSS, shadcn/ui, TanStack Query, and Zod. Set up the project structure, API client, routing, and a basic layout with sidebar navigation. The app should compile, run, and show a placeholder dashboard in the browser.

### Tasks

- [ ] Initialize Vite + React + TypeScript project in `orchestrator/web/`
- [ ] Install and configure TailwindCSS v4 (or v3 with PostCSS)
- [ ] Initialize shadcn/ui (`npx shadcn@latest init`) with:
  - Style: Default, Color: Slate
  - Components installed: Button, Card, Dialog, Input, Label, Select, Table, Tabs, Toast, Badge, Alert, DropdownMenu, Sidebar
- [ ] Install TanStack Query (`@tanstack/react-query`) and configure `QueryClientProvider` in `main.tsx`
- [ ] Install Zod for form/input validation
- [ ] Install `react-router-dom` for routing
- [ ] Create API client in `src/lib/api.ts`:
  - Base URL configurable (default: `http://localhost:5000/api`)
  - Generic `fetchApi<T>` function with error handling
  - TanStack Query compatible
- [ ] Create type definitions in `src/types/` matching backend models:
  - `agent.ts` — AgentNode, AgentPackage, etc.
  - `artifact.ts` — Artifact, BulkImportResult, etc.
  - `workload.ts` — Workload, WorkloadPackage, etc.
  - `enrollment.ts` — EnrollmentToken, etc.
  - `run.ts` — WorkloadRun, WorkloadRunStep, enums
- [ ] Create page components (placeholder shells):
  - `DashboardPage.tsx`
  - `ArtifactsPage.tsx`
  - `WorkloadsPage.tsx`
  - `AgentsPage.tsx`
  - `EnrollmentPage.tsx`
  - `RunsPage.tsx`
- [ ] Create sidebar layout with `AppSidebar` component (links to all pages)
- [ ] Configure Vite proxy in `vite.config.ts`: proxy `/api` → `http://localhost:5000` during development
- [ ] Configure build output: `vite.config.ts` → `build.outDir: '../backend/wwwroot'`
- [ ] Verify `npm run build` produces assets in `orchestrator/backend/wwwroot/`

### Code Example — vite.config.ts

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../backend/wwwroot',
    emptyOutDir: true,
  },
})
```

### Code Example — API Client (src/lib/api.ts)

```typescript
const API_BASE = '/api'

async function fetchApi<T>(
  endpoint: string,
  options?: RequestInit
): Promise<T> {
  const response = await fetch(`${API_BASE}${endpoint}`, {
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
    ...options,
  })

  if (!response.ok) {
    const error = await response.json().catch(() => ({ message: response.statusText }))
    throw new Error(error.message || `API error: ${response.status}`)
  }

  if (response.status === 204) return undefined as T
  return response.json()
}

export const api = {
  get: <T>(endpoint: string) => fetchApi<T>(endpoint),
  post: <T>(endpoint: string, body?: unknown) =>
    fetchApi<T>(endpoint, {
      method: 'POST',
      body: body ? JSON.stringify(body) : undefined,
    }),
  put: <T>(endpoint: string, body: unknown) =>
    fetchApi<T>(endpoint, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  delete: <T>(endpoint: string) =>
    fetchApi<T>(endpoint, { method: 'DELETE' }),
  upload: <T>(endpoint: string, formData: FormData) =>
    fetchApi<T>(endpoint, {
      method: 'POST',
      headers: {},
      body: formData,
    }),
}
```

### Code Example — App Layout with Sidebar

```tsx
// src/App.tsx
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SidebarProvider } from '@/components/ui/sidebar'
import { AppSidebar } from '@/components/AppSidebar'
import { DashboardPage } from '@/pages/DashboardPage'
// ... other page imports

const queryClient = new QueryClient()

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <SidebarProvider>
          <AppSidebar />
          <main className="flex-1 p-6">
            <Routes>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/artifacts" element={<ArtifactsPage />} />
              <Route path="/workloads" element={<WorkloadsPage />} />
              <Route path="/agents" element={<AgentsPage />} />
              <Route path="/enrollment" element={<EnrollmentPage />} />
              <Route path="/runs" element={<RunsPage />} />
            </Routes>
          </main>
        </SidebarProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
```

### Acceptance Criteria

- [ ] `cd orchestrator/web && npm run dev` starts Vite dev server on port 5173
- [ ] App loads in browser with sidebar navigation and placeholder pages
- [ ] Sidebar links navigate between all pages without reload
- [ ] `npm run build` produces output in `orchestrator/backend/wwwroot/`
- [ ] TanStack Query `QueryClientProvider` wraps the app
- [ ] Vite proxy correctly forwards `/api/*` requests to `http://localhost:5000`
- [ ] All shadcn/ui components render correctly
- [ ] TypeScript compiles without errors

### Verification Steps

1. Run `cd orchestrator/web && npm run dev` — browser opens, app renders with sidebar
2. Click each sidebar link — page content changes without full reload
3. Start Orchestrator backend, then visit `http://localhost:5173/api/health` via browser — returns 200 (proxied)
4. Run `npm run build` — files appear in `../backend/wwwroot/`
5. Run `npx tsc --noEmit` — no TypeScript errors

---

## TICKET P1-005: Enrollment Token Generation API

**MVP Plan Ref:** Section 4 (Enrollment Flow), Section 9 (API Endpoints)  
**Depends on:** P1-003

### Description

Implement the enrollment token generation endpoint. Admins use this to create short-lived, single-use tokens that agents will use during enrollment.

### Tasks

- [ ] Create `IEnrollmentService` interface and `EnrollmentService` implementation
- [ ] `GenerateTokenAsync()` — generate cryptographically random token (UUID or base64), set TTL from `EnrollmentOptions.TokenTtlHours`, store in DB
- [ ] Create `EnrollmentTokensController` with `POST /api/enrollment/tokens`
- [ ] Response model: `{ token: string, expiresAt: datetime }`
- [ ] Add request validation with data annotations or FluentValidation
- [ ] Add logging (ILogger) for token generation events

### Code Example — Controller

```csharp
// Controllers/EnrollmentTokensController.cs
[ApiController]
[Route("api/enrollment/tokens")]
public class EnrollmentTokensController : ControllerBase
{
    private readonly IEnrollmentService _enrollmentService;

    public EnrollmentTokensController(IEnrollmentService enrollmentService)
    {
        _enrollmentService = enrollmentService;
    }

    [HttpPost]
    public async Task<ActionResult<EnrollmentTokenResponse>> GenerateToken()
    {
        var result = await _enrollmentService.GenerateTokenAsync();
        return Ok(result);
    }
}
```

### Code Example — Service

```csharp
// Services/EnrollmentService.cs
public class EnrollmentService : IEnrollmentService
{
    private readonly AppDbContext _db;
    private readonly IOptions<EnrollmentOptions> _options;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        AppDbContext db,
        IOptions<EnrollmentOptions> options,
        ILogger<EnrollmentService> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    public async Task<EnrollmentTokenResponse> GenerateTokenAsync()
    {
        var token = Guid.NewGuid().ToString("N");
        var expiresAt = DateTime.UtcNow.AddHours(_options.Value.TokenTtlHours);

        var entity = new EnrollmentToken
        {
            Token = token,
            ExpiresAt = expiresAt
        };

        _db.EnrollmentTokens.Add(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Enrollment token generated, expires at {ExpiresAt}", expiresAt);

        return new EnrollmentTokenResponse(token, expiresAt);
    }
}
```

### Acceptance Criteria

- [ ] `POST /api/enrollment/tokens` returns `{ token, expiresAt }` with 200 OK
- [ ] Token is a cryptographically random string (UUID format or similar)
- [ ] Token stored in `EnrollmentTokens` table with `used = false`
- [ ] `expiresAt` matches the configured TTL (default 24h)
- [ ] Calling the endpoint twice produces two different tokens

### Verification Steps

1. `curl -X POST http://localhost:5000/api/enrollment/tokens` — returns 200 with token and expiresAt
2. Query SQLite DB: `SELECT * FROM EnrollmentTokens` — shows 1 row with `used = 0`
3. Call endpoint again — new token generated, both exist in DB
4. Verify token format is random/unpredictable (not sequential or predictable)

---

## TICKET P1-006: Agent Enrollment & Unregistration API

**MVP Plan Ref:** Section 4 (Enrollment Flow), Section 8 (API Contract)  
**Depends on:** P1-003, P1-005

### Description

Implement the agent enrollment endpoint (called by `Agent.exe --enroll`) and the unregistration endpoint (called by `Agent.exe --reset`). This covers token validation, AgentNode creation, token invalidation, and agent secret generation.

### Tasks

- [ ] Create `IAgentService` interface and `AgentService` implementation
- [ ] `EnrollAsync(token, hostname, ipAddress)` — validate enrollment token, create AgentNode, generate agentSecret, invalidate token
- [ ] `UnregisterAsync(agentId, agentSecret)` — validate agentSecret, mark AgentNode as UNREGISTERED
- [ ] Create `AgentsController` with:
  - `POST /api/agents/enroll` — body: `{ token, hostname, ipAddress }`, response: `{ agentId, agentSecret, pollingIntervalSeconds }`
  - `POST /api/agents/{agentId}/unregister` — header: `Authorization: Bearer <agentSecret>`
- [ ] Add enrollment token validation: check token exists, not expired, not used
- [ ] Add `agentSecret` validation for unregistration requests
- [ ] Return `pollingIntervalSeconds` from `AgentOptions.DefaultPollingIntervalSeconds` in enrollment response
- [ ] Create `AgentAuthMiddleware` that validates `Authorization: Bearer {agentSecret}` header:
  - Look up `AgentNode` by `AgentSecret` in DB
  - Set `HttpContext.Items["AgentNode"]` for downstream handlers
  - Apply to all `/api/agents/{agentId}/*` endpoints (except `POST /api/agents/enroll`)
  - Reject unauthenticated requests with 401
- [ ] Register `AgentAuthMiddleware` in DI and middleware pipeline
- [ ] Add request/response DTOs with validation
- [ ] Add logging for enrollment and unregistration events

### Code Example — Enrollment DTOs

```csharp
// Models/Dto/EnrollRequest.cs
public class EnrollRequest
{
    [Required] public string Token { get; set; } = string.Empty;
    [Required] public string Hostname { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}

// Models/Dto/EnrollResponse.cs
public class EnrollResponse
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentSecret { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; }
}

// Models/Dto/UnregisterResponse.cs
public class UnregisterResponse
{
    public string Message { get; set; } = string.Empty;
}
```

### Code Example — Enrollment Logic

```csharp
// Services/AgentService.cs (enrollment portion)
public async Task<EnrollResponse> EnrollAsync(EnrollRequest request)
{
    var enrollmentToken = await _db.EnrollmentTokens
        .FirstOrDefaultAsync(t => t.Token == request.Token);

    if (enrollmentToken == null)
        throw new InvalidOperationException("Invalid enrollment token");

    if (enrollmentToken.Used)
        throw new InvalidOperationException("Token already used");

    if (enrollmentToken.ExpiresAt < DateTime.UtcNow)
        throw new InvalidOperationException("Token expired");

    var agentId = Guid.NewGuid().ToString();
    var agentSecret = Guid.NewGuid().ToString();

    var agentNode = new AgentNode
    {
        AgentId = agentId,
        Hostname = request.Hostname,
        IpAddress = request.IpAddress ?? "unknown",
        AgentSecret = agentSecret,
        Status = AgentNodeStatus.REGISTERED,
        LastSeenAt = DateTime.UtcNow,
        RegisteredAt = DateTime.UtcNow
    };

    enrollmentToken.Used = true;
    enrollmentToken.UsedAt = DateTime.UtcNow;
    enrollmentToken.UsedByAgentId = agentId;

    _db.AgentNodes.Add(agentNode);
    _db.EnrollmentTokens.Update(enrollmentToken);
    await _db.SaveChangesAsync();

    _logger.LogInformation("Agent {AgentId} enrolled successfully", agentId);

    return new EnrollResponse
    {
        AgentId = agentId,
        AgentSecret = agentSecret,
        PollingIntervalSeconds = _options.Value.DefaultPollingIntervalSeconds
    };
}
```

### Acceptance Criteria

- [ ] `POST /api/agents/enroll` with valid token returns `{ agentId, agentSecret, pollingIntervalSeconds }` with 200 OK
- [ ] Enrollment token invalidated after use (`used = true`)
- [ ] Enrollment with expired token returns 400 with error message
- [ ] Enrollment with already-used token returns 400 with error message
- [ ] Enrollment with nonexistent token returns 400 with error message
- [ ] `POST /api/agents/{agentId}/unregister` with valid agentSecret returns 200 and marks AgentNode as `UNREGISTERED`
- [ ] Unregistration with invalid agentSecret returns 401
- [ ] Unregistration with unknown agentId returns 404
- [ ] `AgentAuthMiddleware` rejects requests to `/api/agents/{agentId}/*` without valid `Authorization: Bearer` header → 401
- [ ] `AgentAuthMiddleware` sets `HttpContext.Items["AgentNode"]` for authenticated requests
- [ ] `POST /api/agents/enroll` is exempt from auth middleware

### Verification Steps

1. Generate an enrollment token via `POST /api/enrollment/tokens`
2. Enroll an agent: `POST /api/agents/enroll` with the token, hostname, and IP → 200, response contains agentId, agentSecret, pollingIntervalSeconds=30
3. Try enrolling again with the same token → 400 "Token already used"
4. Query DB: `AgentNode` record exists with status `REGISTERED`
5. Unregister: `POST /api/agents/{agentId}/unregister` with `Authorization: Bearer {agentSecret}` → 200
6. Try enrolling with expired token → 400 "Token expired"

---

## TICKET P1-007: Agent CLI — Enrollment (--enroll)

**MVP Plan Ref:** Section 4 (Single-Command Enrollment)  
**Depends on:** P1-006

### Description

Implement the `Agent.exe --enroll <token> --url <orchestrator-url>` CLI mode. This performs the full enrollment sequence: call the enroll API → write `agent.json` → register as Windows Service via SCM → start the service.

### Tasks

- [ ] Add CLI argument parsing to Agent project (use `System.CommandLine` or manual args parsing)
- [ ] Implement `--enroll` flow:
  1. Parse `--enroll <token>` and `--url <orchestrator-url-or-ip>` arguments
  2. Get local hostname and IP address
  3. Call `POST http://{url}/api/agents/enroll` with `{ token, hostname, ipAddress }`
  4. On success, write `agent.json` to `%ProgramData%\OrchestratorAgent\`
  5. Register Agent.exe as Windows Service via `ScmService.InstallServiceAsync()`
  6. Start service via `ScmService.StartServiceAsync()`
  7. Print confirmation message and exit
- [ ] Create `Configuration/AgentConfig.cs` model matching `agent.json` schema
- [ ] Create `Services/ScmService.cs` for Windows Service Control Manager P/Invoke operations
- [ ] Handle errors: invalid token, network failure, service registration failure
- [ ] Create `%ProgramData%\OrchestratorAgent\` directory if it doesn't exist

### Code Example — P/Invoke Declarations

```csharp
// PInvoke/ScmNativeMethods.cs
internal static class ScmNativeMethods
{
    private const string Advapi32 = "advapi32.dll";

    [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenSCManager(
        string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenServiceW(
        IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport(Advapi32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateService(
        IntPtr hSCManager, string lpServiceName, string lpDisplayName,
        uint dwDesiredAccess, uint dwServiceType, uint dwStartType,
        uint dwErrorControl, string lpBinaryPathName, string? lpLoadOrderGroup,
        IntPtr lpdwTagId, string? lpDependencies, string? lpServiceStartName,
        string? lpPassword);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool DeleteService(IntPtr hService);

    [DllImport(Advapi32, SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    public const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    public const uint SERVICE_ALL_ACCESS = 0x000F01FF;
    public const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    public const uint SERVICE_AUTO_START = 0x00000002;
    public const uint SERVICE_ERROR_NORMAL = 0x00000001;
    public const uint SERVICE_CONTROL_STOP = 0x00000001;
    public const uint SERVICE_STOPPED = 0x00000001;
    public const uint SERVICE_RUNNING = 0x00000004;
}
```

### Code Example — ScmService Class

```csharp
// Services/ScmService.cs
public class ScmService : IDisposable
{
    private IntPtr _scManagerHandle;

    private IntPtr GetScManagerHandle()
    {
        if (_scManagerHandle == IntPtr.Zero)
        {
            _scManagerHandle = ScmNativeMethods.OpenSCManager(null, null, ScmNativeMethods.SC_MANAGER_CREATE_SERVICE);
            if (_scManagerHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return _scManagerHandle;
    }

    public async Task<bool> InstallServiceAsync(string serviceName, string binaryPath, string? displayName = null)
    {
        return await Task.Run(() =>
        {
            var hService = ScmNativeMethods.CreateService(
                GetScManagerHandle(), serviceName, displayName ?? serviceName,
                ScmNativeMethods.SERVICE_ALL_ACCESS, ScmNativeMethods.SERVICE_WIN32_OWN_PROCESS,
                ScmNativeMethods.SERVICE_AUTO_START, ScmNativeMethods.SERVICE_ERROR_NORMAL,
                binaryPath, null, IntPtr.Zero, null, null, null);

            if (hService == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            ScmNativeMethods.CloseServiceHandle(hService);
            return true;
        });
    }

    public async Task<bool> StartServiceAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            var hService = ScmNativeMethods.OpenServiceW(GetScManagerHandle(), serviceName, ScmNativeMethods.SERVICE_ALL_ACCESS);
            if (hService == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                if (!ScmNativeMethods.StartService(hService, 0, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != 1056) // ERROR_SERVICE_ALREADY_RUNNING
                        throw new Win32Exception(error);
                }
                return true;
            }
            finally
            {
                ScmNativeMethods.CloseServiceHandle(hService);
            }
        });
    }

    public async Task<bool> StopServiceAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            var hService = ScmNativeMethods.OpenServiceW(GetScManagerHandle(), serviceName, ScmNativeMethods.SERVICE_ALL_ACCESS);
            if (hService == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var status = new ScmNativeMethods.SERVICE_STATUS();
                if (!ScmNativeMethods.ControlService(hService, ScmNativeMethods.SERVICE_CONTROL_STOP, ref status))
                {
                    var lastError = Marshal.GetLastWin32Error();
                    if (lastError != 1062) // ERROR_SERVICE_NOT_ACTIVE
                        throw new Win32Exception(lastError);
                }
                return true;
            }
            finally
            {
                ScmNativeMethods.CloseServiceHandle(hService);
            }
        });
    }

    public async Task<ServiceControllerStatus> GetServiceStatusAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            var hService = ScmNativeMethods.OpenServiceW(GetScManagerHandle(), serviceName, ScmNativeMethods.SERVICE_ALL_ACCESS);
            if (hService == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var status = new ScmNativeMethods.SERVICE_STATUS();
                ScmNativeMethods.QueryServiceStatus(hService, ref status);
                return (ServiceControllerStatus)status.dwCurrentState;
            }
            finally
            {
                ScmNativeMethods.CloseServiceHandle(hService);
            }
        });
    }

    public async Task<bool> DeleteServiceAsync(string serviceName)
    {
        return await Task.Run(() =>
        {
            var hService = ScmNativeMethods.OpenServiceW(GetScManagerHandle(), serviceName, ScmNativeMethods.SERVICE_ALL_ACCESS);
            if (hService == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                if (!ScmNativeMethods.DeleteService(hService))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return true;
            }
            finally
            {
                ScmNativeMethods.CloseServiceHandle(hService);
            }
        });
    }

    public void Dispose()
    {
        if (_scManagerHandle != IntPtr.Zero)
        {
            ScmNativeMethods.CloseServiceHandle(_scManagerHandle);
            _scManagerHandle = IntPtr.Zero;
        }
    }
}
```

### Code Example — Enrollment Flow

```csharp
// Services/AgentEnrollService.cs (enroll mode handler)
public async Task<int> ExecuteEnrollAsync(string enrollToken, string orchestratorUrl)
{
    var httpClient = new HttpClient();
    var response = await httpClient.PostAsJsonAsync(
        $"{orchestratorUrl}/api/agents/enroll",
        new { token = enrollToken, hostname = Environment.MachineName, ipAddress = GetLocalIpAddress() });

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Enrollment failed: {response.StatusCode}");
        return 1;
    }

    var result = await response.Content.ReadFromJsonAsync<EnrollResponse>();
    var agentConfig = new AgentConfig
    {
        AgentId = result!.AgentId,
        OrchestratorUrl = orchestratorUrl,
        AgentSecret = result.AgentSecret,
        PollingIntervalSeconds = result.PollingIntervalSeconds
    };

    var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OrchestratorAgent");
    Directory.CreateDirectory(configDir);
    var configPath = Path.Combine(configDir, "agent.json");
    await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(agentConfig, new JsonSerializerOptions { WriteIndented = true }));

    using var scmService = new ScmService();
    var exePath = Process.GetCurrentProcess().MainModule!.FileName;
    await scmService.InstallServiceAsync("Agent", exePath, "Orchestrator Agent");
    await scmService.StartServiceAsync("Agent");

    Console.WriteLine($"Agent enrolled successfully. AgentId: {result.AgentId}");
    return 0;
}
```

### Acceptance Criteria

- [ ] `Agent.exe --enroll <valid-token> --url http://host:5000` completes full enrollment sequence
- [ ] `agent.json` written to `%ProgramData%\OrchestratorAgent\` with correct agentId, orchestratorUrl, agentSecret, pollingIntervalSeconds
- [ ] Windows Service "Agent" registered and started via SCM API
- [ ] Invalid/expired token shows clear error message and exits with code 1
- [ ] Network failure shows clear error message and exits with code 1
- [ ] Service registration failure shows clear error message and exits with code 1
- [ ] `%ProgramData%\OrchestratorAgent\` directory created if it doesn't exist

### Verification Steps

1. Start Orchestrator backend
2. Generate enrollment token via API
3. Run `Agent.exe --enroll <token> --url http://localhost:5000`
4. Verify `agent.json` exists at `%ProgramData%\OrchestratorAgent\agent.json` with correct content
5. Verify Windows Service "Agent" appears in `services.msc` and is running
6. Try with invalid token → error message, no service registration
7. Try with unreachable URL → error message, no service registration

---

## TICKET P1-008: Agent CLI — Reset (--reset)

**MVP Plan Ref:** Section 4 (Single-Command Reset & Unregistration)  
**Depends on:** P1-006

### Description

Implement the `Agent.exe --reset` CLI mode. This performs the full teardown: unregister with Orchestrator → stop service → delete service → delete `agent.json`.

### Tasks

- [ ] Add `--reset` CLI argument parsing
- [ ] Implement `--reset` flow:
  1. Read `agent.json` from `%ProgramData%\OrchestratorAgent\`
  2. If `agent.json` doesn't exist, print error and exit
  3. Call `POST /api/agents/{agentId}/unregister` with bearer token (best effort — continue even if Orchestrator unreachable)
  4. Stop Windows Service via SCM (`StopService`)
  5. Delete Windows Service via SCM (`DeleteService`)
  6. Delete `agent.json`
  7. Print confirmation and exit
- [ ] Add `ScmService` methods for `StopService` and `DeleteService`
- [ ] Handle Orchestrator unreachable gracefully — proceed with local cleanup regardless

> **Agent Entry Point Architecture (MISS-3):** The Agent has three runtime modes dispatched from `Program.cs` before the host builder:
> 1. `--enroll <token> --url <orchestratorUrl>` — runs enrollment, writes `agent.json`, registers and starts Windows Service, then exits
> 2. `--reset` — removes `agent.json`, stops and uninstalls service, calls unregistration API (best-effort), then exits
> 3. Default (no CLI args) — `UseWindowsService()` host builder, runs the polling daemon

### Code Example — Reset Flow

```csharp
// Services/AgentResetService.cs (reset mode handler)
public async Task<int> ExecuteResetAsync()
{
    var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OrchestratorAgent");
    var configPath = Path.Combine(configDir, "agent.json");

    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine("No agent.json found. Agent is not enrolled.");
        return 1;
    }

    var agentConfig = JsonSerializer.Deserialize<AgentConfig>(await File.ReadAllTextAsync(configPath))!;

    try
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentConfig.AgentSecret);
        await httpClient.PostAsync($"{agentConfig.OrchestratorUrl}/api/agents/{agentConfig.AgentId}/unregister", null);
        Console.WriteLine("Unregistered with Orchestrator.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Could not reach Orchestrator for unregistration: {ex.Message}");
    }

    using var scmService = new ScmService();
    await scmService.StopServiceAsync("Agent");
    await scmService.DeleteServiceAsync("Agent");

    File.Delete(configPath);
    Console.WriteLine("Agent reset complete. Service removed and configuration deleted.");
    return 0;
}
```

### Acceptance Criteria

- [ ] `Agent.exe --reset` completes full teardown sequence
- [ ] Unregistration API called with correct agentSecret (when Orchestrator reachable)
- [ ] Windows Service "Agent" stopped and deleted
- [ ] `agent.json` deleted from `%ProgramData%\OrchestratorAgent\`
- [ ] If Orchestrator unreachable, local cleanup (steps 4-6) still completes
- [ ] If `agent.json` doesn't exist, shows error and exits with code 1
- [ ] Confirmation message printed on success

### Verification Steps

1. Enroll an agent first (P1-007 verification)
2. Run `Agent.exe --reset`
3. Verify Agent service is stopped and removed from `services.msc`
4. Verify `agent.json` is deleted
5. Verify AgentNode status in DB is `UNREGISTERED`
6. Test with Orchestrator stopped — reset should still work (local cleanup succeeds, API call fails gracefully)

---

## TICKET P1-009: Artifact Single Upload API + Disk Store

**MVP Plan Ref:** Section 9 (Upload Endpoints), Section 6.1 (Package Manifest JSON)  
**Depends on:** P1-003

### Description

Implement the single artifact upload endpoint. This accepts a binary file and manifest JSON as multipart form data, validates the pairing (installerFile matches binary filename), stores both on disk, and creates a database record.

### Tasks

- [ ] Create `IArtifactService` interface and `ArtifactService` implementation
- [ ] Create `ArtifactsController` with `POST /api/artifacts` (multipart/form-data)
- [ ] Create request model: `ArtifactUploadRequest` (binary file + manifest file)
- [ ] Create response model: `ArtifactUploadResponse` (id, packageId, version, installerFile, uploadedAt)
- [ ] Create `PackageManifest` model for JSON deserialization with `[JsonPropertyName]` attributes:
  - `packageId`, `version` (mapped from JSON `"version"`), `hash`, `downloadUrl`, `filename`, `preInitSteps`, `postInitSteps`, `updateStrategy`
- [ ] Implement upload logic:
  1. Parse multipart form data (binary + manifest)
  2. Deserialize manifest JSON, validate required fields (`packageId`, `version`, `installerFile`, `detection`)
  3. Verify `installerFile` in manifest matches the uploaded binary's filename
  4. Reject if `packageId + version` already exists in DB
  5. Store binary to `{ArtifactStore.BasePath}/{packageId}/{version}/{filename}`
  6. Store manifest to `{ArtifactStore.BasePath}/{packageId}/{version}/{stem}.json`
  7. Create `Artifacts` DB record with paths, packageId, version, etc.
- [ ] Add Zod schema for manifest JSON validation (for future React form use)
- [ ] Add request logging and error response models

### Code Example — PackageManifest Model

```csharp
// Models/PackageManifest.cs
using System.Text.Json.Serialization;

public class PackageManifest
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("preInitSteps")]
    public List<string> PreInitSteps { get; set; } = [];

    [JsonPropertyName("postInitSteps")]
    public List<string> PostInitSteps { get; set; } = [];

    [JsonPropertyName("updateStrategy")]
    public string UpdateStrategy { get; set; } = "reinstall";
}
```

> **Note (GAP-5):** The `[JsonPropertyName("version")]` attribute on `PackageManifest.Version` ensures the JSON property name matches the MVP Plan specification, which uses `"version"` rather than the C# default `"Version"`. The same attribute is needed on `WorkloadManifest.Version`.

### Code Example — Controller

```csharp
// Controllers/ArtifactsController.cs
[ApiController]
[Route("api/artifacts")]
public class ArtifactsController : ControllerBase
{
    private readonly IArtifactService _artifactService;

    public ArtifactsController(IArtifactService artifactService)
    {
        _artifactService = artifactService;
    }

    [HttpPost]
    public async Task<ActionResult<ArtifactUploadResponse>> Upload(
        IFormFile binary, IFormFile manifest)
    {
        try
        {
            var result = await _artifactService.UploadAsync(binary, manifest);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
```

### Code Example — Manifest Validation

```csharp
// Services/ArtifactService.cs (upload portion)
public async Task<ArtifactUploadResponse> UploadAsync(IFormFile binary, IFormFile manifest)
{
    var manifestContent = await ReadManifestAsync(manifest);
    var manifestObj = JsonSerializer.Deserialize<PackageManifest>(manifestContent)!;

    if (manifestObj.InstallerFile != binary.FileName)
        throw new InvalidOperationException(
            $"Manifest installerFile '{manifestObj.InstallerFile}' does not match binary filename '{binary.FileName}'");

    if (await _db.Artifacts.AnyAsync(a => a.PackageId == manifestObj.PackageId && a.Version == manifestObj.Version))
        throw new InvalidOperationException(
            $"Artifact with packageId '{manifestObj.PackageId}' version '{manifestObj.Version}' already exists");

    var artifactDir = Path.Combine(_options.Value.BasePath, manifestObj.PackageId, manifestObj.Version);
    Directory.CreateDirectory(artifactDir);

    var binaryPath = Path.Combine(artifactDir, binary.FileName);
    var manifestPath = Path.Combine(artifactDir, Path.GetFileNameWithoutExtension(binary.FileName) + ".json");

    await using (var stream = System.IO.File.Create(binaryPath))
        await binary.CopyToAsync(stream);
    await System.IO.File.WriteAllTextAsync(manifestPath, manifestContent);

    var artifact = new Artifact
    {
        PackageId = manifestObj.PackageId,
        PackageName = manifestObj.PackageName,
        Version = manifestObj.Version,
        InstallerFile = manifestObj.InstallerFile,
        ManifestPath = manifestPath,
        BinaryPath = binaryPath,
        UploadedAt = DateTime.UtcNow
    };

    _db.Artifacts.Add(artifact);
    await _db.SaveChangesAsync();

    return new ArtifactUploadResponse(artifact.Id, artifact.PackageId, artifact.Version, artifact.InstallerFile, artifact.UploadedAt);
}
```

### Acceptance Criteria

- [ ] `POST /api/artifacts` with valid binary + manifest pair returns 200 with artifact info
- [ ] Binary and manifest stored on disk at `{BasePath}/{packageId}/{version}/`
- [ ] `Artifacts` DB record created with correct fields
- [ ] Manifest `installerFile` validation enforced — mismatch returns 400
- [ ] Duplicate `packageId + version` rejected with 400 error
- [ ] Manifest JSON validated for required fields (`packageId`, `version`, `installerFile`, `detection.type`)
- [ ] Large file upload handled correctly (test with >10MB file)

### Verification Steps

1. Create a valid manifest JSON and a dummy binary file
2. `curl -X POST http://localhost:5000/api/artifacts -F "binary=@installer.exe" -F "manifest=@manifest.json"` → 200 OK
3. Check `artifacts/{packageId}/{version}/` directory — both files present
4. Check DB — `Artifacts` table has one record
5. Upload same `packageId + version` again → 400 "already exists"
6. Upload manifest with `installerFile` not matching binary filename → 400
7. Upload manifest missing required fields → 400

---

## TICKET P1-010: Artifact Bulk Import API (ZIP)

**MVP Plan Ref:** Section 6.3 (Bulk Artifact Import), Section 9 (Upload Endpoints)  
**Depends on:** P1-009

### Description

Implement the bulk artifact import endpoint that accepts a flat ZIP file, pairs binaries with manifests by filename stem, validates each pair, and returns an import summary.

### Tasks

- [ ] Add `POST /api/artifacts/bulk` endpoint to `ArtifactsController`
- [ ] Implement bulk import logic in `ArtifactService`:
  1. Extract ZIP to temp directory using `ZipArchive` with proper stream handling (`using var archive = new ZipArchive(stream, ZipArchiveMode.Read)`)
  2. Validate ZIP is flat (no subdirectories)
  3. For each `.json` file, find matching binary by filename stem
  4. Validate each manifest JSON (required fields, detection type)
  5. Validate `installerFile` matches paired binary filename
  6. Reject duplicate `packageId + version` (already in DB)
  7. Store valid pairs, create DB records
  8. Collect failures with reasons
  9. Return import summary: `{ imported: [...], failed: [...] }`
- [ ] Add response models: `BulkImportResponse`, `ImportedArtifact`, `FailedArtifact`
- [ ] Clean up temp directory after processing (success or failure)
- [ ] Add logging for each imported/failed artifact

### Code Example — Bulk Import Logic

```csharp
// Services/ArtifactService.cs (bulk import portion)
public async Task<BulkImportResponse> BulkImportAsync(IFormFile archiveFile)
{
    var imported = new List<ImportedArtifact>();
    var failed = new List<FailedArtifact>();

    var tempDir = Path.Combine(Path.GetTempPath(), $"orchestrator-import-{Guid.NewGuid():N}");
    try
    {
        using var stream = archiveFile.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(tempDir);

        var jsonFiles = Directory.GetFiles(tempDir, "*.json", SearchOption.TopDirectoryOnly);
        if (Directory.GetDirectories(tempDir).Length > 0)
            throw new InvalidOperationException("ZIP must be flat (no subdirectories)");

        foreach (var jsonPath in jsonFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(jsonPath);
            var matchingBinary = Directory.GetFiles(tempDir, $"{stem}.*")
                .FirstOrDefault(f => !f.EndsWith(".json"));

            if (matchingBinary == null)
            {
                failed.Add(new FailedArtifact(Path.GetFileName(jsonPath), "No matching binary found"));
                continue;
            }

            var binaryFileName = Path.GetFileName(matchingBinary);
            var manifestContent = await File.ReadAllTextAsync(jsonPath);

            PackageManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<PackageManifest>(manifestContent)!;
            }
            catch
            {
                failed.Add(new FailedArtifact(Path.GetFileName(jsonPath), "Invalid manifest JSON"));
                continue;
            }

            if (manifest.InstallerFile != binaryFileName)
            {
                failed.Add(new FailedArtifact(Path.GetFileName(jsonPath),
                    $"installerFile '{manifest.InstallerFile}' does not match binary '{binaryFileName}'"));
                continue;
            }

            if (await _db.Artifacts.AnyAsync(a => a.PackageId == manifest.PackageId && a.Version == manifest.Version))
            {
                failed.Add(new FailedArtifact(Path.GetFileName(jsonPath),
                    $"Duplicate: packageId '{manifest.PackageId}' version '{manifest.Version}' already exists"));
                continue;
            }

            var artifactDir = Path.Combine(_options.Value.BasePath, manifest.PackageId, manifest.Version);
            Directory.CreateDirectory(artifactDir);
            var destBinary = Path.Combine(artifactDir, binaryFileName);
            var destManifest = Path.Combine(artifactDir, stem + ".json");
            File.Copy(matchingBinary, destBinary, overwrite: true);
            File.Copy(jsonPath, destManifest, overwrite: true);

            var artifact = new Artifact { /* ... set all fields ... */ };
            _db.Artifacts.Add(artifact);
            imported.Add(new ImportedArtifact(manifest.PackageId, manifest.Version, binaryFileName));
        }

        await _db.SaveChangesAsync();
    }
    finally
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    return new BulkImportResponse(imported, failed);
}
```

### Acceptance Criteria

- [ ] `POST /api/artifacts/bulk` with valid ZIP returns 200 with import summary
- [ ] Flat ZIP with valid pairs: all artifacts imported, DB records created, files on disk
- [ ] ZIP with subdirectories: rejected with error
- [ ] Manifest without matching binary: listed in `failed` with reason "No matching binary found"
- [ ] `installerFile` mismatch: listed in `failed` with reason
- [ ] Duplicate `packageId + version`: listed in `failed`, existing record preserved
- [ ] Invalid manifest JSON: listed in `failed` with reason
- [ ] Temp directory cleaned up after processing (success or failure)
- [ ] Mixed valid + invalid entries in ZIP: valid ones imported, invalid ones in `failed`

### Verification Steps

1. Create a flat ZIP with 3 valid pairs (manifest + binary)
2. Upload ZIP → all 3 imported, summary shows 3 in `imported`, 0 in `failed`
3. Check disk: 3 directories under `artifacts/` with correct files
4. Upload same ZIP again → all 3 in `failed` with "already exists"
5. Create ZIP with 1 valid pair and 1 manifest without binary → 1 imported, 1 failed
6. Create ZIP with nested directories → rejected with error
7. Create ZIP with manifest where `installerFile` doesn't match binary name → in `failed`

---

## TICKET P1-011: Workload Upload API (Single + Bulk)

**MVP Plan Ref:** Section 6.2 (Workload Definition JSON), Section 9 (Upload Endpoints)  
**Depends on:** P1-003

### Description

Implement the workload upload endpoint that accepts a single workload JSON object or an array of workload objects. Supports upsert on conflict (same `workloadId + version`).

### Tasks

- [ ] Create `IWorkloadService` interface and `WorkloadService` implementation
- [ ] Create `WorkloadsController` with `POST /api/workloads`
- [ ] Auto-detect single object vs. array in request body
- [ ] Create request/response models:
  - `WorkloadUploadRequest` (matches Section 6.2 schema)
  - `WorkloadUploadResponse` with `imported`, `updated`, `failed` arrays
- [ ] Implement upload logic:
  1. Parse JSON body as single object or array
  2. Validate each workload: `workloadId`, `workloadName`, `version`, `packages` array
  3. Validate each package entry: `packageId`, `version`, referential integrity (packageId+version exists in Artifacts table is a soft check — log warning but don't block for MVP)
  4. For each workload: if `workloadId + version` exists, update changed fields (upsert); else insert
  5. Store workload definition JSON to `{BasePath}/workload-definitions/{workloadId}/{version}.json`
  6. Create/update `Workloads` and `WorkloadPackages` DB records
  7. Return summary: `{ imported: [...], updated: [...], failed: [...] }`
- [ ] Add Zod schema for workload JSON validation (for React form use)

> **Upsert Logic Specification (MISS-1):**
> - **Workload upsert:** On matching `workloadId + version`, replace Workload row fields entirely (`workloadName`, `description`, `uploadedAt` updated).
> - **Package upsert:** Match packages by the composite key `(workloadId, workloadVersion, packageId)`.
>   - If package exists: update `downloadUrl`, `hash`, `preInitSteps`, `postInitSteps`, `updateStrategy` — but **NOT** `status`.
>   - If package is new: insert with `status = PENDING`.
>   - Packages in the DB but **not** in the manifest: mark as `REMOVED` (soft delete).
> - **Note:** `WorkloadPackage.WorkloadVersion` is part of the composite key `(WorkloadId, WorkloadVersion, PackageId)`, not just `(WorkloadId, PackageId)`.

### Code Example — Workload Manifest Model

```csharp
// Models/WorkloadManifest.cs
using System.Text.Json.Serialization;

public class WorkloadManifest
{
    [Required] public string WorkloadId { get; set; } = string.Empty;
    [Required] public string WorkloadName { get; set; } = string.Empty;
    [Required]
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
    [Required] public List<WorkloadPackageManifest> Packages { get; set; } = [];
}

public class WorkloadPackageManifest
{
    [Required] public string PackageId { get; set; } = string.Empty;
    [Required]
    [JsonPropertyName("version")]
    public string PackageVersion { get; set; } = string.Empty;
    public List<string> PreInitSteps { get; set; } = [];
    public List<string> PostInitSteps { get; set; } = [];
}
```

### Code Example — Controller

```csharp
// Controllers/WorkloadsController.cs
[ApiController]
[Route("api/workloads")]
public class WorkloadsController : ControllerBase
{
    private readonly IWorkloadService _workloadService;

    public WorkloadsController(IWorkloadService workloadService)
    {
        _workloadService = workloadService;
    }

    [HttpPost]
    public async Task<ActionResult<WorkloadUploadResponse>> Upload()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        var isBulk = body.TrimStart().StartsWith("[");
        var result = isBulk
            ? await _workloadService.UploadBulkAsync(body)
            : await _workloadService.UploadSingleAsync(body);

        return Ok(result);
    }
}
```

### Acceptance Criteria

- [ ] `POST /api/workloads` with single workload object → 200, stored in DB and on disk
- [ ] `POST /api/workloads` with array of workload objects → 200, all stored
- [ ] Auto-detection of single vs. array format works correctly
- [ ] Upsert: same `workloadId + version` → updates changed fields, preserves unchanged fields
- [ ] Invalid workload JSON (missing required fields) → 400 with error details
- [ ] Workload definition JSON stored on disk at `{BasePath}/workload-definitions/{workloadId}/{version}.json`
- [ ] `WorkloadPackages` records created/updated with `preInitSteps`/`postInitSteps` as JSON arrays
- [ ] Response includes `imported`, `updated`, and `failed` arrays

### Verification Steps

1. POST a single workload JSON → 200, check DB `Workloads` and `WorkloadPackages` tables
2. POST a bulk array of 2 workloads → 200, both stored, summary shows 2 imported
3. POST same workload (same `workloadId + version`) with changed `workloadName` → updated in DB, original fields preserved
4. POST workload missing `workloadId` → 400 with validation error
5. POST workload referencing `packageId + version` not yet in Artifacts → stored anyway (soft check, warning logged)
6. Verify workload definition JSON file on disk matches the uploaded content

---

## TICKET P1-012: Test Data Scripts (PowerShell + Bash) + Manifests

**MVP Plan Ref:** Section 6.1 (Package Manifest JSON), Section 6.2 (Workload Definition JSON), Section 6.3 (Bulk Artifact Import)  
**Depends on:** None (can run in parallel with P1-005+)

### Description

Create download and packaging scripts (PowerShell + Bash) that:
1. Download actual installer media (.exe/.msi) from the internet into `.artifact-cache/`
2. Generate manifest JSON files for each package
3. Generate workload definition JSON files
4. Package pairs into `artifacts-older.zip` and `artifacts-newer.zip` for bulk import testing

The "older" set: DBeaver v24, Python v3.13, SSMS 2019. The "newer" set: DBeaver v26, Python v3.14, SSMS latest.

### Tasks

- [ ] Create `scripts/download-artifacts.ps1` (PowerShell) that:
  1. Creates `.artifact-cache/` directory
  2. Downloads DBeaver CE v24.x installer
  3. Downloads DBeaver CE v26.x installer
  4. Downloads Python v3.13.x installer
  5. Downloads Python v3.14.x installer (or latest 3.13 if 3.14 not available)
  6. Downloads SSMS 2019 installer
  7. Downloads SSMS latest installer
  8. Generates matching manifest JSON files for each installer
  9. Creates `artifacts-older.zip` (flat: DBeaver v24 + Python 3.13 + SSMS 2019, with matching .json manifests)
  10. Creates `artifacts-newer.zip` (flat: DBeaver v26 + Python 3.14 + SSMS latest, with matching .json manifests)
  11. Places ZIP files in `dist/imports/`
- [ ] Create `scripts/download-artifacts.sh` (Bash) — equivalent logic for Linux/macOS development environments
- [ ] Create manifest JSON templates for each package with correct detection rules:
  - DBeaver CE: `registry` detection (Uninstall key + DisplayVersion)
  - Python: `filePath` detection (python.exe path + FileVersion)
  - SSMS: `registry` detection (Uninstall key + DisplayVersion)
- [ ] Create workload definition JSON files:
  - `dbms-workload-v1.json` (older set: DBeaver v24, Python 3.13, SSMS 2019)
  - `dbms-workload-v2.json` (newer set: DBeaver v26, Python 3.14, SSMS latest)

### Code Example — DBeaver Manifest (v24)

```json
{
  "packageId": "dbeaver-ce",
  "packageName": "DBeaver Community Edition",
  "version": "24.0.0",
  "installerFile": "dbeaver-ce-24.0.0-x86_64-setup.exe",
  "installCommand": "dbeaver-ce-24.0.0-x86_64-setup.exe",
  "installArgs": "/S",
  "uninstallCommand": "MsiExec.exe",
  "uninstallArgs": "/X{CBE8F722-17D3-4D36-959C-240EB607719C} /qn",
  "updateStrategy": "overinstall",
  "detection": {
    "type": "registry",
    "key": "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DBeaver Community_is1",
    "valueName": "DisplayVersion",
    "expectedValue": "24.0.0"
  }
}
```

### Code Example — Python Manifest (v3.13)

```json
{
  "packageId": "python",
  "packageName": "Python",
  "version": "3.13.0",
  "installerFile": "python-3.13.0-amd64.exe",
  "installCommand": "python-3.13.0-amd64.exe",
  "installArgs": "/quiet InstallAllUsers=1 PrependPath=1",
  "uninstallCommand": "python-3.13.0-amd64.exe",
  "uninstallArgs": "/quiet UninstallAllUsers=1",
  "updateStrategy": "overinstall",
  "detection": {
    "type": "filePath",
    "key": "C:\\Program Files\\Python313\\python.exe",
    "valueName": "FileVersion",
    "expectedValue": "3.13.0"
  }
}
```

### Code Example — Workload Definition (dbms-workload v1.0)

```json
{
  "workloadId": "dbms-workload",
  "workloadName": "DBMS Workload",
  "version": "1.0",
  "packages": [
    {
      "packageId": "dbeaver-ce",
      "version": "24.0.0",
      "preInitSteps": [],
      "postInitSteps": [
        "mkdir \"C:\\ProgramData\\DBeaverData\""
      ]
    },
    {
      "packageId": "python",
      "version": "3.13.0",
      "preInitSteps": [],
      "postInitSteps": []
    },
    {
      "packageId": "ssms-2019",
      "version": "15.0.18390.0",
      "preInitSteps": [
        "net stop SQLBrowser"
      ],
      "postInitSteps": [
        "net start SQLBrowser"
      ]
    }
  ]
}
```

### Acceptance Criteria

- [ ] `scripts/download-artifacts.ps1` downloads all 6 installer binaries to `.artifact-cache/`
- [ ] `scripts/download-artifacts.sh` downloads all 6 installer binaries (or gracefully skips Windows-only installers on non-Windows)
- [ ] Each manifest JSON has correct detection rules (registry for DBeaver/SSMS, filePath for Python)
- [ ] Each manifest's `installerFile` field matches the actual binary filename exactly
- [ ] `artifacts-older.zip` is a flat archive (no subdirectories) with 3 pairs (3 binaries + 3 JSONs)
- [ ] `artifacts-newer.zip` is a flat archive with 3 pairs
- [ ] Both ZIP files can be successfully imported via `POST /api/artifacts/bulk`
- [ ] Workload definition JSON files are valid and uploadable via `POST /api/workloads`
- [ ] Manifest filenames match binary filenames by stem (e.g., `dbeaver-ce-24.0.0-x86_64-setup.exe` ↔ `dbeaver-ce-24.0.0-x86_64-setup.json`)

### Verification Steps

1. Run `./scripts/download-artifacts.ps1` — all installers downloaded to `.artifact-cache/`
2. Verify `.artifact-cache/` contains 6 .exe/.msi files + 6 .json manifest files
3. Verify `dist/imports/artifacts-older.zip` exists and is a valid ZIP
4. Verify `dist/imports/artifacts-newer.zip` exists and is a valid ZIP
5. Extract `artifacts-older.zip` — verify flat structure (no subdirectories), 6 files total (3 binaries + 3 manifests)
6. For each pair: verify manifest filename stem matches binary filename stem
7. Start Orchestrator, upload `artifacts-older.zip` via bulk import → all 3 imported successfully
8. Upload `dbms-workload-v1.json` → workload stored in DB
9. Run Bash equivalent script and verify same results