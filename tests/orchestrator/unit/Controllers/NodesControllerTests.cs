using DeploymentPoC.Orchestrator.Contracts.Api;
using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

[TestFixture]
public class NodesControllerTests
{
    private InstallerDbContext _db = null!;
    private SqliteConnection _connection = null!;
    private Mock<ILogger<NodesController>> _loggerMock = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private Mock<IConfiguration> _configMock = null!;

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
        _loggerMock = new Mock<ILogger<NodesController>>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configMock = new Mock<IConfiguration>();
        _configMock.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private NodesController CreateController()
    {
        return new NodesController(_db, _loggerMock.Object, _httpClientFactoryMock.Object, _configMock.Object);
    }

    [Test]
    public async Task GetAll_ReturnsNodes_WithCorrectStatusMapping()
    {
        var onlineNode = TestSeedBuilder.CreateNode(
            hostname: "online-node", displayName: "Online Node", ipAddress: "10.0.0.1",
            status: "Online", lastSeenUtc: DateTime.UtcNow.AddSeconds(-30),
            firstConnectedUtc: DateTime.UtcNow.AddDays(-1), osVersion: "Windows 11", agentVersion: "1.0.0");
        var offlineNode = TestSeedBuilder.CreateNode(
            hostname: "offline-node", displayName: "Offline Node", ipAddress: "10.0.0.2",
            status: "Online", lastSeenUtc: DateTime.UtcNow.AddMinutes(-5),
            firstConnectedUtc: DateTime.UtcNow.AddDays(-2), osVersion: "Windows 10", agentVersion: "0.9.0");
        _db.Nodes.AddRange(onlineNode, offlineNode);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetAll();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var nodes = okResult.Value as List<DeploymentPoC.Orchestrator.Models.Node>;
        Assert.That(nodes, Is.Not.Null);
        Assert.That(nodes!.Count, Is.EqualTo(2));

        var online = nodes.First(n => n.Hostname == "online-node");
        Assert.That(online.Status, Is.EqualTo("online"));
        Assert.That(online.DisplayName, Is.EqualTo("Online Node"));

        var offline = nodes.First(n => n.Hostname == "offline-node");
        Assert.That(offline.Status, Is.EqualTo("offline"));
    }

    [Test]
    public async Task GetById_ReturnsNode_WhenFound()
    {
        var entity = TestSeedBuilder.CreateNode(
            hostname: "test-node", displayName: "Test Node", ipAddress: "192.168.1.50",
            status: "Online", lastSeenUtc: DateTime.UtcNow.AddSeconds(-15),
            osVersion: "Windows 11", agentVersion: "1.2.0");
        _db.Nodes.Add(entity);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetById(entity.NodeId);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var node = okResult.Value as DeploymentPoC.Orchestrator.Models.Node;
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Id, Is.EqualTo(entity.NodeId));
        Assert.That(node.Hostname, Is.EqualTo("test-node"));
        Assert.That(node.IpAddress, Is.EqualTo("192.168.1.50"));
        Assert.That(node.Status, Is.EqualTo("online"));
    }

    [Test]
    public async Task GetById_ReturnsNotFound_WhenNodeDoesNotExist()
    {
        var controller = CreateController();
        var result = await controller.GetById(Guid.NewGuid());

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Create_ReturnsCreatedAtAction_WithValidRequest()
    {
        var controller = CreateController();
        var request = new CreateNodeRequest
        {
            Hostname = "new-node",
            IpAddress = "10.0.0.100",
            Description = "A new test node"
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = (CreatedAtActionResult)result.Result!;
        var node = createdResult.Value as DeploymentPoC.Orchestrator.Models.Node;
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Hostname, Is.EqualTo("new-node"));
        Assert.That(node.IpAddress, Is.EqualTo("10.0.0.100"));
        Assert.That(node.Description, Is.EqualTo("A new test node"));
        Assert.That(node.Status, Is.EqualTo("online"));

        var savedEntity = await _db.Nodes.FirstOrDefaultAsync(n => n.NodeId == node.Id);
        Assert.That(savedEntity, Is.Not.Null);
    }

    [Test]
    public async Task Create_ReturnsConflict_WhenDuplicateHostname()
    {
        var existing = TestSeedBuilder.CreateNode(
            hostname: "duplicate-node", ipAddress: "10.0.0.1", status: "Online", lastSeenUtc: DateTime.UtcNow);
        _db.Nodes.Add(existing);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new CreateNodeRequest
        {
            Hostname = "duplicate-node",
            IpAddress = "10.0.0.2",
            Description = "Should conflict"
        };

        var result = await controller.Create(request);

        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
    }

    [Test]
    public async Task Update_ReturnsOk_WhenNodeExists()
    {
        var entity = TestSeedBuilder.CreateNode(
            hostname: "old-hostname", displayName: "Old Display", ipAddress: "10.0.0.1",
            description: "Old description", status: "Online", lastSeenUtc: DateTime.UtcNow.AddMinutes(-1));
        _db.Nodes.Add(entity);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new UpdateNodeRequest
        {
            Hostname = "updated-hostname",
            DisplayName = "Updated Display",
            IpAddress = "10.0.0.200",
            Description = "Updated description"
        };

        var result = await controller.Update(entity.NodeId, request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var node = okResult.Value as DeploymentPoC.Orchestrator.Models.Node;
        Assert.That(node!.Hostname, Is.EqualTo("updated-hostname"));
        Assert.That(node.IpAddress, Is.EqualTo("10.0.0.200"));
        Assert.That(node.Description, Is.EqualTo("Updated description"));
    }

    [Test]
    public async Task Update_ReturnsNotFound_WhenNodeDoesNotExist()
    {
        var controller = CreateController();
        var request = new UpdateNodeRequest
        {
            Hostname = "nonexistent",
            IpAddress = "10.0.0.1"
        };

        var result = await controller.Update(Guid.NewGuid(), request);

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task UpdateDisplayName_ReturnsOk_WhenNodeExists()
    {
        var entity = TestSeedBuilder.CreateNode(
            hostname: "test-node", displayName: "Old Name", ipAddress: "10.0.0.1",
            status: "Online", lastSeenUtc: DateTime.UtcNow);
        _db.Nodes.Add(entity);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new UpdateNodeDisplayNameRequest
        {
            DisplayName = "New Name"
        };

        var result = await controller.UpdateDisplayName(entity.NodeId, request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var node = okResult.Value as DeploymentPoC.Orchestrator.Models.Node;
        Assert.That(node!.DisplayName, Is.EqualTo("New Name"));

        var refreshed = await _db.Nodes.FirstAsync(n => n.NodeId == entity.NodeId);
        Assert.That(refreshed.DisplayName, Is.EqualTo("New Name"));
    }

    [Test]
    public async Task UpdateDisplayName_ReturnsNotFound_WhenNodeDoesNotExist()
    {
        var controller = CreateController();
        var request = new UpdateNodeDisplayNameRequest { DisplayName = "Anyone" };

        var result = await controller.UpdateDisplayName(Guid.NewGuid(), request);

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Delete_ReturnsNoContent_WhenNodeExists()
    {
        var entity = TestSeedBuilder.CreateNode(
            hostname: "to-delete", ipAddress: "10.0.0.99", status: "Online", lastSeenUtc: DateTime.UtcNow);
        _db.Nodes.Add(entity);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.Delete(entity.NodeId);

        Assert.That(result, Is.TypeOf<NoContentResult>());

        var deleted = await _db.Nodes.FirstOrDefaultAsync(n => n.NodeId == entity.NodeId);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task Delete_ReturnsNotFound_WhenNodeDoesNotExist()
    {
        var controller = CreateController();
        var result = await controller.Delete(Guid.NewGuid());

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetWorkloadStates_ReturnsStates()
    {
        var workload = TestSeedBuilder.CreateWorkload(name: "TestWorkload");
        var revision = TestSeedBuilder.CreateRevision(workloadId: workload.WorkloadId);
        var node = TestSeedBuilder.CreateNode(
            hostname: "test-node", ipAddress: "10.0.0.1", status: "Online", lastSeenUtc: DateTime.UtcNow);
        var state = TestSeedBuilder.CreateNodeWorkloadState(
            nodeId: node.NodeId, workloadId: workload.WorkloadId,
            currentRevisionId: revision.RevisionId, status: "Current");
        _db.WorkloadDefinitions.Add(workload);
        _db.WorkloadRevisions.Add(revision);
        _db.Nodes.Add(node);
        _db.NodeWorkloadStates.Add(state);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetWorkloadStates();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var states = okResult.Value as List<NodeWorkloadStateResponse>;
        Assert.That(states, Is.Not.Null);
        Assert.That(states!.Count, Is.EqualTo(1));
        Assert.That(states[0].NodeId, Is.EqualTo(node.NodeId));
        Assert.That(states[0].WorkloadRevision, Is.EqualTo("1.0.0"));
        Assert.That(states[0].Status, Is.EqualTo("Current"));
    }

    [Test]
    public async Task GetDetails_ReturnsNodeDetail_WithWorkloadAssignments()
    {
        var workload = TestSeedBuilder.CreateWorkload(name: "TestWorkload");
        var revision = TestSeedBuilder.CreateRevision(workloadId: workload.WorkloadId, version: "2.0.0");
        var node = TestSeedBuilder.CreateNode(
            hostname: "detail-node", displayName: "Detail Node", ipAddress: "10.0.0.42",
            status: "Online", lastSeenUtc: DateTime.UtcNow.AddSeconds(-10),
            firstConnectedUtc: DateTime.UtcNow.AddDays(-1), osVersion: "Windows 11", agentVersion: "1.0.0");
        var state = TestSeedBuilder.CreateNodeWorkloadState(
            nodeId: node.NodeId, workloadId: workload.WorkloadId,
            currentRevisionId: revision.RevisionId, status: "Current");
        node.NodeWorkloadStates = new List<NodeWorkloadStateEntity> { state };
        _db.WorkloadDefinitions.Add(workload);
        _db.WorkloadRevisions.Add(revision);
        _db.Nodes.Add(node);
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.GetDetails(node.NodeId);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result!;
        var detail = okResult.Value as NodeDetailResponse;
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.Id, Is.EqualTo(node.NodeId));
        Assert.That(detail.Hostname, Is.EqualTo("detail-node"));
        Assert.That(detail.DisplayName, Is.EqualTo("Detail Node"));
        Assert.That(detail.Workloads, Has.Count.EqualTo(1));
        Assert.That(detail.Workloads[0].Name, Is.EqualTo("TestWorkload"));
        Assert.That(detail.Workloads[0].CurrentVersion, Is.EqualTo("2.0.0"));
        Assert.That(detail.LatestPreCheck, Is.Not.Null);
    }

    [Test]
    public async Task GetDetails_ReturnsNotFound_WhenNodeDoesNotExist()
    {
        var controller = CreateController();
        var result = await controller.GetDetails(Guid.NewGuid());

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }
}
