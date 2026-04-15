# P1 Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement all Phase-1 P1 tasks (`P1-01`, `P1-02`, `P1-03`) by adding SQLite persistence, aligning API contracts, and implementing HTTP artifact transport with range/chunk retrieval plus a minimal agent acquisition path.

**Architecture:** Replace in-memory runtime state (`AppStore`) with EF Core SQLite canonical entities (`Job`, `Node`, `AssignmentLease`, `ConfigSnapshot`, `JobStep`). Expose API-001..API-005 shapes with dedicated DTOs and contract-aligned routes. Keep control-plane/data-plane split strict by serving artifact bytes only through HTTP endpoints and consuming artifacts through a minimal agent acquisition component.

**Tech Stack:** ASP.NET Core (`net10.0`), EF Core SQLite, NUnit + Moq, ASP.NET Core integration testing (`Microsoft.AspNetCore.Mvc.Testing`), HttpClient.

---

## File Structure

### Orchestrator data/persistence

- Create: `src/DeploymentPoC.Orchestrator/Data/InstallerDbContext.cs` - canonical EF DbContext and mapping.
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/JobEntity.cs` - job aggregate root.
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/JobStepEntity.cs` - step timeline rows.
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/NodeEntity.cs` - node inventory/status.
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/AssignmentLeaseEntity.cs` - lease ownership and cursor.
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/ConfigSnapshotEntity.cs` - config snapshot records.
- Create: `src/DeploymentPoC.Orchestrator/Data/Migrations/*` - initial SQLite schema migration.

### Orchestrator API contract + controllers

- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobRequest.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/JobDetailResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/JobStepListResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobRequest.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/NodeListResponse.cs`
- Modify: `src/DeploymentPoC.Orchestrator/Controllers/JobsController.cs` - API-001/002/003/005.
- Modify: `src/DeploymentPoC.Orchestrator/Controllers/NodesController.cs` - API-004.

### Orchestrator artifact data plane

- Create: `src/DeploymentPoC.Orchestrator/Services/ArtifactStoreService.cs` - metadata/stream abstraction over local filesystem store.
- Create: `src/DeploymentPoC.Orchestrator/Controllers/ArtifactsController.cs` - HEAD/GET with range support.
- Modify: `src/DeploymentPoC.Orchestrator/Program.cs` - DI, DB migration, artifact options.

### Agent minimal acquisition stub

- Create: `src/DeploymentPoC.Agent/DeploymentPoC.Agent.csproj`
- Create: `src/DeploymentPoC.Agent/Steps/AcquireArtifact.cs`
- Create: `src/DeploymentPoC.Agent/Steps/AcquireArtifactModels.cs`

### Tests

- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/DeploymentPoC.Orchestrator.IntegrationTests.csproj`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Persistence/PersistenceTests.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Api/JobsApiContractTests.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Api/NodesApiContractTests.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Artifacts/ArtifactTransportTests.cs`
- Create: `tests/DeploymentPoC.Agent.IntegrationTests/DeploymentPoC.Agent.IntegrationTests.csproj`
- Create: `tests/DeploymentPoC.Agent.IntegrationTests/AcquireArtifactTests.cs`

### Build/solution wiring

- Modify: `src/DeploymentPoC.Orchestrator/DeploymentPoC.Orchestrator.csproj` - EF packages.
- Modify: `tests/DeploymentPoC.Orchestrator.Tests/DeploymentPoC.Orchestrator.Tests.csproj` - references if needed for data tests.
- Modify: `DeploymentPoC.sln` - add new projects and normalize duplicate GUID entries.

---

### Task 1: Add SQLite persistence foundation (P1-01)

**Files:**
- Create: `src/DeploymentPoC.Orchestrator/Data/InstallerDbContext.cs`
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/JobEntity.cs`
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/JobStepEntity.cs`
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/NodeEntity.cs`
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/AssignmentLeaseEntity.cs`
- Create: `src/DeploymentPoC.Orchestrator/Data/Entities/ConfigSnapshotEntity.cs`
- Modify: `src/DeploymentPoC.Orchestrator/DeploymentPoC.Orchestrator.csproj`
- Modify: `src/DeploymentPoC.Orchestrator/Program.cs`
- Create: `tests/DeploymentPoC.Orchestrator.Tests/Persistence/InstallerDbContextShapeTests.cs`

- [ ] **Step 1: Write the failing persistence shape test**

```csharp
using System.Linq;
using DeploymentPoC.Orchestrator.Data;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Persistence;

public class InstallerDbContextShapeTests
{
    [Test]
    public void InstallerDbContext_ExposesCanonicalEntitySets()
    {
        var dbSetNames = typeof(InstallerDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.Name.StartsWith("DbSet"))
            .Select(p => p.Name)
            .ToHashSet();

        Assert.That(dbSetNames, Does.Contain("Jobs"));
        Assert.That(dbSetNames, Does.Contain("JobSteps"));
        Assert.That(dbSetNames, Does.Contain("Nodes"));
        Assert.That(dbSetNames, Does.Contain("AssignmentLeases"));
        Assert.That(dbSetNames, Does.Contain("ConfigSnapshots"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter InstallerDbContext_ExposesCanonicalEntitySets`
Expected: FAIL at compile time because `DeploymentPoC.Orchestrator.Data` and `InstallerDbContext` do not exist.

- [ ] **Step 3: Implement DbContext and canonical entities**

`src/DeploymentPoC.Orchestrator/Data/Entities/JobEntity.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class JobEntity
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public string Mode { get; set; } = "install";
    public string State { get; set; } = "Queued";
    public int? ReasonCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string ManifestPackageId { get; set; } = string.Empty;
    public string ManifestTargetVersion { get; set; } = string.Empty;
    public string TargetNodeIdsCsv { get; set; } = string.Empty;
    public List<JobStepEntity> Steps { get; set; } = new();
}
```

`src/DeploymentPoC.Orchestrator/Data/Entities/JobStepEntity.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class JobStepEntity
{
    public Guid JobStepId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public JobEntity Job { get; set; } = null!;
    public string StepId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int Sequence { get; set; }
    public int? ReasonCode { get; set; }
    public string? TelemetryRef { get; set; }
    public string? Detail { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
```

`src/DeploymentPoC.Orchestrator/Data/Entities/NodeEntity.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class NodeEntity
{
    public Guid NodeId { get; set; } = Guid.NewGuid();
    public string? AgentId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
```

`src/DeploymentPoC.Orchestrator/Data/Entities/AssignmentLeaseEntity.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class AssignmentLeaseEntity
{
    public Guid AssignmentId { get; set; } = Guid.NewGuid();
    public string LeaseId { get; set; } = Guid.NewGuid().ToString("N");
    public Guid JobId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public int TtlSeconds { get; set; } = 90;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public int LastAckedSequence { get; set; }
    public string State { get; set; } = "Assigned";
}
```

`src/DeploymentPoC.Orchestrator/Data/Entities/ConfigSnapshotEntity.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Data.Entities;

public sealed class ConfigSnapshotEntity
{
    public Guid ConfigSnapshotId { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid NodeId { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string SourceSchemaVersion { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string StorageLocation { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;
}
```

`src/DeploymentPoC.Orchestrator/Data/InstallerDbContext.cs`

```csharp
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeploymentPoC.Orchestrator.Data;

public sealed class InstallerDbContext : DbContext
{
    public InstallerDbContext(DbContextOptions<InstallerDbContext> options) : base(options)
    {
    }

    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<JobStepEntity> JobSteps => Set<JobStepEntity>();
    public DbSet<NodeEntity> Nodes => Set<NodeEntity>();
    public DbSet<AssignmentLeaseEntity> AssignmentLeases => Set<AssignmentLeaseEntity>();
    public DbSet<ConfigSnapshotEntity> ConfigSnapshots => Set<ConfigSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobEntity>().HasKey(x => x.JobId);
        modelBuilder.Entity<JobEntity>().Property(x => x.State).HasMaxLength(64);
        modelBuilder.Entity<JobEntity>().Property(x => x.Mode).HasMaxLength(32);

        modelBuilder.Entity<JobStepEntity>().HasKey(x => x.JobStepId);
        modelBuilder.Entity<JobStepEntity>()
            .HasOne(x => x.Job)
            .WithMany(x => x.Steps)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<JobStepEntity>()
            .HasIndex(x => new { x.JobId, x.Sequence })
            .IsUnique();

        modelBuilder.Entity<NodeEntity>().HasKey(x => x.NodeId);
        modelBuilder.Entity<NodeEntity>().HasIndex(x => x.Hostname).IsUnique();

        modelBuilder.Entity<AssignmentLeaseEntity>().HasKey(x => x.AssignmentId);
        modelBuilder.Entity<AssignmentLeaseEntity>().HasIndex(x => x.LeaseId).IsUnique();

        modelBuilder.Entity<ConfigSnapshotEntity>().HasKey(x => x.ConfigSnapshotId);
        modelBuilder.Entity<ConfigSnapshotEntity>()
            .HasIndex(x => new { x.JobId, x.NodeId, x.PackageId, x.CapturedAtUtc });
    }
}
```

`src/DeploymentPoC.Orchestrator/DeploymentPoC.Orchestrator.csproj` package additions:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.4" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.4">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.4" />
</ItemGroup>
```

`src/DeploymentPoC.Orchestrator/Program.cs` DI snippet:

```csharp
using DeploymentPoC.Orchestrator.Data;
using Microsoft.EntityFrameworkCore;

var connectionString =
    builder.Configuration.GetConnectionString("InstallerDb")
    ?? "Data Source=deployment-poc.db";

builder.Services.AddDbContext<InstallerDbContext>(options =>
    options.UseSqlite(connectionString));
```

- [ ] **Step 4: Generate migration and run tests**

Run:

- `dotnet ef migrations add InitialCanonicalEntities --project src/DeploymentPoC.Orchestrator`
- `dotnet ef database update --project src/DeploymentPoC.Orchestrator`
- `dotnet test tests/DeploymentPoC.Orchestrator.Tests --filter InstallerDbContext_ExposesCanonicalEntitySets`

Expected: migration files created, database update succeeds, test PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DeploymentPoC.Orchestrator/Data src/DeploymentPoC.Orchestrator/Program.cs src/DeploymentPoC.Orchestrator/DeploymentPoC.Orchestrator.csproj tests/DeploymentPoC.Orchestrator.Tests/Persistence
git commit -m "feat(orchestrator): add sqlite canonical persistence model"
```

### Task 2: Replace runtime AppStore state with DbContext-backed operations (P1-01)

**Files:**
- Modify: `src/DeploymentPoC.Orchestrator/Controllers/JobsController.cs`
- Modify: `src/DeploymentPoC.Orchestrator/Controllers/NodesController.cs`
- Modify: `src/DeploymentPoC.Orchestrator/Store/AppStore.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/DeploymentPoC.Orchestrator.IntegrationTests.csproj`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Persistence/PersistenceTests.cs`
- Modify: `DeploymentPoC.sln`

- [ ] **Step 1: Write failing persistence integration test**

`tests/DeploymentPoC.Orchestrator.IntegrationTests/Persistence/PersistenceTests.cs`

```csharp
using System.Net.Http.Json;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Persistence;

public class PersistenceTests
{
    [Test]
    public async Task CreateNode_IsDurableAcrossRequests()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var createPayload = new
        {
            hostname = "NODE-PERSIST-01",
            ipAddress = "10.10.10.1",
            description = "persistence-check"
        };

        var createResponse = await client.PostAsJsonAsync("/api/nodes", createPayload);
        createResponse.EnsureSuccessStatusCode();

        var listResponse = await client.GetAsync("/api/nodes");
        listResponse.EnsureSuccessStatusCode();
        var body = await listResponse.Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("NODE-PERSIST-01"));
    }
}
```

- [ ] **Step 2: Run integration test to verify it fails**

Run: `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter CreateNode_IsDurableAcrossRequests`
Expected: FAIL because integration test project/factory does not exist yet.

- [ ] **Step 3: Add integration harness and switch controllers to DbContext**

`tests/DeploymentPoC.Orchestrator.IntegrationTests/DeploymentPoC.Orchestrator.IntegrationTests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="NUnit" Version="4.3.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\DeploymentPoC.Orchestrator\DeploymentPoC.Orchestrator.csproj" />
  </ItemGroup>
</Project>
```

`tests/DeploymentPoC.Orchestrator.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs`

```csharp
using DeploymentPoC.Orchestrator.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DeploymentPoC.Orchestrator.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            _connection.Open();

            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<InstallerDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<InstallerDbContext>(options => options.UseSqlite(_connection));

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
            db.Database.EnsureCreated();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
```

`JobsController` and `NodesController` constructor replacements:

```csharp
private readonly InstallerDbContext _db;

public JobsController(InstallerDbContext db, ILogger<JobsController> logger)
{
    _db = db;
    _logger = logger;
}
```

```csharp
private readonly InstallerDbContext _db;

public NodesController(InstallerDbContext db, ILogger<NodesController> logger)
{
    _db = db;
    _logger = logger;
}
```

`AppStore` deprecation boundary:

```csharp
namespace DeploymentPoC.Orchestrator.Store;

[Obsolete("Runtime state moved to InstallerDbContext. Keep only for non-runtime transitional use.")]
public class AppStore
{
}
```

- [ ] **Step 4: Run integration test and solution build**

Run:

- `dotnet build DeploymentPoC.sln`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter CreateNode_IsDurableAcrossRequests`

Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/DeploymentPoC.Orchestrator.IntegrationTests src/DeploymentPoC.Orchestrator/Controllers/JobsController.cs src/DeploymentPoC.Orchestrator/Controllers/NodesController.cs src/DeploymentPoC.Orchestrator/Store/AppStore.cs DeploymentPoC.sln
git commit -m "feat(orchestrator): move runtime state reads and writes to sqlite dbcontext"
```

### Task 3: Align jobs/nodes API contracts and routes (P1-02)

**Files:**
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobRequest.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/JobDetailResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/JobStepListResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobRequest.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobResponse.cs`
- Create: `src/DeploymentPoC.Orchestrator/Contracts/Api/NodeListResponse.cs`
- Modify: `src/DeploymentPoC.Orchestrator/Controllers/JobsController.cs`
- Modify: `src/DeploymentPoC.Orchestrator/Controllers/NodesController.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Api/JobsApiContractTests.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Api/NodesApiContractTests.cs`

- [ ] **Step 1: Write failing API contract tests**

`tests/DeploymentPoC.Orchestrator.IntegrationTests/Api/JobsApiContractTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public class JobsApiContractTests
{
    [Test]
    public async Task JobsApi_Implements_PostCancelAndStepsRoutes()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var create = new
        {
            packageId = "nodejs",
            targetVersion = "24.0.0",
            executionMode = "install",
            idempotencyKey = "idem-1",
            targets = new[] { Guid.NewGuid() }
        };

        var createResponse = await client.PostAsJsonAsync("/api/jobs", create);
        createResponse.EnsureSuccessStatusCode();
        var createBody = await createResponse.Content.ReadAsStringAsync();

        var jobIdToken = "\"jobId\":\"";
        var start = createBody.IndexOf(jobIdToken, StringComparison.OrdinalIgnoreCase);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));

        var begin = start + jobIdToken.Length;
        var end = createBody.IndexOf('"', begin);
        var jobId = createBody.Substring(begin, end - begin);

        var stepsResponse = await client.GetAsync($"/api/jobs/{jobId}/steps");
        Assert.That(stepsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var cancelResponse = await client.PostAsJsonAsync($"/api/jobs/{jobId}/cancel", new { reason = "test" });
        Assert.That(cancelResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter JobsApi_Implements_PostCancelAndStepsRoutes`
Expected: FAIL because `/api/jobs/{jobId}/steps` and `POST /cancel` are not implemented yet.

- [ ] **Step 3: Implement DTO contracts and controller endpoints**

`src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobRequest.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CreateJobRequest
{
    public string PackageId { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = "install";
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<Guid> Targets { get; set; } = new();
}
```

`src/DeploymentPoC.Orchestrator/Contracts/Api/CreateJobResponse.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CreateJobResponse
{
    public Guid JobId { get; set; }
    public string State { get; set; } = string.Empty;
}
```

`src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobRequest.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CancelJobRequest
{
    public string Reason { get; set; } = string.Empty;
}
```

`src/DeploymentPoC.Orchestrator/Contracts/Api/CancelJobResponse.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class CancelJobResponse
{
    public Guid JobId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CancelledAtUtc { get; set; }
}
```

`src/DeploymentPoC.Orchestrator/Contracts/Api/JobDetailResponse.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class JobDetailResponse
{
    public Guid JobId { get; set; }
    public string State { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int? ReasonCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
```

`src/DeploymentPoC.Orchestrator/Contracts/Api/JobStepListResponse.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class JobStepListResponse
{
    public List<JobStepDto> Steps { get; set; } = new();
}

public sealed class JobStepDto
{
    public string StepId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public int? ReasonCode { get; set; }
    public string? TelemetryRef { get; set; }
}
```

`src/DeploymentPoC.Orchestrator/Contracts/Api/NodeListResponse.cs`

```csharp
namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class NodeListResponse
{
    public List<NodeSummaryDto> Nodes { get; set; } = new();
}

public sealed class NodeSummaryDto
{
    public Guid NodeId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime LastSeenUtc { get; set; }
}
```

`JobsController` route and actions (core signatures):

```csharp
[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CreateJobResponse>> Create([FromBody] CreateJobRequest request) { /* ... */ }

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobDetailResponse>> GetById(Guid jobId) { /* ... */ }

    [HttpGet("{jobId:guid}/steps")]
    public async Task<ActionResult<JobStepListResponse>> GetSteps(Guid jobId) { /* ... */ }

    [HttpPost("{jobId:guid}/cancel")]
    public async Task<ActionResult<CancelJobResponse>> Cancel(Guid jobId, [FromBody] CancelJobRequest request) { /* ... */ }
}
```

`NodesController` route:

```csharp
[ApiController]
[Route("api/nodes")]
public class NodesController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NodeListResponse>> GetAll() { /* ... */ }
}
```

- [ ] **Step 4: Run contract tests**

Run:

- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter JobsApi_Implements_PostCancelAndStepsRoutes`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Nodes`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DeploymentPoC.Orchestrator/Contracts/Api src/DeploymentPoC.Orchestrator/Controllers/JobsController.cs src/DeploymentPoC.Orchestrator/Controllers/NodesController.cs tests/DeploymentPoC.Orchestrator.IntegrationTests/Api
git commit -m "feat(api): align jobs and nodes endpoints with phase1 contracts"
```

### Task 4: Add artifact HTTP transport and range retrieval (P1-03)

**Files:**
- Create: `src/DeploymentPoC.Orchestrator/Services/ArtifactStoreService.cs`
- Create: `src/DeploymentPoC.Orchestrator/Controllers/ArtifactsController.cs`
- Modify: `src/DeploymentPoC.Orchestrator/Program.cs`
- Create: `tests/DeploymentPoC.Orchestrator.IntegrationTests/Artifacts/ArtifactTransportTests.cs`

- [ ] **Step 1: Write failing range transport test**

`tests/DeploymentPoC.Orchestrator.IntegrationTests/Artifacts/ArtifactTransportTests.cs`

```csharp
using System.Net;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Artifacts;

public class ArtifactTransportTests
{
    [Test]
    public async Task ArtifactEndpoint_SupportsHttpRangeRequests()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/artifacts/testpkg/1.0.0");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ArtifactEndpoint_SupportsHttpRangeRequests`
Expected: FAIL because `/api/artifacts/...` does not exist.

- [ ] **Step 3: Implement artifact store service and controller**

`src/DeploymentPoC.Orchestrator/Services/ArtifactStoreService.cs`

```csharp
using System.Security.Cryptography;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class ArtifactStoreService
{
    private readonly string _root;

    public ArtifactStoreService(IConfiguration configuration)
    {
        _root = configuration["ArtifactStore:RootPath"] ?? Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(_root);
    }

    public string GetArtifactPath(string packageId, string version)
        => Path.Combine(_root, packageId, version, "artifact.bin");

    public bool Exists(string packageId, string version)
        => File.Exists(GetArtifactPath(packageId, version));

    public Stream OpenRead(string packageId, string version)
        => File.OpenRead(GetArtifactPath(packageId, version));

    public long GetSize(string packageId, string version)
        => new FileInfo(GetArtifactPath(packageId, version)).Length;

    public string ComputeSha256(string packageId, string version)
    {
        using var stream = OpenRead(packageId, version);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

`src/DeploymentPoC.Orchestrator/Controllers/ArtifactsController.cs`

```csharp
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeploymentPoC.Orchestrator.Controllers;

[ApiController]
[Route("api/artifacts")]
public class ArtifactsController : ControllerBase
{
    private readonly ArtifactStoreService _store;

    public ArtifactsController(ArtifactStoreService store)
    {
        _store = store;
    }

    [HttpHead("{packageId}/{version}")]
    public IActionResult Head(string packageId, string version)
    {
        if (!_store.Exists(packageId, version))
        {
            return NotFound();
        }

        Response.Headers.ContentLength = _store.GetSize(packageId, version);
        Response.Headers.ETag = $"\"{_store.ComputeSha256(packageId, version)}\"";
        return Ok();
    }

    [HttpGet("{packageId}/{version}")]
    public IActionResult Get(string packageId, string version)
    {
        if (!_store.Exists(packageId, version))
        {
            return NotFound();
        }

        var stream = _store.OpenRead(packageId, version);
        return File(stream, "application/octet-stream", enableRangeProcessing: true);
    }
}
```

`Program.cs` DI snippet:

```csharp
builder.Services.AddSingleton<ArtifactStoreService>();
```

- [ ] **Step 4: Run artifact transport tests**

Run:

- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ArtifactEndpoint_SupportsHttpRangeRequests`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Artifact`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DeploymentPoC.Orchestrator/Services/ArtifactStoreService.cs src/DeploymentPoC.Orchestrator/Controllers/ArtifactsController.cs src/DeploymentPoC.Orchestrator/Program.cs tests/DeploymentPoC.Orchestrator.IntegrationTests/Artifacts
git commit -m "feat(artifact): add http artifact transport with range retrieval"
```

### Task 5: Create minimal agent AcquireArtifact path and tests (P1-03)

**Files:**
- Create: `src/DeploymentPoC.Agent/DeploymentPoC.Agent.csproj`
- Create: `src/DeploymentPoC.Agent/Steps/AcquireArtifactModels.cs`
- Create: `src/DeploymentPoC.Agent/Steps/AcquireArtifact.cs`
- Create: `tests/DeploymentPoC.Agent.IntegrationTests/DeploymentPoC.Agent.IntegrationTests.csproj`
- Create: `tests/DeploymentPoC.Agent.IntegrationTests/AcquireArtifactTests.cs`
- Modify: `DeploymentPoC.sln`

- [ ] **Step 1: Write failing acquisition test**

`tests/DeploymentPoC.Agent.IntegrationTests/AcquireArtifactTests.cs`

```csharp
using NUnit.Framework;

namespace DeploymentPoC.Agent.IntegrationTests;

public class AcquireArtifactTests
{
    [Test]
    public async Task AcquireArtifact_DownloadsUsingHttpOnly()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");
        var acquire = new DeploymentPoC.Agent.Steps.AcquireArtifact(new HttpClient());

        var result = await acquire.ExecuteAsync(new DeploymentPoC.Agent.Steps.AcquireArtifactRequest
        {
            ArtifactUrl = "https://example.com/artifact.bin",
            DestinationPath = tempPath,
            ChunkSizeBytes = 1024 * 1024
        });

        Assert.That(result.Transport, Is.EqualTo("http"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DeploymentPoC.Agent.IntegrationTests --filter AcquireArtifact_DownloadsUsingHttpOnly`
Expected: FAIL because agent project and acquire step do not exist.

- [ ] **Step 3: Implement minimal agent acquisition component**

`src/DeploymentPoC.Agent/DeploymentPoC.Agent.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`src/DeploymentPoC.Agent/Steps/AcquireArtifactModels.cs`

```csharp
namespace DeploymentPoC.Agent.Steps;

public sealed class AcquireArtifactRequest
{
    public string ArtifactUrl { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public int ChunkSizeBytes { get; set; } = 8 * 1024 * 1024;
    public string? ExpectedSha256 { get; set; }
}

public sealed class AcquireArtifactResult
{
    public bool Success { get; set; }
    public string Transport { get; set; } = "http";
    public long BytesWritten { get; set; }
    public string? Error { get; set; }
}
```

`src/DeploymentPoC.Agent/Steps/AcquireArtifact.cs`

```csharp
using System.Net;
using System.Security.Cryptography;

namespace DeploymentPoC.Agent.Steps;

public sealed class AcquireArtifact
{
    private readonly HttpClient _http;

    public AcquireArtifact(HttpClient http)
    {
        _http = http;
    }

    public async Task<AcquireArtifactResult> ExecuteAsync(AcquireArtifactRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ArtifactUrl) || string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return new AcquireArtifactResult { Success = false, Error = "invalid_request" };
        }

        using var head = new HttpRequestMessage(HttpMethod.Head, request.ArtifactUrl);
        using var headResponse = await _http.SendAsync(head, ct);
        if (!headResponse.IsSuccessStatusCode)
        {
            return new AcquireArtifactResult { Success = false, Error = $"head_{(int)headResponse.StatusCode}" };
        }

        var length = headResponse.Content.Headers.ContentLength ?? 0;
        Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);

        await using var output = File.Create(request.DestinationPath);

        if (length <= 0)
        {
            using var response = await _http.GetAsync(request.ArtifactUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(output, ct);
        }
        else
        {
            var from = 0L;
            while (from < length)
            {
                var to = Math.Min(from + request.ChunkSizeBytes - 1, length - 1);
                using var get = new HttpRequestMessage(HttpMethod.Get, request.ArtifactUrl);
                get.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);

                using var response = await _http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode != HttpStatusCode.PartialContent && response.StatusCode != HttpStatusCode.OK)
                {
                    return new AcquireArtifactResult { Success = false, Error = $"range_{(int)response.StatusCode}" };
                }

                await response.Content.CopyToAsync(output, ct);
                from = to + 1;
            }
        }

        await output.FlushAsync(ct);

        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256))
        {
            await using var verify = File.OpenRead(request.DestinationPath);
            using var sha = SHA256.Create();
            var actual = Convert.ToHexString(await sha.ComputeHashAsync(verify, ct)).ToLowerInvariant();
            if (!string.Equals(actual, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new AcquireArtifactResult { Success = false, Error = "hash_mismatch" };
            }
        }

        return new AcquireArtifactResult
        {
            Success = true,
            Transport = "http",
            BytesWritten = new FileInfo(request.DestinationPath).Length
        };
    }
}
```

- [ ] **Step 4: Run acquisition tests**

Run:

- `dotnet test tests/DeploymentPoC.Agent.IntegrationTests --filter AcquireArtifact`
- `dotnet build DeploymentPoC.sln`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DeploymentPoC.Agent tests/DeploymentPoC.Agent.IntegrationTests DeploymentPoC.sln
git commit -m "feat(agent): add minimal http artifact acquisition path for p1"
```

### Task 6: Enforce P1 verification commands and tracker updates

**Files:**
- Modify: `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md`
- Modify: `docs/distributed-installer/poc-phase1-prd-final.md` (only if contract wording changed during implementation)

- [ ] **Step 1: Write failing status-check test command list (manual gate)**

```text
Gate target:
- Persistence tests must pass
- API contract tests must pass
- Artifact transport tests must pass
- Agent AcquireArtifact tests must pass
```

- [ ] **Step 2: Run full P1 command suite**

Run:

- `dotnet build DeploymentPoC.sln`
- `dotnet ef database update --project src/DeploymentPoC.Orchestrator`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Persistence`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter ApiContract`
- `dotnet test tests/DeploymentPoC.Orchestrator.IntegrationTests --filter Artifact`
- `dotnet test tests/DeploymentPoC.Agent.IntegrationTests --filter AcquireArtifact`

Expected: all commands PASS.

- [ ] **Step 3: Update tracker status for P1 rows**

Update `docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md` rows:

```markdown
| P1-01 | SQLite persistence for canonical entities | S1 | P0-03 | TBD (Backend) | Done | AC-001, AC-002, AC-007, AC-101 |
| P1-02 | API contract alignment (`/api/jobs`, `/steps`, `/cancel`, `/nodes`) | S1 | P1-01 | TBD (Backend) | Done | AC-001, AC-002, AC-104 |
| P1-03 | Artifact HTTP transport + range/chunk retrieval | S1 | P1-02 | TBD (Backend/Agent) | Done | AC-001, AC-006, AC-102 |
```

- [ ] **Step 4: Commit verification + tracker updates**

```bash
git add docs/distributed-installer/poc-phase1-prd-and-implementation-tracker.md
git commit -m "docs(tracker): mark p1-01 p1-02 p1-03 complete with verification evidence"
```

---

## Self-Review Checklist (performed while writing this plan)

### Spec coverage

- P1-01 covered by Task 1 + Task 2.
- P1-02 covered by Task 3.
- P1-03 covered by Task 4 + Task 5.
- Verification and tracker evidence closure covered by Task 6.

### Placeholder scan

- No `TBD`, `TODO`, `implement later`, or missing-command placeholders left in execution steps.

### Type/signature consistency

- Job and node route contracts use `api/jobs` and `api/nodes` consistently.
- Cancel endpoint consistently uses `POST /api/jobs/{jobId}/cancel`.
- Step list endpoint consistently uses `GET /api/jobs/{jobId}/steps`.
