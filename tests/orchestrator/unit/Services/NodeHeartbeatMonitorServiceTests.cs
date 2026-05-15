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
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<InstallerDbContext>(options => options.UseSqlite(_connection));
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
        _connection.Dispose();
    }

    private NodeHeartbeatMonitorService CreateService()
    {
        var loggerMock = new Mock<ILogger<NodeHeartbeatMonitorService>>();
        return new NodeHeartbeatMonitorService(_serviceProvider, loggerMock.Object);
    }

    private InstallerDbContext CreateDbContext()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
    }

    [Test]
    public async Task ScanAsync_DetectsStaleNodes_TransitionsToOffline()
    {
        var staleId = Guid.NewGuid();
        var staleLastSeen = DateTime.UtcNow.AddMinutes(-5);

        using (var db = CreateDbContext())
        {
            db.Nodes.Add(TestSeedBuilder.CreateNode(nodeId: staleId, hostname: "stale", status: "Online", lastSeenUtc: staleLastSeen));
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));

        using (var db = CreateDbContext())
        {
            var node = await db.Nodes.FindAsync(staleId);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.Status, Is.EqualTo("Offline"));
        }
    }

    [Test]
    public async Task ScanAsync_LeavesFreshOnlineNodes_Online()
    {
        var freshId = Guid.NewGuid();
        var freshLastSeen = DateTime.UtcNow.AddSeconds(-30);

        using (var db = CreateDbContext())
        {
            db.Nodes.Add(TestSeedBuilder.CreateNode(nodeId: freshId, hostname: "fresh", status: "Online", lastSeenUtc: freshLastSeen));
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));

        using (var db = CreateDbContext())
        {
            var node = await db.Nodes.FindAsync(freshId);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.Status, Is.EqualTo("Online"));
        }
    }

    [Test]
    public async Task ScanAsync_DoesNotReprocessAlreadyOfflineNodes()
    {
        var offlineId = Guid.NewGuid();
        var offlineLastSeen = DateTime.UtcNow.AddMinutes(-5);

        using (var db = CreateDbContext())
        {
            db.Nodes.Add(TestSeedBuilder.CreateNode(nodeId: offlineId, hostname: "offline", status: "Offline", lastSeenUtc: offlineLastSeen));
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));

        using (var db = CreateDbContext())
        {
            var node = await db.Nodes.FindAsync(offlineId);
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.Status, Is.EqualTo("Offline"));
            Assert.That(node.LastSeenUtc, Is.EqualTo(offlineLastSeen));
        }
    }

    [Test]
    public async Task ScanAsync_BulkTransition_MultipleStaleNodes()
    {
        var staleId1 = Guid.NewGuid();
        var staleId2 = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        var staleTime = DateTime.UtcNow.AddMinutes(-5);

        using (var db = CreateDbContext())
        {
            db.Nodes.Add(TestSeedBuilder.CreateNode(nodeId: staleId1, hostname: "stale-1", status: "Online", lastSeenUtc: staleTime));
            db.Nodes.Add(TestSeedBuilder.CreateNode(nodeId: staleId2, hostname: "stale-2", status: "Online", lastSeenUtc: staleTime));
            db.Nodes.Add(TestSeedBuilder.CreateNode(nodeId: freshId, hostname: "fresh", status: "Online", lastSeenUtc: DateTime.UtcNow.AddSeconds(-30)));
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        Assert.DoesNotThrowAsync(async () => await service.ScanAsync(CancellationToken.None));

        using (var db = CreateDbContext())
        {
            var stale1 = await db.Nodes.FindAsync(staleId1);
            Assert.That(stale1, Is.Not.Null);
            Assert.That(stale1!.Status, Is.EqualTo("Offline"));

            var stale2 = await db.Nodes.FindAsync(staleId2);
            Assert.That(stale2, Is.Not.Null);
            Assert.That(stale2!.Status, Is.EqualTo("Offline"));

            var fresh = await db.Nodes.FindAsync(freshId);
            Assert.That(fresh, Is.Not.Null);
            Assert.That(fresh!.Status, Is.EqualTo("Online"));
        }
    }
}
