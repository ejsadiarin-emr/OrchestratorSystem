using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Artifacts;

public class ArtifactTransportTests
{
    [Test]
    public async Task ArtifactEndpoint_SupportsHttpRangeRequests()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var packageId = $"testpkg-{Guid.NewGuid():N}";
        var version = "1.0.0";
        var payload = Encoding.UTF8.GetBytes("0123456789ABCDEFGHIJ");
        SeedArtifact(packageId, version, payload);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/artifacts/{packageId}/{version}");
        request.Headers.Range = new RangeHeaderValue(0, 9);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
        Assert.That(response.Content.Headers.ContentRange, Is.Not.Null);
        Assert.That(response.Content.Headers.ContentRange!.From, Is.EqualTo(0));
        Assert.That(response.Content.Headers.ContentRange.To, Is.EqualTo(9));
        Assert.That(response.Content.Headers.ContentRange.Length, Is.EqualTo(payload.Length));
        Assert.That(body, Is.EqualTo(payload[..10]));
    }

    [Test]
    public async Task ArtifactEndpoint_HeadReturnsMetadata_WhenArtifactExists()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var packageId = $"testpkg-{Guid.NewGuid():N}";
        var version = "1.0.1";
        var payload = Encoding.UTF8.GetBytes("artifact-metadata-payload");
        SeedArtifact(packageId, version, payload);

        using var request = new HttpRequestMessage(HttpMethod.Head, $"/api/artifacts/{packageId}/{version}");
        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(payload.Length));
        Assert.That(response.Headers.ETag, Is.Not.Null);
        Assert.That(response.Headers.ETag!.ToString(), Does.Match("^W/\"[0-9a-f]+-[0-9a-f]+\"$"));
    }

    [Test]
    public async Task ArtifactEndpoint_MissingArtifact_ReturnsNotFound()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var packageId = $"missing-{Guid.NewGuid():N}";
        var version = "9.9.9";

        var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/artifacts/{packageId}/{version}"));
        var get = await client.GetAsync($"/api/artifacts/{packageId}/{version}");

        Assert.That(head.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase("bad$id", "1.0.0")]
    [TestCase("pkg", "1.0.0 beta")]
    [TestCase("pkg", "1:0:0")]
    public async Task ArtifactEndpoint_InvalidSegments_AreRejected(string packageId, string version)
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/artifacts/{packageId}/{version}"));
        var get = await client.GetAsync($"/api/artifacts/{packageId}/{version}");

        Assert.That(head.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ArtifactEndpoint_InvalidEncodedSeparatorSegment_IsRejected()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/artifacts/pkg%5Cbad/1.0.0"));
        var get = await client.GetAsync("/api/artifacts/pkg%5Cbad/1.0.0");

        Assert.That(head.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ArtifactEndpoint_MissingFileDuringAccess_ReturnsNotFoundInsteadOfServerError()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var packageId = $"race-{Guid.NewGuid():N}";
        var version = "2.0.0";
        var payload = Encoding.UTF8.GetBytes("race-window-payload");
        SeedArtifact(packageId, version, payload);

        var path = Path.Combine(AppContext.BaseDirectory, "artifacts", packageId, version, "artifact.bin");
        File.Delete(path);

        var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/artifacts/{packageId}/{version}"));
        var get = await client.GetAsync($"/api/artifacts/{packageId}/{version}");

        Assert.That(head.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static void SeedArtifact(string packageId, string version, byte[] bytes)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "artifacts", packageId, version, "artifact.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
