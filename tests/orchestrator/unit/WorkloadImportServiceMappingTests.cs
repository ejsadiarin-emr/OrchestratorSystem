using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests;

public class PackageAssignmentSlugTests
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
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public void MapToPackageAssignments_UsesManifestSlug_NotGuid()
    {
        var manifest = new ResolvedManifest
        {
            PackageId = "emerson-uaf-runtime",
            Version = "1.0.0",
            Channel = "stable",
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
                Path = "emerson-uaf-runtime"
            },
            OriginMetadata = new OriginMetadata
            {
                Source = "internal-upload",
                Publisher = "unknown",
                IngestedBy = "test",
                VerificationResult = "pass"
            },
            PolicyTags = new PolicyTags
            {
                RiskLevel = "medium",
                RetryabilityClass = "transient_only",
                IdempotencyMode = "version_check"
            },
            Sources = new ResolvedManifestSources()
        };

        var assignments = _service.MapToPackageAssignments(manifest);

        Assert.That(assignments, Has.Count.EqualTo(1));
        Assert.That(assignments[0].PackageId, Is.EqualTo("emerson-uaf-runtime"),
            "PackageId should be the human-readable slug, not a GUID");
    }
}