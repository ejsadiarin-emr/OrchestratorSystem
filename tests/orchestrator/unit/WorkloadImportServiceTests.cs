using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests;

public sealed class WorkloadImportServiceTests : IDisposable
{
    private SqliteConnection _connection = null!;
    private InstallerDbContext _db = null!;
    private WorkloadImportService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new InstallerDbContext(options);
        _db.Database.EnsureCreated();
        _service = new WorkloadImportService(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _db?.Dispose();
    }

    [Test]
    public void MapToPackageAssignments_ProducesCorrectAssignments_FromResolvedManifest()
    {
        var manifest = CreateSampleResolvedManifest(packageId: "pkg-sqlserver", version: "2022.1.0", channel: "stable");

        var assignments = _service.MapToPackageAssignments(manifest);

        Assert.That(assignments, Has.Count.EqualTo(1));
        Assert.That(assignments[0].PackageId, Is.EqualTo("pkg-sqlserver"));
        Assert.That(assignments[0].Version, Is.EqualTo("2022.1.0"));
        Assert.That(assignments[0].Channel, Is.EqualTo("stable"));
        Assert.That(assignments[0].PackageIndex, Is.EqualTo(0));
    }

    [Test]
    public void MapToPackageAssignments_MapsInstallAdapter_FromResolvedManifest()
    {
        var manifest = CreateSampleResolvedManifest();
        manifest.InstallAdapter = new InstallAdapter
        {
            Type = "msi",
            Command = "setup.msi",
            Arguments = "/qn /norestart",
            ExpectedExitCodes = [0, 3010],
            TimeoutSeconds = 1800
        };

        var assignments = _service.MapToPackageAssignments(manifest);

        Assert.That(assignments[0].InstallAdapter.Type, Is.EqualTo("msi"));
        Assert.That(assignments[0].InstallAdapter.Command, Is.EqualTo("setup.msi"));
        Assert.That(assignments[0].InstallAdapter.Arguments, Is.EqualTo("/qn /norestart"));
        Assert.That(assignments[0].InstallAdapter.ExpectedExitCodes, Is.EqualTo(new List<int> { 0, 3010 }));
        Assert.That(assignments[0].InstallAdapter.TimeoutSeconds, Is.EqualTo(1800));
    }

    [Test]
    public void MapToPackageAssignments_MapsDetection_FromResolvedManifest()
    {
        var manifest = CreateSampleResolvedManifest();
        manifest.Detection = new Detection
        {
            Type = "version_manifest",
            Path = "pkg-sqlserver"
        };

        var assignments = _service.MapToPackageAssignments(manifest);

        Assert.That(assignments[0].Detection.Type, Is.EqualTo("version_manifest"));
        Assert.That(assignments[0].Detection.Path, Is.EqualTo("pkg-sqlserver"));
    }

    [Test]
    public async Task EnsurePackageEntitiesAsync_CreatesPackageEntity_FromResolvedManifest()
    {
        var manifest = CreateSampleResolvedManifest(packageId: "pkg-oracle", version: "19.3.0", channel: "canary");
        manifest.InstallAdapter = new InstallAdapter
        {
            Type = "exe",
            Command = "oracle-setup.exe",
            Arguments = "/quiet",
            ExpectedExitCodes = [0],
            TimeoutSeconds = 3600
        };

        var packageIds = await _service.EnsurePackageEntitiesAsync(manifest);

        Assert.That(packageIds, Has.Count.EqualTo(1));
        var saved = await _db.Packages.SingleAsync(p => p.PackageId == packageIds[0]);
        Assert.That(saved.Name, Is.EqualTo("pkg-oracle"));
        Assert.That(saved.Version, Is.EqualTo("19.3.0"));
        Assert.That(saved.InstallType, Is.EqualTo("exe"));
        Assert.That(saved.InstallArgs, Is.EqualTo("/quiet"));
    }

    [Test]
    public async Task EnsurePackageEntitiesAsync_ReusesExistingPackage_WhenAlreadyPresent()
    {
        var manifest = CreateSampleResolvedManifest(packageId: "pkg-redis", version: "7.2.0");
        var existingId = Guid.NewGuid();
        _db.Packages.Add(new PackageEntity
        {
            PackageId = existingId,
            Name = "pkg-redis",
            Version = "7.2.0",
            SourcePath = "pkg-redis/7.2.0/artifact.bin",
            InstallType = "msi",
            InstallArgs = "/qn",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var packageIds = await _service.EnsurePackageEntitiesAsync(manifest);

        Assert.That(packageIds, Has.Count.EqualTo(1));
        Assert.That(packageIds[0], Is.EqualTo(existingId));
        Assert.That(await _db.Packages.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateWorkloadPackageEntitiesAsync_CreatesEntities_WhenRevisionExists()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "TestWorkload",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0.0",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var entries = await _service.CreateWorkloadPackageEntitiesAsync(
            revisionId,
            new List<(Guid PackageId, int Index)> { (packageId, 0) });

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].PackageId, Is.EqualTo(packageId));
        Assert.That(entries[0].PackageIndex, Is.EqualTo(0));
        Assert.That(entries[0].RevisionId, Is.EqualTo(revisionId));
    }

    private static ResolvedManifest CreateSampleResolvedManifest(
        string packageId = "test-pkg",
        string version = "1.0.0",
        string channel = "stable")
    {
        return new ResolvedManifest
        {
            PackageId = packageId,
            Version = version,
            Channel = channel,
            ArtifactType = "msi",
            InstallAdapter = new InstallAdapter
            {
                Type = "msi",
                Command = "artifact.bin",
                Arguments = "/qn /norestart",
                ExpectedExitCodes = [0, 3010],
                TimeoutSeconds = 1800
            },
            Detection = new Detection
            {
                Type = "version_manifest",
                Path = packageId
            },
            OriginMetadata = new OriginMetadata
            {
                Source = "internal-upload",
                Publisher = "unknown",
                IngestedBy = "test-user",
                IngestedAtUtc = DateTime.UtcNow,
                VerificationResult = "pass"
            },
            PolicyTags = new PolicyTags
            {
                RetryabilityClass = "transient_only",
                IdempotencyMode = "version_check",
                RiskLevel = "medium",
                ApprovalRequired = false
            }
        };
    }

    [Test]
    public async Task Dispatch_PopulatesInitStepsFromEntityJsonColumns()
    {
        var workloadId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var packageEntityId = Guid.NewGuid();
        var workloadPackageId = Guid.NewGuid();

        _db.WorkloadDefinitions.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = "TestWorkload",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _db.WorkloadRevisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = "1.0.0",
            IsPublished = true,
            CreatedAtUtc = DateTime.UtcNow,
            PreWorkloadStepsJson = "[\"echo pre-wl\"]",
            PostWorkloadStepsJson = "[\"echo post-wl\"]",
            PreUninstallStepsJson = "[\"echo pre-uninstall\"]",
            PostUninstallStepsJson = "[\"echo post-uninstall\"]",
            DefaultShell = "pwsh"
        });

        _db.Packages.Add(new PackageEntity
        {
            PackageId = packageEntityId,
            Name = "test-pkg",
            Version = "1.0.0",
            SourcePath = "test.bin",
            InstallType = "exe",
            CreatedAtUtc = DateTime.UtcNow
        });

        _db.WorkloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = workloadPackageId,
            RevisionId = revisionId,
            PackageId = packageEntityId,
            PackageIndex = 1,
            PreInitStepsJson = "[\"echo pre-init\"]",
            PostInitStepsJson = "[\"echo post-init\"]"
        });
        await _db.SaveChangesAsync();

        var revision = await _db.WorkloadRevisions
            .Include(r => r.Packages)
            .FirstAsync(r => r.RevisionId == revisionId);

        Assert.That(revision.PreWorkloadStepsJson, Is.EqualTo("[\"echo pre-wl\"]"));
        Assert.That(revision.PostWorkloadStepsJson, Is.EqualTo("[\"echo post-wl\"]"));
        Assert.That(revision.PreUninstallStepsJson, Is.EqualTo("[\"echo pre-uninstall\"]"));
        Assert.That(revision.PostUninstallStepsJson, Is.EqualTo("[\"echo post-uninstall\"]"));
        Assert.That(revision.DefaultShell, Is.EqualTo("pwsh"));
        Assert.That(revision.Packages.First().PreInitStepsJson, Is.EqualTo("[\"echo pre-init\"]"));
        Assert.That(revision.Packages.First().PostInitStepsJson, Is.EqualTo("[\"echo post-init\"]"));
    }

    [Test]
    public void Dispatch_DeserializeInitSteps_HandlesEmptyAndNull()
    {
        var result1 = WorkloadRunDispatcher.DeserializeStringList(null);
        Assert.That(result1, Is.Empty);

        var result2 = WorkloadRunDispatcher.DeserializeStringList("");
        Assert.That(result2, Is.Empty);

        var result3 = WorkloadRunDispatcher.DeserializeStringList("[]");
        Assert.That(result3, Is.Empty);

        var result4 = WorkloadRunDispatcher.DeserializeStringList("[\"cmd1\",\"cmd2\"]");
        Assert.That(result4, Is.EqualTo(new List<string> { "cmd1", "cmd2" }));
    }
}
