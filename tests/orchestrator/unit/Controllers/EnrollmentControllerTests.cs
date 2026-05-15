using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

[TestFixture]
public class EnrollmentControllerTests
{
    private InstallerDbContext _db = null!;
    private SqliteConnection _connection = null!;
    private Mock<ILogger<EnrollmentController>> _loggerMock = null!;

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
        _loggerMock = new Mock<ILogger<EnrollmentController>>();
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task IssueToken_ReturnsOk_WithValidRequest()
    {
        var controller = new EnrollmentController(_db, _loggerMock.Object);
        var request = new IssueEnrollmentTokenRequest
        {
            RequestedBy = "admin",
            OrchestratorUrl = "http://orchestrator:5000",
            TtlMinutes = 30
        };

        var result = await controller.IssueToken(request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var token = okResult.Value as EnrollmentTokenResponse;
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Token, Does.StartWith("enroll-"));
        Assert.That(token.RequestedBy, Is.EqualTo("admin"));
        Assert.That(token.OrchestratorUrl, Is.EqualTo("http://orchestrator:5000"));
        Assert.That(token.SingleUse, Is.True);
        Assert.That(token.Used, Is.False);
        Assert.That(token.ExpiresAt, Is.GreaterThan(DateTime.UtcNow));

        var savedToken = await _db.EnrollmentTokens.FirstOrDefaultAsync();
        Assert.That(savedToken, Is.Not.Null);
        Assert.That(savedToken!.Token, Is.EqualTo(token.Token));
    }

    [Test]
    public async Task IssueToken_ReturnsBadRequest_WhenTtlBelowMinimum()
    {
        var controller = new EnrollmentController(_db, _loggerMock.Object);
        var request = new IssueEnrollmentTokenRequest
        {
            RequestedBy = "admin",
            OrchestratorUrl = "http://orchestrator:5000",
            TtlMinutes = 0
        };

        var result = await controller.IssueToken(request);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task IssueToken_ReturnsBadRequest_WhenTtlAboveMaximum()
    {
        var controller = new EnrollmentController(_db, _loggerMock.Object);
        var request = new IssueEnrollmentTokenRequest
        {
            RequestedBy = "admin",
            OrchestratorUrl = "http://orchestrator:5000",
            TtlMinutes = 121
        };

        var result = await controller.IssueToken(request);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task ListTokens_ReturnsTokens_OrderedByIssuedAtDescending()
    {
        var older = new EnrollmentTokenEntity
        {
            Token = "enroll-older",
            IssuedAtUtc = DateTime.UtcNow.AddHours(-2),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RequestedBy = "admin",
            SingleUse = true,
            Used = false
        };
        var newer = new EnrollmentTokenEntity
        {
            Token = "enroll-newer",
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RequestedBy = "admin2",
            SingleUse = true,
            Used = true
        };
        _db.EnrollmentTokens.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var controller = new EnrollmentController(_db, _loggerMock.Object);
        var result = await controller.ListTokens();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var tokens = okResult.Value as List<EnrollmentTokenResponse>;
        Assert.That(tokens, Is.Not.Null);
        Assert.That(tokens!.Count, Is.EqualTo(2));
        Assert.That(tokens[0].Token, Is.EqualTo("enroll-newer"));
        Assert.That(tokens[1].Token, Is.EqualTo("enroll-older"));
    }

    [Test]
    public async Task ListTokens_ReturnsEmptyList_WhenNoTokens()
    {
        var controller = new EnrollmentController(_db, _loggerMock.Object);
        var result = await controller.ListTokens();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var tokens = okResult.Value as List<EnrollmentTokenResponse>;
        Assert.That(tokens, Is.Not.Null);
        Assert.That(tokens!.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task ConsumeToken_ReturnsOk_WithNode_WhenValid()
    {
        var tokenValue = "enroll-valid";
        var entity = new EnrollmentTokenEntity
        {
            Token = tokenValue,
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RequestedBy = "admin",
            SingleUse = true,
            Used = false
        };
        _db.EnrollmentTokens.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new EnrollmentController(_db, _loggerMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Connection = { RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100") }
            }
        };

        var request = new ConsumeEnrollmentTokenRequest
        {
            Hostname = "test-node",
            DisplayName = "Test Node",
            OsVersion = "Windows 11",
            AgentVersion = "1.0.0"
        };

        var result = await controller.ConsumeToken(tokenValue, request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var node = okResult.Value as DeploymentPoC.Orchestrator.Models.Node;
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Hostname, Is.EqualTo("test-node"));
        Assert.That(node.DisplayName, Is.EqualTo("Test Node"));
        Assert.That(node.IpAddress, Is.EqualTo("192.168.1.100"));
        Assert.That(node.OsVersion, Is.EqualTo("Windows 11"));
        Assert.That(node.AgentVersion, Is.EqualTo("1.0.0"));
        Assert.That(node.Status, Is.EqualTo("online"));

        var refreshedToken = await _db.EnrollmentTokens.FirstOrDefaultAsync(t => t.Token == tokenValue);
        Assert.That(refreshedToken!.Used, Is.True);
        Assert.That(refreshedToken.ConsumedAtUtc, Is.Not.Null);
    }

    [Test]
    public async Task ConsumeToken_MarksTokenUsed_AndSetsConsumedByNodeId()
    {
        var tokenValue = "enroll-consume";
        var entity = new EnrollmentTokenEntity
        {
            Token = tokenValue,
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RequestedBy = "admin",
            SingleUse = true,
            Used = false
        };
        _db.EnrollmentTokens.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new EnrollmentController(_db, _loggerMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.ConsumeToken(tokenValue, null);
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());

        var refreshed = await _db.EnrollmentTokens.FirstAsync(t => t.Token == tokenValue);
        Assert.That(refreshed.Used, Is.True);
        Assert.That(refreshed.ConsumedByNodeId, Is.Not.Null);
        Assert.That(refreshed.ConsumedByNodeId, Is.Not.EqualTo(Guid.Empty));

        var createdNode = await _db.Nodes.FirstOrDefaultAsync(n => n.NodeId == refreshed.ConsumedByNodeId);
        Assert.That(createdNode, Is.Not.Null);
        Assert.That(createdNode!.Hostname, Does.StartWith("auto-node-"));
    }

    [Test]
    public async Task ConsumeToken_ReturnsNotFound_WhenTokenDoesNotExist()
    {
        var controller = new EnrollmentController(_db, _loggerMock.Object);

        var result = await controller.ConsumeToken("nonexistent-token", null);

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task ConsumeToken_ReturnsConflict_WhenTokenAlreadyUsed()
    {
        var entity = new EnrollmentTokenEntity
        {
            Token = "enroll-used",
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RequestedBy = "admin",
            SingleUse = true,
            Used = true,
            ConsumedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ConsumedByNodeId = Guid.NewGuid()
        };
        _db.EnrollmentTokens.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new EnrollmentController(_db, _loggerMock.Object);

        var result = await controller.ConsumeToken("enroll-used", null);

        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
    }

    [Test]
    public async Task ConsumeToken_Returns410Gone_WhenTokenExpired()
    {
        var entity = new EnrollmentTokenEntity
        {
            Token = "enroll-expired",
            IssuedAtUtc = DateTime.UtcNow.AddHours(-2),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-30),
            RequestedBy = "admin",
            SingleUse = true,
            Used = false
        };
        _db.EnrollmentTokens.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new EnrollmentController(_db, _loggerMock.Object);

        var result = await controller.ConsumeToken("enroll-expired", null);

        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        var objectResult = (ObjectResult)result.Result!;
        Assert.That(objectResult.StatusCode, Is.EqualTo(410));
    }

    [Test]
    public async Task ConsumeToken_UsesAutoGeneratedHostname_WhenRequestIsNull()
    {
        var entity = new EnrollmentTokenEntity
        {
            Token = "enroll-auto",
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RequestedBy = "admin",
            SingleUse = true,
            Used = false
        };
        _db.EnrollmentTokens.Add(entity);
        await _db.SaveChangesAsync();

        var controller = new EnrollmentController(_db, _loggerMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.ConsumeToken("enroll-auto", null);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var node = okResult.Value as DeploymentPoC.Orchestrator.Models.Node;
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Hostname, Does.StartWith("auto-node-"));
    }
}
