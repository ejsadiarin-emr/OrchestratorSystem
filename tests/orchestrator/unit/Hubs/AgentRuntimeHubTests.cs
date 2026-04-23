using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Hubs;
using DeploymentPoC.Orchestrator.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Hubs;

[TestFixture]
public class AgentRuntimeHubTests
{
    private Mock<ILogger<AgentRuntimeHub>> _loggerMock = null!;
    private AgentConnectionTracker _connectionTracker = null!;
    private InstallerDbContext _db = null!;
    private AgentRuntimeHub _hub = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<AgentRuntimeHub>>();
        var spMock = new Mock<IServiceProvider>();
        var stateLoggerMock = new Mock<ILogger<NodeWorkloadStateService>>();
        var stateService = new NodeWorkloadStateService(spMock.Object, stateLoggerMock.Object);
        _connectionTracker = new AgentConnectionTracker();

        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite(connection)
            .Options;
        _db = new InstallerDbContext(options);
        _db.Database.EnsureCreated();

        _hub = new AgentRuntimeHub(
            stateService,
            _connectionTracker,
            _db,
            _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    private static HubCallerContext CreateHubContext(string connectionId)
    {
        var mock = new Mock<HubCallerContext>();
        mock.Setup(c => c.ConnectionId).Returns(connectionId);
        return mock.Object;
    }

    [Test]
    public async Task OnDisconnectedAsync_SetsNodeStatusToOffline()
    {
        var nodeId = Guid.NewGuid();
        var connectionId = "conn-test-1";

        _connectionTracker.Register(nodeId, connectionId);
        _hub.Context = CreateHubContext(connectionId);

        var node = new NodeEntity { NodeId = nodeId, Hostname = "test-node", Status = "Online" };
        _db.Nodes.Add(node);
        await _db.SaveChangesAsync();

        await _hub.OnDisconnectedAsync(null);

        var updated = await _db.Nodes.FindAsync(nodeId);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Status, Is.EqualTo("Offline"));
    }

    [Test]
    public async Task OnDisconnectedAsync_DoesNotThrow_WhenNodeNotFound()
    {
        var connectionId = "conn-test-unknown";
        _hub.Context = CreateHubContext(connectionId);

        Assert.DoesNotThrowAsync(async () => await _hub.OnDisconnectedAsync(null));
    }
}
