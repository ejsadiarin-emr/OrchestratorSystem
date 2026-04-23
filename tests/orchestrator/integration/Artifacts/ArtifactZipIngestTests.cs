using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Artifacts;

public sealed class ArtifactZipIngestTests
{
    [Test]
    public async Task SingleZipIngest_ExtractsManifestAndMedia()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var manifest = """
        {
          "packageId": "pkg-zip-single",
          "version": "1.0.0",
          "channel": "stable"
        }
        """;

        using var zipStream = CreateSingleZip("pkg-zip-single.msi", Encoding.UTF8.GetBytes("fake-msi"), manifest);

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(zipStream), "file", "artifact.zip");

        var response = await client.PostAsync("/api/artifacts", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await response.Content.ReadFromJsonAsync<ArtifactIngestResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ResolvedManifest.PackageId, Is.EqualTo("pkg-zip-single"));
        Assert.That(body.ResolvedManifest.Version, Is.EqualTo("1.0.0"));
        Assert.That(body.ResolvedManifest.Channel, Is.EqualTo("stable"));
    }

    [Test]
    public async Task BulkZipIngest_HandlesMultiplePairs()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var manifestA = """
        {
          "packageId": "pkg-bulk-a",
          "version": "1.0.0",
          "channel": "stable"
        }
        """;

        var manifestB = """
        {
          "packageId": "pkg-bulk-b",
          "version": "2.0.0",
          "channel": "canary"
        }
        """;

        var artifacts = new List<(string mediaFileName, byte[] mediaBytes, string manifestJson)>
        {
            ("pkg-bulk-a.msi", Encoding.UTF8.GetBytes("fake-msi-a"), manifestA),
            ("pkg-bulk-b.msi", Encoding.UTF8.GetBytes("fake-msi-b"), manifestB)
        };

        using var zipStream = CreateBulkZip(artifacts);

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(zipStream), "file", "bulk.zip");

        var response = await client.PostAsync("/api/artifacts/bulk", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BulkIngestResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Results.Count, Is.EqualTo(2));
        Assert.That(body.Results.All(r => r.Status == "success"), Is.True);
        Assert.That(body.Results.Any(r => r.Artifact?.PackageId == "pkg-bulk-a"), Is.True);
        Assert.That(body.Results.Any(r => r.Artifact?.PackageId == "pkg-bulk-b"), Is.True);
    }

    [Test]
    public async Task BulkZipIngest_RejectsUnpairedFiles()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var manifestA = """
        {
          "packageId": "pkg-paired",
          "version": "1.0.0",
          "channel": "stable"
        }
        """;

        var artifacts = new List<(string mediaFileName, byte[] mediaBytes, string manifestJson)>
        {
            ("pkg-paired.msi", Encoding.UTF8.GetBytes("fake-msi"), manifestA)
        };

        var unpaired = new List<(string fileName, byte[] bytes)>
        {
            ("lonely-file.txt", Encoding.UTF8.GetBytes("i have no manifest"))
        };

        using var zipStream = CreateBulkZip(artifacts, unpaired);

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(zipStream), "file", "bulk-with-unpaired.zip");

        var response = await client.PostAsync("/api/artifacts/bulk", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BulkIngestResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Results.Any(r => r.Status == "success" && r.Artifact?.PackageId == "pkg-paired"), Is.True);
        Assert.That(body.Results.Any(r => r.Status == "failed" && r.Reason?.Contains("lonely-file.txt") == true), Is.True);
    }

    private static Stream CreateSingleZip(string mediaFileName, byte[] mediaBytes, string manifestJson)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mediaEntry = archive.CreateEntry(mediaFileName);
            using (var entryStream = mediaEntry.Open())
            {
                entryStream.Write(mediaBytes, 0, mediaBytes.Length);
            }

            var manifestEntryName = Path.GetFileNameWithoutExtension(mediaFileName) + ".manifest.json";
            var manifestEntry = archive.CreateEntry(manifestEntryName);
            using (var entryStream = manifestEntry.Open())
            {
                var bytes = Encoding.UTF8.GetBytes(manifestJson);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static Stream CreateBulkZip(List<(string mediaFileName, byte[] mediaBytes, string manifestJson)> artifacts, List<(string fileName, byte[] bytes)>? unpaired = null)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var artifact in artifacts)
            {
                var mediaEntry = archive.CreateEntry(artifact.mediaFileName);
                using (var entryStream = mediaEntry.Open())
                {
                    entryStream.Write(artifact.mediaBytes, 0, artifact.mediaBytes.Length);
                }

                var manifestEntryName = Path.GetFileNameWithoutExtension(artifact.mediaFileName) + ".manifest.json";
                var manifestEntry = archive.CreateEntry(manifestEntryName);
                using (var entryStream = manifestEntry.Open())
                {
                    var bytes = Encoding.UTF8.GetBytes(artifact.manifestJson);
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }

            if (unpaired is not null)
            {
                foreach (var u in unpaired)
                {
                    var entry = archive.CreateEntry(u.fileName);
                    using (var entryStream = entry.Open())
                    {
                        entryStream.Write(u.bytes, 0, u.bytes.Length);
                    }
                }
            }
        }

        ms.Position = 0;
        return ms;
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

    private sealed class BulkIngestResponse
    {
        public List<BulkResultItem> Results { get; set; } = new();
    }

    private sealed class BulkResultItem
    {
        public string? FileName { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public BulkResultArtifact? Artifact { get; set; }
    }

    private sealed class BulkResultArtifact
    {
        public string PackageId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    [Test]
    public async Task DeleteArtifact_RemovesArtifactAndReturnsNoContent()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var manifest = """
        {
          "packageId": "pkg-delete-test",
          "version": "1.0.0",
          "channel": "stable"
        }
        """;

        using var zipStream = CreateSingleZip("pkg-delete-test.msi", Encoding.UTF8.GetBytes("fake-msi"), manifest);

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(zipStream), "file", "artifact.zip");

        var ingestResponse = await client.PostAsync("/api/artifacts", content);
        Assert.That(ingestResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var deleteResponse = await client.DeleteAsync("/api/artifacts/pkg-delete-test/1.0.0");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var secondDelete = await client.DeleteAsync("/api/artifacts/pkg-delete-test/1.0.0");
        Assert.That(secondDelete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
