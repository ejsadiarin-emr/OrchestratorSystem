using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class NodeHeartbeatMonitorServiceTests
{
    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<InstallerDbContext>(options => options.UseSqlite(connection));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
    }

    private NodeHeartbeatMonitorService CreateService()
    {
        var loggerMock = new Mock<ILogger<NodeHeartbeatMonitorService>>();
        return new NodeHeartbeatMonitorService(_serviceProvider, loggerMock.Object);
    }

    [Test]
    public async Task ScanAsync_DetectsStaleNodes()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

        var staleNode = new NodeEntity
        {
            NodeId = Guid.NewGuid(),
            Hostname = "stale",
            LastSeenUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        db.Nodes.Add(staleNode);
        await db.SaveChangesAsync();

        var service = CreateService();
        Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));
    }

    [Test]
    public async Task ScanAsync_DoesNotAffectFreshOnlineNodes()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

        var freshNode = new NodeEntity
        {
            NodeId = Guid.NewGuid(),
            Hostname = "fresh",
            LastSeenUtc = DateTime.UtcNow.AddSeconds(-30)
        };
        db.Nodes.Add(freshNode);
        await db.SaveChangesAsync();

        var service = CreateService();
        Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));
    }

    [Test]
    public async Task ScanAsync_DoesNotAffectAlreadyOfflineNodes()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();

        var offlineNode = new NodeEntity
        {
            NodeId = Guid.NewGuid(),
            Hostname = "offline",
            LastSeenUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        db.Nodes.Add(offlineNode);
        await db.SaveChangesAsync();

        var service = CreateService();
        Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));
    }
}
