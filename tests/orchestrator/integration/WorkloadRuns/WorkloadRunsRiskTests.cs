using System.Net;
using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public sealed class WorkloadRunsRiskTests
{
    [Test]
    public async Task CreateRun_WithLowRiskPackages_ReturnsLowRisk()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "RISK-NODE-01", "10.20.2.1");
        var pkgA = await CreatePackageAsync(client, "risk-low-a");
        var pkgB = await CreatePackageAsync(client, "risk-low-b");

        await IngestArtifactAsync(client, "risk-low-a", "1.0.0", "low");
        await IngestArtifactAsync(client, "risk-low-b", "1.0.0", "low");

        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB);

        var createResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "risk-low-idem-1",
            nodeIds = new[] { nodeId }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.RiskLevel, Is.EqualTo("low"));

        var detailResponse = await client.GetAsync($"/api/workload-runs/{created.RunId}");
        Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await detailResponse.Content.ReadFromJsonAsync<WorkloadRunDetailResponse>();
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.RiskLevel, Is.EqualTo("low"));
    }

    [Test]
    public async Task CreateRun_WithHighRiskPackage_ReturnsHighRisk()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "RISK-NODE-02", "10.20.2.2");
        var pkgA = await CreatePackageAsync(client, "risk-high-a");
        var pkgB = await CreatePackageAsync(client, "risk-high-b");

        await IngestArtifactAsync(client, "risk-high-a", "1.0.0", "high");
        await IngestArtifactAsync(client, "risk-high-b", "1.0.0", "low");

        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB);

        var createResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "risk-high-idem-1",
            nodeIds = new[] { nodeId }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.RiskLevel, Is.EqualTo("high"));

        var detailResponse = await client.GetAsync($"/api/workload-runs/{created.RunId}");
        Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await detailResponse.Content.ReadFromJsonAsync<WorkloadRunDetailResponse>();
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.RiskLevel, Is.EqualTo("high"));
    }

    [Test]
    public async Task CreateRun_WithMixedRiskPackages_ReturnsHighRisk()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "RISK-NODE-03", "10.20.2.3");
        var pkgA = await CreatePackageAsync(client, "risk-mixed-a");
        var pkgB = await CreatePackageAsync(client, "risk-mixed-b");
        var pkgC = await CreatePackageAsync(client, "risk-mixed-c");

        await IngestArtifactAsync(client, "risk-mixed-a", "1.0.0", "low");
        await IngestArtifactAsync(client, "risk-mixed-b", "1.0.0", "medium");
        await IngestArtifactAsync(client, "risk-mixed-c", "1.0.0", "high");

        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB, pkgC);

        var createResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "risk-mixed-idem-1",
            nodeIds = new[] { nodeId }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.RiskLevel, Is.EqualTo("high"));

        var detailResponse = await client.GetAsync($"/api/workload-runs/{created.RunId}");
        Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await detailResponse.Content.ReadFromJsonAsync<WorkloadRunDetailResponse>();
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.RiskLevel, Is.EqualTo("high"));
    }

    private static async Task<Guid> CreateNodeAsync(HttpClient client, string hostname, string ipAddress)
    {
        var response = await client.PostAsJsonAsync("/api/nodes", new
        {
            hostname,
            ipAddress,
            description = "workload-run-target"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(body, Is.Not.Null);
        return body!.Id;
    }

    private static async Task<Guid> CreatePackageAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/packages", new
        {
            name,
            version = "1.0.0",
            sourcePath = $"/tmp/{name}",
            installType = "archive",
            installArgs = ""
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PackageResponse>();
        Assert.That(body, Is.Not.Null);
        return body!.Id;
    }

    private static async Task IngestArtifactAsync(HttpClient client, string packageId, string version, string riskLevel)
    {
        var manifest = $$"""
            {
                "packageId": "{{packageId}}",
                "version": "{{version}}",
                "channel": "stable",
                "artifactType": "archive",
                "installAdapter": {
                    "type": "archive",
                    "command": "install.sh",
                    "arguments": "-quiet",
                    "expectedExitCodes": [0],
                    "timeoutSeconds": 1800
                },
                "detection": {
                    "type": "version_manifest",
                    "path": "{{packageId}}",
                    "expectedVersion": "=={{version}}"
                },
                "policyTags": {
                    "riskLevel": "{{riskLevel}}",
                    "retryabilityClass": "transient_only",
                    "idempotencyMode": "version_check",
                    "approvalRequired": false
                }
            }
            """;

        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(new System.IO.MemoryStream(new byte[] { 0x01, 0x02, 0x03 }));
        content.Add(fileContent, "file", "artifact.zip");
        content.Add(new StringContent(manifest), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<(Guid WorkloadId, Guid RevisionId)> CreatePublishedWorkloadAsync(HttpClient client, params Guid[] packageIds)
    {
        var createWorkload = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = $"workload-{Guid.NewGuid():N}"
        });
        createWorkload.EnsureSuccessStatusCode();
        var workload = await createWorkload.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(workload, Is.Not.Null);

        var packages = packageIds.Select((id, idx) => new { packageId = id, packageIndex = idx + 1 }).ToArray();

        var createRevision = await client.PostAsJsonAsync($"/api/workloads/{workload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages
        });
        createRevision.EnsureSuccessStatusCode();
        var revision = await createRevision.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(revision, Is.Not.Null);

        var publish = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = revision!.RevisionId
        });
        publish.EnsureSuccessStatusCode();

        return (workload.WorkloadId, revision.RevisionId);
    }

    private sealed class NodeResponse
    {
        public Guid Id { get; set; }
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

    private sealed class CreateWorkloadRunResponse
    {
        public Guid RunId { get; set; }
        public string State { get; set; } = string.Empty;
        public string? RiskLevel { get; set; }
    }

    private sealed class WorkloadRunDetailResponse
    {
        public Guid RunId { get; set; }
        public List<Guid> NodeIds { get; set; } = new();
        public string? RiskLevel { get; set; }
    }
}
