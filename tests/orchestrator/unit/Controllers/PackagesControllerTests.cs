using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

[TestFixture]
public class PackagesControllerTests
{
    private InstallerDbContext _db = null!;
    private SqliteConnection _connection = null!;
    private Mock<ILogger<PackagesController>> _loggerMock = null!;

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
        _loggerMock = new Mock<ILogger<PackagesController>>();
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task GetAll_ReturnsAllPackages_OrderedByCreatedAtDescending()
    {
        var older = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = "git",
            Version = "2.47.0",
            SourcePath = "git.exe",
            InstallType = "exe",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var newer = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = "node",
            Version = "20.0.0",
            SourcePath = "node.msi",
            InstallType = "msi",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Packages.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var controller = new PackagesController(_db, _loggerMock.Object);
        var result = await controller.GetAll();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var packages = okResult.Value as IEnumerable<Package>;
        Assert.That(packages, Is.Not.Null);
        var list = packages!.ToList();
        Assert.That(list, Has.Count.EqualTo(2));
        Assert.That(list[0].Name, Is.EqualTo("node"));
        Assert.That(list[1].Name, Is.EqualTo("git"));
    }

    [Test]
    public async Task GetById_ReturnsPackage_WhenFound()
    {
        var entity = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            InstallArgs = "/S",
            DetectionConfigJson = "{\"Type\":\"file\",\"Path\":\"C:\\\\git\\\\bin\\\\git.exe\"}",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Packages.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new PackagesController(_db, _loggerMock.Object);
        var result = await controller.GetById(entity.PackageId);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var package = okResult.Value as Package;
        Assert.That(package, Is.Not.Null);
        Assert.That(package!.Id, Is.EqualTo(entity.PackageId));
        Assert.That(package.Name, Is.EqualTo("git"));
        Assert.That(package.Version, Is.EqualTo("2.47.1"));
        Assert.That(package.DetectionType, Is.EqualTo("file"));
        Assert.That(package.DetectionPath, Is.EqualTo("C:\\git\\bin\\git.exe"));
    }

    [Test]
    public async Task GetById_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var controller = new PackagesController(_db, _loggerMock.Object);
        var result = await controller.GetById(Guid.NewGuid());

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Create_ReturnsCreatedAtAction_WithValidRequest()
    {
        var controller = new PackagesController(_db, _loggerMock.Object);
        var request = new CreatePackageRequest
        {
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            InstallArgs = "/S",
            UninstallCommand = "git-uninstall.exe",
            UninstallArgs = "/quiet",
            UpgradeBehavior = "InPlace",
            DetectionType = "file",
            DetectionPath = "C:\\git\\bin\\git.exe"
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = (CreatedAtActionResult)result.Result!;
        var package = createdResult.Value as Package;
        Assert.That(package, Is.Not.Null);
        Assert.That(package!.Name, Is.EqualTo("git"));
        Assert.That(package.Version, Is.EqualTo("2.47.1"));
        Assert.That(package.UpgradeBehavior, Is.EqualTo("InPlace"));
        Assert.That(package.DetectionType, Is.EqualTo("file"));
        Assert.That(package.DetectionPath, Is.EqualTo("C:\\git\\bin\\git.exe"));

        var savedEntity = await _db.Packages.FirstOrDefaultAsync(p => p.PackageId == package.Id);
        Assert.That(savedEntity, Is.Not.Null);
        Assert.That(savedEntity!.DetectionConfigJson, Does.Contain("file"));
    }

    [Test]
    public async Task Create_ReturnsBadRequest_WhenUpgradeBehaviorInvalid()
    {
        var controller = new PackagesController(_db, _loggerMock.Object);
        var request = new CreatePackageRequest
        {
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            UpgradeBehavior = "InvalidBehavior"
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Create_NormalizesUpgradeBehavior_WhenCasingDiffers()
    {
        var controller = new PackagesController(_db, _loggerMock.Object);
        var request = new CreatePackageRequest
        {
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            UpgradeBehavior = "inplace"
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = (CreatedAtActionResult)result.Result!;
        var package = createdResult.Value as Package;
        Assert.That(package!.UpgradeBehavior, Is.EqualTo("InPlace"));
    }

    [Test]
    public async Task Create_StoresEmptyDetectionConfig_WhenNoDetectionInfo()
    {
        var controller = new PackagesController(_db, _loggerMock.Object);
        var request = new CreatePackageRequest
        {
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            UpgradeBehavior = "InPlace"
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = (CreatedAtActionResult)result.Result!;
        var package = createdResult.Value as Package;
        Assert.That(package!.DetectionType, Is.Empty);
        Assert.That(package.DetectionPath, Is.Empty);
    }

    [Test]
    public async Task Delete_ReturnsNoContent_WhenPackageExists()
    {
        var entity = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Packages.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new PackagesController(_db, _loggerMock.Object);
        var result = await controller.Delete(entity.PackageId);

        Assert.That(result, Is.TypeOf<NoContentResult>());

        var deleted = await _db.Packages.FirstOrDefaultAsync(p => p.PackageId == entity.PackageId);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task Delete_ReturnsNotFound_WhenPackageDoesNotExist()
    {
        var controller = new PackagesController(_db, _loggerMock.Object);
        var result = await controller.Delete(Guid.NewGuid());

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetAll_ReturnsDetectionConfig_WhenJsonIsValid()
    {
        var entity = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = "nginx",
            Version = "1.24.0",
            SourcePath = "nginx.msi",
            InstallType = "msi",
            DetectionConfigJson = "{\"Type\":\"registry\",\"Path\":\"DisplayName like 'nginx'\"}",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Packages.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new PackagesController(_db, _loggerMock.Object);
        var result = await controller.GetAll();

        var okResult = (OkObjectResult)result.Result!;
        var packages = (okResult.Value as IEnumerable<Package>)!.ToList();
        Assert.That(packages[0].DetectionType, Is.EqualTo("registry"));
        Assert.That(packages[0].DetectionPath, Is.EqualTo("DisplayName like 'nginx'"));
    }

    [Test]
    public async Task GetAll_ReturnsEmptyDetectionConfig_WhenJsonIsNull()
    {
        var entity = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = "git",
            Version = "2.47.1",
            SourcePath = "git.exe",
            InstallType = "exe",
            DetectionConfigJson = string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Packages.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new PackagesController(_db, _loggerMock.Object);
        var result = await controller.GetAll();

        var okResult = (OkObjectResult)result.Result!;
        var packages = (okResult.Value as IEnumerable<Package>)!.ToList();
        Assert.That(packages[0].DetectionType, Is.EqualTo(string.Empty));
        Assert.That(packages[0].DetectionPath, Is.EqualTo(string.Empty));
    }
}
