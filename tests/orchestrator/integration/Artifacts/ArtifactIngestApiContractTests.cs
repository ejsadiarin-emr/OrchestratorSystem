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
    public async Task ArtifactIngest_AdminProvidedInstallAdapterArguments_ShouldHaveAdminSource()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("fake-msi")), "file", "agent.msi");
        var manifest = """
        {
          "packageId": "pkg-admin-args",
          "version": "1.0.0",
          "channel": "stable",
          "installAdapter": {
            "arguments": "/custom /args"
          }
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<ArtifactIngestResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ResolvedManifest.InstallAdapter.Arguments, Is.EqualTo("/custom /args"));
        Assert.That(body.ResolvedManifest.Sources.InstallAdapterSources.Arguments, Is.EqualTo("admin"));
    }

    [Test]
    public async Task ArtifactIngest_DefaultInstallAdapterArguments_ShouldHaveDefaultSource()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("fake-msi")), "file", "agent.msi");
        var manifest = """
        {
          "packageId": "pkg-default-args",
          "version": "1.0.0",
          "channel": "stable"
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<ArtifactIngestResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ResolvedManifest.InstallAdapter.Arguments, Is.EqualTo("/qn /norestart"));
        Assert.That(body.ResolvedManifest.Sources.InstallAdapterSources.Arguments, Is.EqualTo("default"));
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

    [Test]
    public async Task ArtifactIngest_ConditionalFieldsRequired_WhenArtifactTypeUnknown_ReturnsFieldLevelErrors()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("mystery")), "file", "agent.unknown");
        var manifest = """
        {
          "packageId": "pkg-unknown-type",
          "version": "3.0.0",
          "channel": "test",
          "artifactType": "unknown"
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Errors.Any(e => e.Field == "manifest.installAdapter.type"), Is.True);
        Assert.That(body.Errors.Any(e => e.Field == "manifest.installAdapter.command"), Is.True);
        Assert.That(body.Errors.Any(e => e.Field == "manifest.installAdapter.arguments"), Is.True);
        Assert.That(body.Errors.Any(e => e.Field == "manifest.installAdapter.expectedExitCodes"), Is.True);
        Assert.That(body.Errors.Any(e => e.Field == "manifest.installAdapter.timeoutSeconds"), Is.True);
        Assert.That(body.Errors.Any(e => e.Field == "manifest.detection.type"), Is.True);
        Assert.That(body.Errors.Any(e => e.Field == "manifest.detection.path"), Is.True);
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
        public string PackageId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string ArtifactType { get; set; } = string.Empty;
        public PolicyTagsResponse PolicyTags { get; set; } = new();
        public PolicyTagSourcesResponse PolicyTagsSources { get; set; } = new();
        public InstallAdapterResponse InstallAdapter { get; set; } = new();
        public DetectionResponse Detection { get; set; } = new();
        public OriginMetadataResponse OriginMetadata { get; set; } = new();
        public SourcesResponse Sources { get; set; } = new();
    }

    private sealed class InstallAdapterResponse
    {
        public string Type { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public List<int> ExpectedExitCodes { get; set; } = new();
        public int TimeoutSeconds { get; set; }
    }

    private sealed class DetectionResponse
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    private sealed class OriginMetadataResponse
    {
        public string Source { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string IngestedBy { get; set; } = string.Empty;
        public string VerificationResult { get; set; } = string.Empty;
    }

    private sealed class PolicyTagsResponse
    {
        public string RiskLevel { get; set; } = string.Empty;
        public bool ApprovalRequired { get; set; }
        public string RetryabilityClass { get; set; } = string.Empty;
        public string IdempotencyMode { get; set; } = string.Empty;
    }

    private sealed class PolicyTagSourcesResponse
    {
        public string RiskLevel { get; set; } = string.Empty;
        public string ApprovalRequired { get; set; } = string.Empty;
    }

    private sealed class SourcesResponse
    {
        public string ArtifactType { get; set; } = string.Empty;
        public string InstallAdapter { get; set; } = string.Empty;
        public string Detection { get; set; } = string.Empty;
        public string PolicyTagsComposite { get; set; } = string.Empty;
        public PolicyTagSourcesResponse PolicyTags { get; set; } = new();
        public InstallAdapterSourceResponse InstallAdapterSources { get; set; } = new();
        public DetectionSourceResponse DetectionSources { get; set; } = new();
    }

    private sealed class InstallAdapterSourceResponse
    {
        public string Type { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string ExpectedExitCodes { get; set; } = string.Empty;
        public string TimeoutSeconds { get; set; } = string.Empty;
    }

    private sealed class DetectionSourceResponse
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    [Test]
    public async Task ArtifactIngest_ResolvedManifest_MatchesSchema()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("fake-msi")), "file", "agent.msi");
        var manifest = """
        {
          "packageId": "pkg-schema",
          "version": "1.0.0",
          "channel": "stable"
        }
        """;
        content.Add(new StringContent(manifest, Encoding.UTF8, "application/json"), "manifest");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<ArtifactIngestResponse>();
        Assert.That(body, Is.Not.Null);

        // Schema validation: all required fields must be present
        Assert.That(body!.ResolvedManifest.PackageId, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.Version, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.Channel, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.ArtifactType, Is.Not.Null.Or.Empty);

        Assert.That(body.ResolvedManifest.InstallAdapter, Is.Not.Null);
        Assert.That(body.ResolvedManifest.InstallAdapter.Type, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.InstallAdapter.Command, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.InstallAdapter.ExpectedExitCodes, Is.Not.Null);
        Assert.That(body.ResolvedManifest.InstallAdapter.ExpectedExitCodes.Count, Is.GreaterThan(0));
        Assert.That(body.ResolvedManifest.InstallAdapter.TimeoutSeconds, Is.GreaterThan(0));

        Assert.That(body.ResolvedManifest.Detection, Is.Not.Null);
        Assert.That(body.ResolvedManifest.Detection.Type, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.Detection.Path, Is.Not.Null.Or.Empty);

        Assert.That(body.ResolvedManifest.OriginMetadata, Is.Not.Null);
        Assert.That(body.ResolvedManifest.OriginMetadata.Source, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.OriginMetadata.Publisher, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.OriginMetadata.IngestedBy, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.OriginMetadata.VerificationResult, Is.Not.Null.Or.Empty);

        Assert.That(body.ResolvedManifest.PolicyTags, Is.Not.Null);
        Assert.That(body.ResolvedManifest.PolicyTags.RiskLevel, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.PolicyTags.RetryabilityClass, Is.Not.Null.Or.Empty);
        Assert.That(body.ResolvedManifest.PolicyTags.IdempotencyMode, Is.Not.Null.Or.Empty);

        Assert.That(body.ResolvedManifest.Sources, Is.Not.Null);
        Assert.That(body.ResolvedManifest.Sources.PolicyTags, Is.Not.Null);
        Assert.That(body.ResolvedManifest.Sources.InstallAdapterSources, Is.Not.Null);
        Assert.That(body.ResolvedManifest.Sources.DetectionSources, Is.Not.Null);
    }
}
