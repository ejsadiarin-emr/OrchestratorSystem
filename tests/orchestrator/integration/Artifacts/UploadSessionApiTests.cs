using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Artifacts;

public sealed class UploadSessionApiTests
{
    [Test]
    public async Task CreateUploadSession_Returns201WithSessionId()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/artifacts/upload-sessions", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.SessionId, Is.Not.Null.Or.Empty);
    }

    [Test]
    public async Task UploadChunk_ToSession_Succeeds()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsync("/api/artifacts/upload-sessions", null);
        var session = await createResponse.Content.ReadFromJsonAsync<CreateSessionResponse>();
        Assert.That(session, Is.Not.Null);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("chunk-data")), "chunk", "chunk.bin");

        var response = await client.PostAsync($"/api/artifacts/upload-sessions/{session!.SessionId}/chunks?index=0&totalChunks=1", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task CompleteSession_IngestsArtifact()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var manifest = """
        {
          "packageId": "test-upload-session",
          "version": "1.0.0",
          "channel": "stable",
          "artifactType": "msi"
        }
        """;

        using var manifestContent = new StringContent(manifest, Encoding.UTF8, "application/json");
        var createResponse = await client.PostAsync("/api/artifacts/upload-sessions", manifestContent);
        var session = await createResponse.Content.ReadFromJsonAsync<CreateSessionResponse>();
        Assert.That(session, Is.Not.Null);

        using var chunkContent = new MultipartFormDataContent();
        chunkContent.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("fake-msi")), "chunk", "chunk.bin");
        var chunkResponse = await client.PostAsync($"/api/artifacts/upload-sessions/{session!.SessionId}/chunks?index=0&totalChunks=1", chunkContent);
        Assert.That(chunkResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var completeResponse = await client.PostAsync($"/api/artifacts/upload-sessions/{session.SessionId}/complete", null);

        Assert.That(completeResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await completeResponse.Content.ReadFromJsonAsync<ArtifactIngestResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ResolvedManifest.PackageId, Is.EqualTo("test-upload-session"));
        Assert.That(body.ResolvedManifest.Version, Is.EqualTo("1.0.0"));
    }

    private sealed class CreateSessionResponse
    {
        public string SessionId { get; set; } = string.Empty;
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
    }
}
