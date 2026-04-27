using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Contracts.Api.Workloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

[TestFixture]
public class WorkloadsControllerTests
{
    private InstallerDbContext _db = null!;
    private SqliteConnection _connection = null!;

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
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private WorkloadsController CreateController()
    {
        var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        configMock.Setup(c => c["ArtifactStore:RootPath"]).Returns(Path.Combine(Path.GetTempPath(), "test-artifacts"));
        var artifactStore = new ArtifactStoreService(configMock.Object);
        return new WorkloadsController(_db, artifactStore);
    }

    [Test]
    public async Task CreateRevision_ReturnsBadRequest_WhenPackagesIsEmpty()
    {
        var workload = new WorkloadDefinitionEntity
        {
            WorkloadId = Guid.NewGuid(),
            Name = "Test Workload",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.WorkloadDefinitions.Add(workload);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRevisionRequest
        {
            Version = "1.0.0",
            Packages = new List<WorkloadPackageInput>()
        };

        var result = await controller.CreateRevision(workload.WorkloadId, request);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result!;
        var response = badRequest.Value as ValidationErrorResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Errors.Any(e => e.Field == "packages" && e.Error.Contains("at least 1 package", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task CreateRevision_ReturnsCreated_WhenSinglePackage()
    {
        var workload = new WorkloadDefinitionEntity
        {
            WorkloadId = Guid.NewGuid(),
            Name = "Test Workload",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.WorkloadDefinitions.Add(workload);

        var package = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRevisionRequest
        {
            Version = "1.0.0",
            Packages = new List<WorkloadPackageInput>
            {
                new() { PackageId = package.PackageId, PackageIndex = 1 }
            }
        };

        var result = await controller.CreateRevision(workload.WorkloadId, request);

        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        var created = (CreatedResult)result.Result!;
        var dto = created.Value as WorkloadRevisionDto;
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Packages.Count, Is.EqualTo(1));
        Assert.That(dto.Packages[0].PackageName, Is.EqualTo("git"));
    }

    [Test]
    public async Task CreateRevision_ReturnsCreated_WhenFourPackages()
    {
        var workload = new WorkloadDefinitionEntity
        {
            WorkloadId = Guid.NewGuid(),
            Name = "Test Workload",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.WorkloadDefinitions.Add(workload);

        var packages = new List<PackageEntity>();
        for (int i = 0; i < 4; i++)
        {
            packages.Add(new PackageEntity
            {
                PackageId = Guid.NewGuid(),
                Name = $"pkg-{i}",
                Version = "1.0.0",
                SourcePath = "test.bin",
                InstallType = "exe",
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        _db.Packages.AddRange(packages);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new CreateWorkloadRevisionRequest
        {
            Version = "1.0.0",
            Packages = packages.Select((p, i) => new WorkloadPackageInput { PackageId = p.PackageId, PackageIndex = i }).ToList()
        };

        var result = await controller.CreateRevision(workload.WorkloadId, request);

        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        var created = (CreatedResult)result.Result!;
        var dto = created.Value as WorkloadRevisionDto;
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Packages.Count, Is.EqualTo(4));
    }

    [Test]
    public async Task BulkImport_ReturnsFailedResult_WhenPackagesIsEmpty()
    {
        var controller = CreateController();
        var json = "[{\"name\":\"TestWorkload\",\"slug\":\"test-workload\",\"version\":\"1.0.0\",\"packages\":[]}]";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var file = new FormFile(stream, 0, stream.Length, "file", "workloads.json")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "application/json" }
        };

        var result = await controller.BulkImport(file);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as BulkImportResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo("failed"));
        Assert.That(response.Results[0].Reason, Does.Contain("at least 1 package").IgnoreCase);
    }

    [Test]
    public async Task BulkImport_ReturnsSuccessResult_WhenSinglePackage()
    {
        var controller = CreateController();
        var json = "[{\"name\":\"TestWorkload\",\"slug\":\"test-workload\",\"version\":\"1.0.0\",\"packages\":[\"git-2.47.1\"]}]";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var file = new FormFile(stream, 0, stream.Length, "file", "workloads.json")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "application/json" }
        };

        var result = await controller.BulkImport(file);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as BulkImportResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo("success"));
    }

    [Test]
    public async Task BulkImport_ReturnsSuccessResult_WhenFourPackages()
    {
        var controller = CreateController();
        var json = "[{\"name\":\"TestWorkload\",\"slug\":\"test-workload\",\"version\":\"1.0.0\",\"packages\":[\"git-2.47.1\",\"node-20.0.0\",\"python-3.12.0\",\"nginx-1.24.0\"]}]";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var file = new FormFile(stream, 0, stream.Length, "file", "workloads.json")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "application/json" }
        };

        var result = await controller.BulkImport(file);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as BulkImportResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo("success"));
    }
}
