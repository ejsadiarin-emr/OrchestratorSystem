using System.Net;
using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public sealed class WorkloadRunsApiContractTests
{
    [Test]
    public async Task WorkloadRunsApi_CreateReplayGetStepsCancel_HappyPath()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "W1-RUN-NODE-01", "10.20.1.1");
        var pkgA = await CreatePackageAsync(client, "w1-run-pkg-a");
        var pkgB = await CreatePackageAsync(client, "w1-run-pkg-b");
        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB);

        var createResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "w1-run-idem-1",
            nodeIds = new[] { nodeId }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(created, Is.Not.Null);

        var replayResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "w1-run-idem-1",
            nodeIds = new[] { nodeId }
        });

        Assert.That(replayResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var replay = await replayResponse.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(replay, Is.Not.Null);
        Assert.That(replay!.RunId, Is.EqualTo(created!.RunId));

        var detailResponse = await client.GetAsync($"/api/workload-runs/{created.RunId}");
        Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await detailResponse.Content.ReadFromJsonAsync<WorkloadRunDetailResponse>();
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.NodeIds, Has.Count.EqualTo(1));

        var stepsResponse = await client.GetAsync($"/api/workload-runs/{created.RunId}/steps");
        Assert.That(stepsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var steps = await stepsResponse.Content.ReadFromJsonAsync<WorkloadRunStepsResponse>();
        Assert.That(steps, Is.Not.Null);
        Assert.That(steps!.Steps.Count, Is.EqualTo(2));
        Assert.That(steps.Steps.Select(s => s.PackageIndex), Is.EqualTo(new[] { 1, 2 }));

        var cancelResponse = await client.PostAsJsonAsync($"/api/workload-runs/{created.RunId}/cancel", new
        {
            reason = "operator-request"
        });

        Assert.That(cancelResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<CancelWorkloadRunResponse>();
        Assert.That(cancelled, Is.Not.Null);
        Assert.That(cancelled!.State, Is.EqualTo("Cancelled"));
    }

    [Test]
    public async Task WorkloadRunsApi_CreateConflictingReplay_ReturnsConflict()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "W1-RUN-NODE-02", "10.20.1.2");
        var pkgA = await CreatePackageAsync(client, "w1-run-pkg-c");
        var pkgB = await CreatePackageAsync(client, "w1-run-pkg-d");
        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB);

        var first = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "w1-run-idem-conflict",
            nodeIds = new[] { nodeId }
        });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var second = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "update",
            idempotencyKey = "w1-run-idem-conflict",
            nodeIds = new[] { nodeId }
        });

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task WorkloadRunsApi_CreateInvalidMode_ReturnsDeterministicValidationPayload()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "W1-RUN-NODE-03", "10.20.1.3");
        var pkgA = await CreatePackageAsync(client, "w1-run-pkg-e");
        var pkgB = await CreatePackageAsync(client, "w1-run-pkg-f");
        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB);

        var createResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "modify",
            idempotencyKey = "w1-run-bad-mode",
            nodeIds = new[] { nodeId }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var error = await createResponse.Content.ReadFromJsonAsync<ValidationErrorResponse>();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo("validation_failed"));
        Assert.That(error.Errors.Any(e => e.Field == "mode"), Is.True);
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

    private static async Task<(Guid WorkloadId, Guid RevisionId)> CreatePublishedWorkloadAsync(HttpClient client, Guid pkgA, Guid pkgB)
    {
        var createWorkload = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = $"workload-{Guid.NewGuid():N}"
        });
        createWorkload.EnsureSuccessStatusCode();
        var workload = await createWorkload.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(workload, Is.Not.Null);

        var createRevision = await client.PostAsJsonAsync($"/api/workloads/{workload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 },
                new { packageId = pkgB, packageIndex = 2 }
            }
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
    }

    private sealed class WorkloadRunDetailResponse
    {
        public Guid RunId { get; set; }
        public List<Guid> NodeIds { get; set; } = new();
    }

    private sealed class WorkloadRunStepsResponse
    {
        public List<WorkloadRunStepResponse> Steps { get; set; } = new();
    }

    private sealed class WorkloadRunStepResponse
    {
        public Guid PackageId { get; set; }
        public int PackageIndex { get; set; }
    }

    private sealed class CancelWorkloadRunResponse
    {
        public string State { get; set; } = string.Empty;
    }

    private sealed class ValidationErrorResponse
    {
        public string Code { get; set; } = string.Empty;
        public List<ValidationFieldError> Errors { get; set; } = new();
    }

    private sealed class ValidationFieldError
    {
        public string Field { get; set; } = string.Empty;
    }
}
