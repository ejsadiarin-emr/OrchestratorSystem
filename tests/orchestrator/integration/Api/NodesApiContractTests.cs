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

    [Test]
    public async Task NodesApi_RunPreCheckSummary_ReturnsExpectedStructure()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeResponse = await client.PostAsJsonAsync("/api/nodes", new
        {
            hostname = "PRE-CHECK-NODE-01",
            ipAddress = "10.0.0.21",
            description = "pre-check-summary-test"
        });
        nodeResponse.EnsureSuccessStatusCode();
        var node = await nodeResponse.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(node, Is.Not.Null);

        var pkgResponse = await client.PostAsJsonAsync("/api/packages", new
        {
            name = "pre-check-pkg",
            version = "1.0.0",
            sourcePath = "/tmp/pre-check-pkg",
            installType = "archive",
            installArgs = ""
        });
        pkgResponse.EnsureSuccessStatusCode();
        var package = await pkgResponse.Content.ReadFromJsonAsync<PackageResponse>();
        Assert.That(package, Is.Not.Null);

        var workloadResponse = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = $"pre-check-workload-{Guid.NewGuid():N}"
        });
        workloadResponse.EnsureSuccessStatusCode();
        var workload = await workloadResponse.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(workload, Is.Not.Null);

        var revisionResponse = await client.PostAsJsonAsync($"/api/workloads/{workload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages = new[]
            {
                new { packageId = package!.Id, packageIndex = 1 }
            }
        });
        revisionResponse.EnsureSuccessStatusCode();
        var revision = await revisionResponse.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(revision, Is.Not.Null);

        var publishResponse = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = revision!.RevisionId
        });
        publishResponse.EnsureSuccessStatusCode();

        var summaryResponse = await client.PostAsJsonAsync("/api/nodes/prechecks/summary", new
        {
            nodeIds = new[] { node!.Id },
            workloadId = workload.WorkloadId,
            revisionId = revision.RevisionId
        });

        Assert.That(summaryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var summary = await summaryResponse.Content.ReadFromJsonAsync<PreCheckSummaryResponse>();
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.Nodes, Has.Count.EqualTo(1));
        Assert.That(summary.Nodes[0].NodeId, Is.EqualTo(node.Id));
        Assert.That(summary.Nodes[0].Hostname, Is.EqualTo("PRE-CHECK-NODE-01"));
        Assert.That(summary.Nodes[0].WorkloadStatus, Is.Not.Null);
        Assert.That(summary.Nodes[0].Action, Is.Not.Null);
        Assert.That(summary.Nodes[0].Packages, Has.Count.EqualTo(1));
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

    private sealed class PackageResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class WorkloadDetailResponse
    {
        public Guid WorkloadId { get; set; }
    }

    private sealed class WorkloadRevisionResponse
    {
        public Guid RevisionId { get; set; }
    }

    private sealed class PreCheckSummaryResponse
    {
        public List<PreCheckSummaryNode> Nodes { get; set; } = new();
    }

    private sealed class PreCheckSummaryNode
    {
        public Guid NodeId { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string WorkloadStatus { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public List<PreCheckSummaryPackage> Packages { get; set; } = new();
    }

    private sealed class PreCheckSummaryPackage
    {
        public Guid PackageId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Comparison { get; set; }
        public string? ActualVersion { get; set; }
        public string? ExpectedVersion { get; set; }
    }
}
