using System.Text.Json;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class PolicyEvaluationServiceTests
{
    private InstallerDbContext _db = null!;
    private SqliteConnection _connection = null!;
    private ArtifactStoreService _artifactStore = null!;
    private string _artifactRoot = null!;

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

        _artifactRoot = Path.Combine(Path.GetTempPath(), $"policy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_artifactRoot);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactStore:RootPath"] = _artifactRoot
            })
            .Build();
        _artifactStore = new ArtifactStoreService(config);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }
    }

    private async Task<Guid> SeedWorkloadAsync()
    {
        var workload = TestSeedBuilder.CreateWorkload(name: "TestWorkload");
        _db.WorkloadDefinitions.Add(workload);
        await _db.SaveChangesAsync();
        return workload.WorkloadId;
    }

    [Test]
    public async Task EvaluateRunRiskAsync_ReturnsLow_WhenRevisionHasNoPackages()
    {
        var workloadId = await SeedWorkloadAsync();
        var revision = TestSeedBuilder.CreateRevision(workloadId: workloadId);
        _db.WorkloadRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var service = new PolicyEvaluationService(_artifactStore);
        var result = await service.EvaluateRunRiskAsync(revision.RevisionId, _db);

        Assert.That(result, Is.EqualTo("low"));
    }

    [Test]
    public async Task EvaluateRunRiskAsync_ReturnsLow_WhenPackageHasNoManifest()
    {
        var workloadId = await SeedWorkloadAsync();
        var package = TestSeedBuilder.CreatePackage(
            name: "git", version: "2.47.1", sourcePath: "git.exe", installType: "exe");
        _db.Packages.Add(package);
        var revision = TestSeedBuilder.CreateRevision(workloadId: workloadId);
        revision.Packages = new List<WorkloadPackageEntity>
        {
            TestSeedBuilder.CreateWorkloadPackage(revisionId: revision.RevisionId, packageId: package.PackageId, packageIndex: 0)
        };
        _db.WorkloadRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var service = new PolicyEvaluationService(_artifactStore);
        var result = await service.EvaluateRunRiskAsync(revision.RevisionId, _db);

        Assert.That(result, Is.EqualTo("low"));
    }

    [Test]
    public async Task EvaluateRunRiskAsync_ReturnsHigh_WhenPackageManifestHasHighRisk()
    {
        var workloadId = await SeedWorkloadAsync();
        var package = TestSeedBuilder.CreatePackage(
            name: "critical-app", version: "1.0.0", sourcePath: "app.msi", installType: "msi");
        _db.Packages.Add(package);

        var manifestDir = Path.Combine(_artifactRoot, "critical-app", "1.0.0");
        Directory.CreateDirectory(manifestDir);
        var manifest = new ResolvedManifest
        {
            PackageId = package.PackageId.ToString(),
            Version = "1.0.0",
            PolicyTags = new PolicyTags
            {
                RiskLevel = "high",
                RetryabilityClass = "retryable",
                IdempotencyMode = "idempotent",
                ApprovalRequired = true
            }
        };
        await File.WriteAllTextAsync(Path.Combine(manifestDir, "resolved-manifest.json"),
            JsonSerializer.Serialize(manifest));

        var revision = TestSeedBuilder.CreateRevision(workloadId: workloadId);
        revision.Packages = new List<WorkloadPackageEntity>
        {
            TestSeedBuilder.CreateWorkloadPackage(revisionId: revision.RevisionId, packageId: package.PackageId, packageIndex: 0)
        };
        _db.WorkloadRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var service = new PolicyEvaluationService(_artifactStore);
        var result = await service.EvaluateRunRiskAsync(revision.RevisionId, _db);

        Assert.That(result, Is.EqualTo("high"));
    }

    [Test]
    public async Task EvaluateRunRiskAsync_ReturnsMedium_WhenMaxRiskIsMedium()
    {
        var workloadId = await SeedWorkloadAsync();
        var package = TestSeedBuilder.CreatePackage(
            name: "medium-app", version: "1.0.0", sourcePath: "app.exe", installType: "exe");
        _db.Packages.Add(package);

        var manifestDir = Path.Combine(_artifactRoot, "medium-app", "1.0.0");
        Directory.CreateDirectory(manifestDir);
        var manifest = new ResolvedManifest
        {
            PackageId = package.PackageId.ToString(),
            Version = "1.0.0",
            PolicyTags = new PolicyTags
            {
                RiskLevel = "medium",
                RetryabilityClass = "retryable",
                IdempotencyMode = "idempotent",
                ApprovalRequired = false
            }
        };
        await File.WriteAllTextAsync(Path.Combine(manifestDir, "resolved-manifest.json"),
            JsonSerializer.Serialize(manifest));

        var revision = TestSeedBuilder.CreateRevision(workloadId: workloadId);
        revision.Packages = new List<WorkloadPackageEntity>
        {
            TestSeedBuilder.CreateWorkloadPackage(revisionId: revision.RevisionId, packageId: package.PackageId, packageIndex: 0)
        };
        _db.WorkloadRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var service = new PolicyEvaluationService(_artifactStore);
        var result = await service.EvaluateRunRiskAsync(revision.RevisionId, _db);

        Assert.That(result, Is.EqualTo("medium"));
    }

    [Test]
    public async Task EvaluateRunRiskAsync_ReturnsLow_WhenPackageManifestHasLowRisk()
    {
        var workloadId = await SeedWorkloadAsync();
        var package = TestSeedBuilder.CreatePackage(
            name: "low-risk-app", version: "1.0.0", sourcePath: "app.exe", installType: "exe");
        _db.Packages.Add(package);

        var manifestDir = Path.Combine(_artifactRoot, "low-risk-app", "1.0.0");
        Directory.CreateDirectory(manifestDir);
        var manifest = new ResolvedManifest
        {
            PackageId = package.PackageId.ToString(),
            Version = "1.0.0",
            PolicyTags = new PolicyTags
            {
                RiskLevel = "low",
                RetryabilityClass = "retryable",
                IdempotencyMode = "idempotent",
                ApprovalRequired = false
            }
        };
        await File.WriteAllTextAsync(Path.Combine(manifestDir, "resolved-manifest.json"),
            JsonSerializer.Serialize(manifest));

        var revision = TestSeedBuilder.CreateRevision(workloadId: workloadId);
        revision.Packages = new List<WorkloadPackageEntity>
        {
            TestSeedBuilder.CreateWorkloadPackage(revisionId: revision.RevisionId, packageId: package.PackageId, packageIndex: 0)
        };
        _db.WorkloadRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var service = new PolicyEvaluationService(_artifactStore);
        var result = await service.EvaluateRunRiskAsync(revision.RevisionId, _db);

        Assert.That(result, Is.EqualTo("low"));
    }

    [Test]
    public async Task EvaluateRunRiskAsync_HighTakesPrecedence_OverMedium()
    {
        var workloadId = await SeedWorkloadAsync();
        var highPackage = TestSeedBuilder.CreatePackage(
            name: "high-risk", version: "1.0.0", sourcePath: "high.msi", installType: "msi");
        var mediumPackage = TestSeedBuilder.CreatePackage(
            name: "medium-risk", version: "1.0.0", sourcePath: "med.msi", installType: "msi");
        _db.Packages.AddRange(highPackage, mediumPackage);

        foreach (var (pkg, risk) in new[] { (highPackage, "high"), (mediumPackage, "medium") })
        {
            var manifestDir = Path.Combine(_artifactRoot, pkg.Name, pkg.Version);
            Directory.CreateDirectory(manifestDir);
            var manifest = new ResolvedManifest
            {
                PackageId = pkg.PackageId.ToString(),
                Version = pkg.Version,
                PolicyTags = new PolicyTags { RiskLevel = risk }
            };
            await File.WriteAllTextAsync(Path.Combine(manifestDir, "resolved-manifest.json"),
                JsonSerializer.Serialize(manifest));
        }

        var revision = TestSeedBuilder.CreateRevision(workloadId: workloadId);
        revision.Packages = new List<WorkloadPackageEntity>
        {
            TestSeedBuilder.CreateWorkloadPackage(revisionId: revision.RevisionId, packageId: highPackage.PackageId, packageIndex: 0),
            TestSeedBuilder.CreateWorkloadPackage(revisionId: revision.RevisionId, packageId: mediumPackage.PackageId, packageIndex: 1)
        };
        _db.WorkloadRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var service = new PolicyEvaluationService(_artifactStore);
        var result = await service.EvaluateRunRiskAsync(revision.RevisionId, _db);

        Assert.That(result, Is.EqualTo("high"));
    }
}
