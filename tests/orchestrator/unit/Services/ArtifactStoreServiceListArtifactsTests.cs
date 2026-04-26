using DeploymentPoC.Orchestrator.Services;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Services;

public class ArtifactStoreServiceListArtifactsTests
{
    private static string _testRoot = string.Empty;

    [SetUp]
    public void Setup()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"artifact-store-tests-{Guid.NewGuid()}");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private ArtifactStoreService CreateService(string rootPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactStore:RootPath"] = rootPath
            })
            .Build();
        return new ArtifactStoreService(config);
    }

    [Test]
    public void ListArtifacts_WhenStoreDirectoryDoesNotExist_ReturnsEmptyList()
    {
        var nonExistentPath = Path.Combine(_testRoot, "does-not-exist");
        var service = CreateService(nonExistentPath);

        var result = service.ListArtifacts();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListArtifacts_WhenArtifactExists_ReturnsArtifactWithMetadata()
    {
        var storePath = Path.Combine(_testRoot, "store-with-artifact");
        var service = CreateService(storePath);

        var manifestJson = """{"packageId":"test-pkg","version":"1.0.0","channel":"stable","artifactType":"binary"}""";
        await using var artifactStream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });
        await service.SaveArtifactAndManifestAsync("test-pkg", "1.0.0", artifactStream, manifestJson);

        var result = service.ListArtifacts();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].PackageId, Is.EqualTo("test-pkg"));
        Assert.That(result[0].Version, Is.EqualTo("1.0.0"));
        Assert.That(result[0].Channel, Is.EqualTo("stable"));
        Assert.That(result[0].ArtifactType, Is.EqualTo("binary"));
        Assert.That(result[0].SizeBytes, Is.EqualTo(3));
        Assert.That(result[0].Digest, Is.Not.Null & Is.Not.Empty);
    }
}