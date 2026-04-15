using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Persistence;

public class PersistenceTests
{
    [Test]
    public async Task CreateNode_PersistsIpAddressAndDescriptionAcrossRequests()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var createPayload = new
        {
            hostname = "NODE-PERSIST-01",
            ipAddress = "10.10.10.1",
            description = "persistence-check"
        };

        var createResponse = await client.PostAsJsonAsync("/api/nodes", createPayload);
        createResponse.EnsureSuccessStatusCode();

        var createdNode = await createResponse.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(createdNode, Is.Not.Null);

        var listResponse = await client.GetAsync("/api/nodes");
        listResponse.EnsureSuccessStatusCode();
        var listNodes = await listResponse.Content.ReadFromJsonAsync<List<NodeResponse>>();

        Assert.That(listNodes, Is.Not.Null);
        var persistedFromList = listNodes!.Single(n => n.Hostname == "NODE-PERSIST-01");
        Assert.That(persistedFromList.IpAddress, Is.EqualTo("10.10.10.1"));
        Assert.That(persistedFromList.Description, Is.EqualTo("persistence-check"));

        var getResponse = await client.GetAsync($"/api/nodes/{createdNode!.Id}");
        getResponse.EnsureSuccessStatusCode();
        var getNode = await getResponse.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(getNode, Is.Not.Null);
        Assert.That(getNode!.IpAddress, Is.EqualTo("10.10.10.1"));
        Assert.That(getNode.Description, Is.EqualTo("persistence-check"));
    }

    [Test]
    public async Task Packages_CreateListGetDelete_AreDurableAcrossRequests()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var createPayload = new
        {
            name = "agent",
            version = "1.2.3",
            sourcePath = "/tmp/agent.bin",
            installType = "binary",
            installArgs = "--silent"
        };

        var createResponse = await client.PostAsJsonAsync("/api/packages", createPayload);
        createResponse.EnsureSuccessStatusCode();
        var createdPackage = await createResponse.Content.ReadFromJsonAsync<PackageResponse>();
        Assert.That(createdPackage, Is.Not.Null);

        var listResponse = await client.GetAsync("/api/packages");
        listResponse.EnsureSuccessStatusCode();
        var listPackages = await listResponse.Content.ReadFromJsonAsync<List<PackageResponse>>();
        Assert.That(listPackages, Is.Not.Null);
        Assert.That(listPackages!.Any(p => p.Id == createdPackage!.Id && p.Name == "agent"), Is.True);

        var getResponse = await client.GetAsync($"/api/packages/{createdPackage!.Id}");
        getResponse.EnsureSuccessStatusCode();
        var getPackage = await getResponse.Content.ReadFromJsonAsync<PackageResponse>();
        Assert.That(getPackage, Is.Not.Null);
        Assert.That(getPackage!.Version, Is.EqualTo("1.2.3"));

        var deleteResponse = await client.DeleteAsync($"/api/packages/{createdPackage.Id}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NoContent));

        var getDeleted = await client.GetAsync($"/api/packages/{createdPackage.Id}");
        Assert.That(getDeleted.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Jobs_CreateAndGet_MapsPackageAndNodeFields()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var packageCreate = await client.PostAsJsonAsync("/api/packages", new
        {
            name = "svc-core",
            version = "9.9.9",
            sourcePath = "/tmp/svc-core",
            installType = "archive",
            installArgs = ""
        });
        packageCreate.EnsureSuccessStatusCode();
        var package = await packageCreate.Content.ReadFromJsonAsync<PackageResponse>();
        Assert.That(package, Is.Not.Null);

        var nodeCreate = await client.PostAsJsonAsync("/api/nodes", new
        {
            hostname = "NODE-MAP-01",
            ipAddress = "10.20.30.40",
            description = "mapping-target"
        });
        nodeCreate.EnsureSuccessStatusCode();
        var node = await nodeCreate.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(node, Is.Not.Null);

        var jobCreate = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId = package!.Id,
            targetNodeId = node!.Id
        });
        jobCreate.EnsureSuccessStatusCode();
        var createdJob = await jobCreate.Content.ReadFromJsonAsync<JobResponse>();
        Assert.That(createdJob, Is.Not.Null);
        Assert.That(createdJob!.PackageId, Is.EqualTo(package.Id));
        Assert.That(createdJob.PackageName, Is.EqualTo("svc-core"));
        Assert.That(createdJob.TargetNodeId, Is.EqualTo(node.Id));
        Assert.That(createdJob.TargetNodeHostname, Is.EqualTo("NODE-MAP-01"));

        var getJobResponse = await client.GetAsync($"/api/jobs/{createdJob.Id}");
        getJobResponse.EnsureSuccessStatusCode();
        var getJob = await getJobResponse.Content.ReadFromJsonAsync<JobResponse>();
        Assert.That(getJob, Is.Not.Null);
        Assert.That(getJob!.PackageName, Is.EqualTo("svc-core"));
        Assert.That(getJob.TargetNodeHostname, Is.EqualTo("NODE-MAP-01"));

        var listResponse = await client.GetAsync("/api/jobs?status=running");
        listResponse.EnsureSuccessStatusCode();
        var listJobs = await listResponse.Content.ReadFromJsonAsync<List<JobResponse>>();
        Assert.That(listJobs, Is.Not.Null);
        Assert.That(listJobs!.Any(j => j.Id == createdJob.Id), Is.True);
    }

    [Test]
    public async Task CreateNode_DuplicateHostname_ReturnsConflict()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var payload = new
        {
            hostname = "NODE-DUPE-01",
            ipAddress = "10.11.12.13",
            description = "dupe-check"
        };

        var first = await client.PostAsJsonAsync("/api/nodes", payload);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/nodes", payload);
        Assert.That(second.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Jobs_Create_WithEmptyIds_ReturnsBadRequest()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var emptyPackageResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId = Guid.Empty,
            targetNodeId = Guid.NewGuid()
        });
        Assert.That(emptyPackageResponse.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));

        var emptyNodeResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId = Guid.NewGuid(),
            targetNodeId = Guid.Empty
        });
        Assert.That(emptyNodeResponse.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));
    }

    private sealed class NodeResponse
    {
        public Guid Id { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private sealed class PackageResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    private sealed class JobResponse
    {
        public Guid Id { get; set; }
        public Guid PackageId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public Guid TargetNodeId { get; set; }
        public string TargetNodeHostname { get; set; } = string.Empty;
    }
}
