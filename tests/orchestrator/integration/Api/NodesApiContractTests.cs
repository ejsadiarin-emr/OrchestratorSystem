using System.Net;
using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public class NodesApiContractTests
{
    [Test]
    public async Task NodesApi_Implements_NodeListContract()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var createNodeResponse = await client.PostAsJsonAsync("/api/nodes", new
        {
            hostname = "NODE-API-01",
            ipAddress = "10.0.0.11",
            description = "api-contract"
        });
        createNodeResponse.EnsureSuccessStatusCode();

        var listResponse = await client.GetAsync("/api/nodes");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await listResponse.Content.ReadFromJsonAsync<List<NodeResponse>>();
        Assert.That(body, Is.Not.Null);
        var node = body!.SingleOrDefault(n => n.Hostname == "NODE-API-01");
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(node.Status, Is.Not.Null.And.Not.Empty);
        Assert.That(node.LastSeenAt, Is.Not.EqualTo(default(DateTime)));
    }

    private sealed class NodeResponse
    {
        public Guid Id { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime LastSeenAt { get; set; }
        public DateTime? FirstConnectedAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string AgentVersion { get; set; } = string.Empty;
    }
}
