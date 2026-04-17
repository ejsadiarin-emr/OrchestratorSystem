using System.Net;
using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public sealed class JobsApiContractTests
{
    [Test]
    public async Task JobsApi_MutationEndpoints_ReturnExactDeprecated410Contract()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/jobs", new
        {
            packageId = Guid.NewGuid(),
            targetVersion = "24.0.0",
            executionMode = "install",
            idempotencyKey = "legacy-jobs-deprecated-check",
            targets = new[] { Guid.NewGuid() }
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Gone));
        var createBody = await createResponse.Content.ReadFromJsonAsync<DeprecatedEndpointResponse>();
        Assert.That(createBody, Is.Not.Null);
        Assert.That(createBody!.Code, Is.EqualTo("deprecated_endpoint"));
        Assert.That(createBody.Message, Is.EqualTo("Use /api/workload-runs"));
        Assert.That(createBody.ReplacementPath, Is.EqualTo("/api/workload-runs"));

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/jobs/{Guid.NewGuid()}/cancel",
            new { reason = "legacy-endpoint-check" });

        Assert.That(cancelResponse.StatusCode, Is.EqualTo(HttpStatusCode.Gone));
        var cancelBody = await cancelResponse.Content.ReadFromJsonAsync<DeprecatedEndpointResponse>();
        Assert.That(cancelBody, Is.Not.Null);
        Assert.That(cancelBody!.Code, Is.EqualTo("deprecated_endpoint"));
        Assert.That(cancelBody.Message, Is.EqualTo("Use /api/workload-runs"));
        Assert.That(cancelBody.ReplacementPath, Is.EqualTo("/api/workload-runs"));
    }

    private sealed class DeprecatedEndpointResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ReplacementPath { get; set; } = string.Empty;
    }
}
