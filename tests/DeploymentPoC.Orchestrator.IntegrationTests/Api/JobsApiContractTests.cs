using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public class JobsApiContractTests
{
    [Test]
    public async Task JobsApi_Implements_PostCancelAndStepsRoutes()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var createNodeResponse = await client.PostAsJsonAsync("/api/nodes", new
        {
            hostname = "NODE-CONTRACT-01",
            ipAddress = "10.0.0.10",
            description = "contract-target"
        });
        createNodeResponse.EnsureSuccessStatusCode();
        var createdNode = await createNodeResponse.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(createdNode, Is.Not.Null);

        var packageId = await CreatePackageIdentifierAsync(client, "nodejs-contract-01");

        var createJobResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId,
            targetVersion = "24.0.0",
            executionMode = "install",
            idempotencyKey = "idem-1",
            targets = new[] { createdNode!.Id }
        });
        createJobResponse.EnsureSuccessStatusCode();

        var createBody = await createJobResponse.Content.ReadFromJsonAsync<CreateJobResponse>();
        Assert.That(createBody, Is.Not.Null);

        var stepsResponse = await client.GetAsync($"/api/jobs/{createBody!.JobId}/steps");
        Assert.That(stepsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stepsPayload = await JsonDocument.ParseAsync(await stepsResponse.Content.ReadAsStreamAsync());
        Assert.That(stepsPayload.RootElement.TryGetProperty("steps", out var steps), Is.True);
        Assert.That(steps.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(steps.GetArrayLength(), Is.EqualTo(2));
        foreach (var step in steps.EnumerateArray())
        {
            Assert.That(step.TryGetProperty("stepId", out var stepId), Is.True);
            Assert.That(stepId.GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(step.TryGetProperty("name", out var name), Is.True);
            Assert.That(name.GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(step.TryGetProperty("status", out var status), Is.True);
            Assert.That(status.GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(step.TryGetProperty("sequence", out var sequence), Is.True);
            Assert.That(sequence.GetInt32(), Is.GreaterThan(0));
            Assert.That(step.TryGetProperty("reasonCode", out _), Is.True);
            Assert.That(step.TryGetProperty("telemetryRef", out _), Is.True);
        }

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/jobs/{createBody.JobId}/cancel",
            new { reason = "test" });
        Assert.That(cancelResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task JobsApi_Create_WithInvalidExecutionMode_ReturnsBadRequest()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "NODE-BAD-MODE-01", "10.0.0.21");
        var packageId = await CreatePackageIdentifierAsync(client, "nodejs-bad-mode-01");

        var response = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId,
            targetVersion = "24.0.0",
            executionMode = "not-a-real-mode",
            idempotencyKey = "idem-invalid-mode",
            targets = new[] { nodeId }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task JobsApi_Create_WithInvalidModel_ReturnsBadRequest()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId = "",
            targetVersion = "24.0.0",
            executionMode = "install",
            idempotencyKey = "",
            targets = Array.Empty<Guid>()
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task JobsApi_Create_IdempotencyReplaySamePayload_ReturnsExistingJob()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "NODE-IDEM-01", "10.0.0.31");
        var packageId = await CreatePackageIdentifierAsync(client, "nodejs-idem-01");
        var payload = new
        {
            packageId,
            targetVersion = "24.0.0",
            executionMode = "install",
            idempotencyKey = "idem-replay-1",
            targets = new[] { nodeId }
        };

        var firstResponse = await client.PostAsJsonAsync("/api/jobs", payload);
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<CreateJobResponse>();
        Assert.That(firstBody, Is.Not.Null);

        var secondResponse = await client.PostAsJsonAsync("/api/jobs", payload);
        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<CreateJobResponse>();
        Assert.That(secondBody, Is.Not.Null);
        Assert.That(secondBody!.JobId, Is.EqualTo(firstBody!.JobId));

        var listResponse = await client.GetAsync("/api/jobs");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var listBody = await listResponse.Content.ReadFromJsonAsync<List<JobListItemResponse>>();
        Assert.That(listBody, Is.Not.Null);
        Assert.That(listBody!.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task JobsApi_Create_IdempotencyReplayConflictingPayload_ReturnsConflict()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "NODE-IDEM-02", "10.0.0.41");
        var packageId = await CreatePackageIdentifierAsync(client, "nodejs-idem-02");
        var idempotencyKey = "idem-conflict-1";

        var firstResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId,
            targetVersion = "24.0.0",
            executionMode = "install",
            idempotencyKey,
            targets = new[] { nodeId }
        });
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var secondResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId,
            targetVersion = "24.1.0",
            executionMode = "install",
            idempotencyKey,
            targets = new[] { nodeId }
        });

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task JobsApi_Cancel_WithInvalidReason_ReturnsBadRequest()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var nodeId = await CreateNodeAsync(client, "NODE-CANCEL-01", "10.0.0.51");
        var packageId = await CreatePackageIdentifierAsync(client, "nodejs-cancel-01");
        var createJobResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId,
            targetVersion = "24.0.0",
            executionMode = "install",
            idempotencyKey = "idem-cancel-validation-1",
            targets = new[] { nodeId }
        });
        createJobResponse.EnsureSuccessStatusCode();
        var createdJob = await createJobResponse.Content.ReadFromJsonAsync<CreateJobResponse>();
        Assert.That(createdJob, Is.Not.Null);

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/jobs/{createdJob!.JobId}/cancel",
            new { reason = "x" });

        Assert.That(cancelResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static async Task<Guid> CreateNodeAsync(HttpClient client, string hostname, string ipAddress)
    {
        var createNodeResponse = await client.PostAsJsonAsync("/api/nodes", new
        {
            hostname,
            ipAddress,
            description = "contract-target"
        });
        createNodeResponse.EnsureSuccessStatusCode();
        var createdNode = await createNodeResponse.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(createdNode, Is.Not.Null);
        return createdNode!.Id;
    }

    private static async Task<string> CreatePackageIdentifierAsync(HttpClient client, string name)
    {
        var createPackageResponse = await client.PostAsJsonAsync("/api/packages", new
        {
            name,
            version = "24.0.0",
            sourcePath = $"/tmp/{name}",
            installType = "archive",
            installArgs = ""
        });
        createPackageResponse.EnsureSuccessStatusCode();

        var createdPackage = await createPackageResponse.Content.ReadFromJsonAsync<PackageResponse>();
        Assert.That(createdPackage, Is.Not.Null);
        return createdPackage!.Id.ToString();
    }

    private sealed class NodeResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class CreateJobResponse
    {
        public Guid JobId { get; set; }
    }

    private sealed class PackageResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class JobListItemResponse
    {
        public Guid JobId { get; set; }
        public string State { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
    }
}
