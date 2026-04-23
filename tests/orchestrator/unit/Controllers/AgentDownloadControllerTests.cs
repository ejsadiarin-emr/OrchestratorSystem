using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

[TestFixture]
public class AgentDownloadControllerTests
{
    private Mock<ILogger<AgentDownloadController>> _loggerMock = null!;
    private Mock<IWebHostEnvironment> _envMock = null!;
    private InstallerDbContext _db = null!;
    private string _tempPath = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<AgentDownloadController>>();
        _envMock = new Mock<IWebHostEnvironment>();
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        _envMock.Setup(e => e.ContentRootPath).Returns(_tempPath);

        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite(connection)
            .Options;
        _db = new InstallerDbContext(options);
        _db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }

    private AgentDownloadController CreateController()
    {
        return new AgentDownloadController(_loggerMock.Object, _envMock.Object, _db);
    }

    [Test]
    public void Download_ReturnsUnauthorized_WhenTokenIsMissing()
    {
        var controller = CreateController();
        var result = controller.Download(null);
        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public void Download_ReturnsUnauthorized_WhenTokenIsInvalid()
    {
        var controller = CreateController();
        var result = controller.Download("nonexistent-token");
        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public void Download_ReturnsUnauthorized_WhenTokenIsAlreadyUsed()
    {
        var token = "used-token-123";
        _db.EnrollmentTokens.Add(new EnrollmentTokenEntity
        {
            Token = token,
            Used = true,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });
        _db.SaveChanges();

        var controller = CreateController();
        var result = controller.Download(token);
        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    [Test]
    public void Download_ReturnsGone_WhenTokenIsExpired()
    {
        var token = "expired-token-123";
        _db.EnrollmentTokens.Add(new EnrollmentTokenEntity
        {
            Token = token,
            Used = false,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-1)
        });
        _db.SaveChanges();

        var controller = CreateController();
        var result = controller.Download(token);
        Assert.That(result, Is.TypeOf<ObjectResult>());
        var objectResult = (ObjectResult)result;
        Assert.That(objectResult.StatusCode, Is.EqualTo(410));
    }

    [Test]
    public void Download_ReturnsFile_WhenTokenIsValidAndUnused()
    {
        var token = "valid-token-123";
        _db.EnrollmentTokens.Add(new EnrollmentTokenEntity
        {
            Token = token,
            Used = false,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });
        _db.SaveChanges();

        var controller = CreateController();
        var result = controller.Download(token);
        Assert.That(result, Is.TypeOf<PhysicalFileResult>());
    }
}
