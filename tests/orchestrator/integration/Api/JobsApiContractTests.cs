using System.Net;
using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public sealed class JobsApiContractTests
{
    [Test]
    public async Task JobsApi_MutationEndpoints_AreRemoved()
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

        Assert.That(
            createResponse.StatusCode,
            Is.AnyOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed));

        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/jobs/{Guid.NewGuid()}/cancel",
            new { reason = "legacy-endpoint-check" });

        Assert.That(
            cancelResponse.StatusCode,
            Is.AnyOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed));
    }
}
