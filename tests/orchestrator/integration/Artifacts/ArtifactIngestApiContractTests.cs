using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Artifacts;

public sealed class ArtifactIngestApiContractTests
{
    [Test]
    public async Task ArtifactIngest_MissingMinimalRequiredFields_ReturnsDeterministicValidationErrors()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("fake-archive");
        content.Add(new ByteArrayContent(fileBytes), "file", "pkg.zip");

        var manifest = """
        {
          "packageId": "",
          "version": "",
          "channel": "",
          "artifactType": ""
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var payload = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Code, Is.EqualTo("validation_failed"));
        Assert.That(payload.Errors.Any(e => e.Field == "manifest.packageId"), Is.True);
        Assert.That(payload.Errors.Any(e => e.Field == "manifest.version"), Is.True);
        Assert.That(payload.Errors.Any(e => e.Field == "manifest.channel"), Is.True);
    }

    [Test]
    public async Task ArtifactIngest_VerificationFail_IsRejected()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("fake-msi")), "file", "agent.msi");
        var manifest = """
        {
          "packageId": "pkg-fail",
          "version": "1.0.0",
          "channel": "stable",
          "verificationResult": "fail"
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var payload = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Errors.Any(e => e.Field == "manifest.verificationResult"), Is.True);
    }

    [Test]
    public async Task ArtifactIngest_WarnElevatesRiskDefaults_AndPersistsProvenance()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("fake-exe")), "file", "agent.exe");
        var manifest = """
        {
          "packageId": "pkg-warn",
          "version": "2.0.0",
          "channel": "canary",
          "verificationResult": "warn"
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<ArtifactIngestResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ResolvedManifest.PolicyTags.RiskLevel, Is.EqualTo("high"));
        Assert.That(body.ResolvedManifest.PolicyTags.ApprovalRequired, Is.True);
        Assert.That(body.ResolvedManifest.PolicyTagsSources.RiskLevel, Is.EqualTo("default"));
        Assert.That(body.ResolvedManifest.PolicyTagsSources.ApprovalRequired, Is.EqualTo("default"));
    }

    [Test]
    public async Task ArtifactIngest_ConditionalFieldsRequired_WhenAdapterCannotResolve()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("mystery")), "file", "agent.unknown");
        var manifest = """
        {
          "packageId": "pkg-unknown",
          "version": "3.0.0",
          "channel": "test"
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Errors.Any(e => e.Field == "manifest.artifactType"), Is.True);
    }

    private sealed class ValidationErrorResponse
    {
        public string Code { get; set; } = string.Empty;
        public List<ValidationFieldError> Errors { get; set; } = new();
    }

    private sealed class ValidationFieldError
    {
        public string Field { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    private sealed class ArtifactIngestResponse
    {
        public ResolvedManifestResponse ResolvedManifest { get; set; } = new();
    }

    private sealed class ResolvedManifestResponse
    {
        public PolicyTagsResponse PolicyTags { get; set; } = new();
        public PolicyTagSourcesResponse PolicyTagsSources { get; set; } = new();
    }

    private sealed class PolicyTagsResponse
    {
        public string RiskLevel { get; set; } = string.Empty;
        public bool ApprovalRequired { get; set; }
    }

    private sealed class PolicyTagSourcesResponse
    {
        public string RiskLevel { get; set; } = string.Empty;
        public string ApprovalRequired { get; set; } = string.Empty;
    }
}
