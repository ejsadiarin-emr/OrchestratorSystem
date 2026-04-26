using System.Net;
using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public class EnrollmentApiContractTests
{
    [Test]
    public async Task Enrollment_IssueToken_ListTokens_ConsumeToken_CreatesNode()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Issue token
        var issueResponse = await client.PostAsJsonAsync("/api/nodes/enroll", new
        {
            requestedBy = "ops.admin",
            orchestratorUrl = "https://orchestrator.local:5000",
            ttlMinutes = 20
        });
        Assert.That(issueResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = await issueResponse.Content.ReadFromJsonAsync<EnrollmentTokenResponse>();
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Token, Is.Not.Null.And.Not.Empty);
        Assert.That(token.Used, Is.False);
        Assert.That(token.SingleUse, Is.True);

        // List tokens
        var listResponse = await client.GetAsync("/api/enrollment-tokens");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var tokens = await listResponse.Content.ReadFromJsonAsync<List<EnrollmentTokenResponse>>();
        Assert.That(tokens, Is.Not.Null);
        Assert.That(tokens!.Any(t => t.Token == token.Token), Is.True);

        // Consume token
        var consumeResponse = await client.PostAsJsonAsync($"/api/enrollment-tokens/{token.Token}/consume", new { });
        Assert.That(consumeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var node = await consumeResponse.Content.ReadFromJsonAsync<NodeResponse>();
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Hostname, Is.Not.Null.And.Not.Empty);
        Assert.That(node.Status, Is.EqualTo("online"));

        // Verify token is now used
        var listAfterConsume = await client.GetAsync("/api/enrollment-tokens");
        var tokensAfter = await listAfterConsume.Content.ReadFromJsonAsync<List<EnrollmentTokenResponse>>();
        var consumedToken = tokensAfter!.Single(t => t.Token == token.Token);
        Assert.That(consumedToken.Used, Is.True);

        // Verify duplicate consume returns conflict
        var duplicateConsume = await client.PostAsJsonAsync($"/api/enrollment-tokens/{token.Token}/consume", new { });
        Assert.That(duplicateConsume.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Enrollment_ConsumeInvalidToken_ReturnsNotFound()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/enrollment-tokens/nonexistent-token/consume", new { });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Enrollment_IssueToken_InvalidTtl_ReturnsBadRequest()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/nodes/enroll", new
        {
            requestedBy = "ops.admin",
            orchestratorUrl = "https://orchestrator.local:5000",
            ttlMinutes = 200
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private sealed class EnrollmentTokenResponse
    {
        public string Token { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
        public string OrchestratorUrl { get; set; } = string.Empty;
        public bool SingleUse { get; set; }
        public bool Used { get; set; }
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
