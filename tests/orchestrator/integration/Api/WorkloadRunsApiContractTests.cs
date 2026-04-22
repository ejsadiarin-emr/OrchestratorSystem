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

    [Test]
    public async Task WorkloadRunsApi_GetSteps_UpdateMode_ReturnsDeltaSteps()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "W1-DELTA-NODE-01", "10.20.4.1");
        var pkgA = await CreatePackageAsync(client, "delta-pkg-a");
        var pkgB = await CreatePackageAsync(client, "delta-pkg-b");
        var pkgC = await CreatePackageAsync(client, "delta-pkg-c");
        var pkgD = await CreatePackageAsync(client, "delta-pkg-d");

        var createWorkload = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = $"workload-delta-{Guid.NewGuid():N}"
        });
        createWorkload.EnsureSuccessStatusCode();
        var workload = await createWorkload.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(workload, Is.Not.Null);

        var createRev1 = await client.PostAsJsonAsync($"/api/workloads/{workload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 },
                new { packageId = pkgB, packageIndex = 2 }
            }
        });
        createRev1.EnsureSuccessStatusCode();
        var rev1 = await createRev1.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(rev1, Is.Not.Null);

        var publish1 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = rev1!.RevisionId
        });
        publish1.EnsureSuccessStatusCode();

        var createRev2 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/revisions", new
        {
            version = "2.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 },
                new { packageId = pkgC, packageIndex = 2 },
                new { packageId = pkgD, packageIndex = 3 }
            }
        });
        createRev2.EnsureSuccessStatusCode();
        var rev2 = await createRev2.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(rev2, Is.Not.Null);

        var publish2 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = rev2!.RevisionId
        });
        publish2.EnsureSuccessStatusCode();

        var createRun = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = rev2.RevisionId,
            mode = "update",
            idempotencyKey = "delta-update-test-1",
            nodeIds = new[] { nodeId }
        });
        Assert.That(createRun.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var run = await createRun.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(run, Is.Not.Null);

        var stepsResponse = await client.GetAsync($"/api/workload-runs/{run!.RunId}/steps");
        Assert.That(stepsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var steps = await stepsResponse.Content.ReadFromJsonAsync<WorkloadRunStepsResponse>();
        Assert.That(steps, Is.Not.Null);
        Assert.That(steps!.Steps.Count, Is.EqualTo(2));

        var index2Step = steps.Steps.Single(s => s.PackageIndex == 2);
        Assert.That(index2Step.PackageId, Is.EqualTo(pkgC));
        Assert.That(index2Step.Action, Is.EqualTo("change"));

        var index3Step = steps.Steps.Single(s => s.PackageIndex == 3);
        Assert.That(index3Step.PackageId, Is.EqualTo(pkgD));
        Assert.That(index3Step.Action, Is.EqualTo("add"));

        var removedSteps = steps.Steps.Where(s => s.Action == "remove").ToList();
        Assert.That(removedSteps.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task WorkloadRunsApi_GetSteps_InstallMode_ReturnsAllPackagesWithInstallAction()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "W1-INSTALL-ACTION-NODE", "10.20.4.2");
        var pkgA = await CreatePackageAsync(client, "install-action-pkg-a");
        var pkgB = await CreatePackageAsync(client, "install-action-pkg-b");
        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB);

        var createResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "install-action-test-1",
            nodeIds = new[] { nodeId }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(created, Is.Not.Null);

        var stepsResponse = await client.GetAsync($"/api/workload-runs/{created!.RunId}/steps");
        Assert.That(stepsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var steps = await stepsResponse.Content.ReadFromJsonAsync<WorkloadRunStepsResponse>();
        Assert.That(steps, Is.Not.Null);
        Assert.That(steps!.Steps.Count, Is.EqualTo(2));
        Assert.That(steps.Steps.All(s => s.Action == "install"), Is.True);
    }

    [Test]
    public async Task WorkloadRunsApi_GetSteps_UpdateMode_WithRemovedPackage_ReturnsRemoveAction()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "W1-REMOVE-NODE-01", "10.20.4.3");
        var pkgA = await CreatePackageAsync(client, "remove-pkg-a");
        var pkgB = await CreatePackageAsync(client, "remove-pkg-b");
        var pkgC = await CreatePackageAsync(client, "remove-pkg-c");

        var createWorkload = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = $"workload-remove-{Guid.NewGuid():N}"
        });
        createWorkload.EnsureSuccessStatusCode();
        var workload = await createWorkload.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(workload, Is.Not.Null);

        var createRev1 = await client.PostAsJsonAsync($"/api/workloads/{workload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 },
                new { packageId = pkgB, packageIndex = 2 }
            }
        });
        createRev1.EnsureSuccessStatusCode();
        var rev1 = await createRev1.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(rev1, Is.Not.Null);

        var publish1 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = rev1!.RevisionId
        });
        publish1.EnsureSuccessStatusCode();

        var createRev2 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/revisions", new
        {
            version = "2.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 },
                new { packageId = pkgC, packageIndex = 3 }
            }
        });
        createRev2.EnsureSuccessStatusCode();
        var rev2 = await createRev2.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(rev2, Is.Not.Null);

        var publish2 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = rev2!.RevisionId
        });
        publish2.EnsureSuccessStatusCode();

        var createRun = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = rev2.RevisionId,
            mode = "update",
            idempotencyKey = "remove-test-1",
            nodeIds = new[] { nodeId }
        });
        Assert.That(createRun.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var run = await createRun.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(run, Is.Not.Null);

        var stepsResponse = await client.GetAsync($"/api/workload-runs/{run!.RunId}/steps");
        Assert.That(stepsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var steps = await stepsResponse.Content.ReadFromJsonAsync<WorkloadRunStepsResponse>();
        Assert.That(steps, Is.Not.Null);

        var unchangedSteps = steps.Steps.Where(s => s.PackageIndex == 1).ToList();
        Assert.That(unchangedSteps.Count, Is.EqualTo(0), "Unchanged package at index 1 should not be in delta");

        var addStep = steps.Steps.SingleOrDefault(s => s.PackageIndex == 3);
        Assert.That(addStep, Is.Not.Null);
        Assert.That(addStep!.PackageId, Is.EqualTo(pkgC));
        Assert.That(addStep.Action, Is.EqualTo("add"));

        var removeStep = steps.Steps.SingleOrDefault(s => s.Action == "remove");
        Assert.That(removeStep, Is.Not.Null);
        Assert.That(removeStep!.PackageId, Is.EqualTo(pkgB));
        Assert.That(removeStep.PackageIndex, Is.EqualTo(2));
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
        public string Action { get; set; } = string.Empty;
    }

    private sealed class CancelWorkloadRunResponse
    {
        public string State { get; set; } = string.Empty;
    }

    [Test]
    public async Task WorkloadRunsApi_GetSteps_ReturnsSnapshotPackagesFromCreationTime()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "W1-SNAP-NODE-01", "10.20.3.1");
        var pkgA = await CreatePackageAsync(client, "snap-pkg-a");
        var pkgB = await CreatePackageAsync(client, "snap-pkg-b");
        var workload = await CreatePublishedWorkloadAsync(client, pkgA, pkgB);

        var createResponse = await client.PostAsJsonAsync("/api/workload-runs", new
        {
            workloadId = workload.WorkloadId,
            revisionId = workload.RevisionId,
            mode = "install",
            idempotencyKey = "snapshot-test-1",
            nodeIds = new[] { nodeId }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWorkloadRunResponse>();
        Assert.That(created, Is.Not.Null);

        var stepsResponse = await client.GetAsync($"/api/workload-runs/{created!.RunId}/steps");
        Assert.That(stepsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var steps = await stepsResponse.Content.ReadFromJsonAsync<WorkloadRunStepsResponse>();
        Assert.That(steps, Is.Not.Null);
        Assert.That(steps!.Steps.Count, Is.EqualTo(2));
        Assert.That(steps.Steps.Select(s => s.PackageIndex), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(steps.Steps.Select(s => s.PackageId).OrderBy(id => id),
            Is.EquivalentTo(new[] { pkgA, pkgB }.OrderBy(id => id)));
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
