using System.Text;
using System.Text.Json;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests;

[TestFixture]
public class ArtifactStoreServiceTests
{
    private string _tempRoot = string.Empty;
    private ArtifactStoreService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactStore:RootPath"] = _tempRoot
            })
            .Build();

        _service = new ArtifactStoreService(config);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Test]
    public async Task SaveArtifactAndManifestAsync_WritesBothFilesToSameDirectory()
    {
        // arrange
        var packageId = "test-package";
        var version = "1.0.0";
        var artifactBytes = Encoding.UTF8.GetBytes("artifact content");
        var manifestJson = "{\"PackageId\":\"test-package\",\"Version\":\"1.0.0\"}";

        // act
        await using (var stream = new MemoryStream(artifactBytes))
        {
            await _service.SaveArtifactAndManifestAsync(packageId, version, stream, manifestJson);
        }

        // assert
        var versionDir = Path.Combine(_tempRoot, packageId, version);
        Assert.That(Directory.Exists(versionDir), Is.True, "version directory should exist");
        Assert.That(File.Exists(Path.Combine(versionDir, "artifact.bin")), Is.True, "artifact.bin should exist");
        Assert.That(File.Exists(Path.Combine(versionDir, "resolved-manifest.json")), Is.True, "resolved-manifest.json should exist");

        var savedArtifact = await File.ReadAllBytesAsync(Path.Combine(versionDir, "artifact.bin"));
        Assert.That(savedArtifact, Is.EqualTo(artifactBytes));

        var savedManifest = await File.ReadAllTextAsync(Path.Combine(versionDir, "resolved-manifest.json"));
        Assert.That(savedManifest, Is.EqualTo(manifestJson));
    }

    [Test]
    public async Task SaveArtifactAndManifestAsync_WritesToSameDirectory_WhenCollisionOccurs()
    {
        // arrange - pre-create a version to force collision
        var packageId = "test-package";
        var version = "1.0.0";
        var existingDir = Path.Combine(_tempRoot, packageId, version);
        Directory.CreateDirectory(existingDir);
        await File.WriteAllTextAsync(Path.Combine(existingDir, "artifact.bin"), "existing");

        var artifactBytes = Encoding.UTF8.GetBytes("new artifact content");
        var manifestJson = "{\"PackageId\":\"test-package\",\"Version\":\"1.0.0\"}";

        // act
        await using (var stream = new MemoryStream(artifactBytes))
        {
            await _service.SaveArtifactAndManifestAsync(packageId, version, stream, manifestJson);
        }

        // assert - both should be in 1.0.0-1
        var versionDir = Path.Combine(_tempRoot, packageId, "1.0.0-1");
        Assert.That(Directory.Exists(versionDir), Is.True, "version directory 1.0.0-1 should exist");
        Assert.That(File.Exists(Path.Combine(versionDir, "artifact.bin")), Is.True, "artifact.bin should exist in 1.0.0-1");
        Assert.That(File.Exists(Path.Combine(versionDir, "resolved-manifest.json")), Is.True, "resolved-manifest.json should exist in 1.0.0-1");
    }

    [Test]
    public void ListArtifacts_ReturnsIncompleteEntries_WhenOnlyArtifactExists()
    {
        // arrange
        var packageId = "pkg-only-artifact";
        var version = "2.0.0";
        var versionDir = Path.Combine(_tempRoot, packageId, version);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "artifact.bin"), "artifact data");

        // act
        var results = _service.ListArtifacts();

        // assert
        Assert.That(results, Has.Count.EqualTo(1));
        var item = results[0];
        Assert.That(item.PackageId, Is.EqualTo(packageId));
        Assert.That(item.Version, Is.EqualTo(version));
        Assert.That(item.IsIncomplete, Is.True, "should be incomplete when only artifact exists");
    }

    [Test]
    public void ListArtifacts_ReturnsIncompleteEntries_WhenOnlyManifestExists()
    {
        // arrange
        var packageId = "pkg-only-manifest";
        var version = "3.0.0";
        var versionDir = Path.Combine(_tempRoot, packageId, version);
        Directory.CreateDirectory(versionDir);
        var manifest = new { PackageId = packageId, Version = version, Channel = "stable" };
        File.WriteAllText(Path.Combine(versionDir, "resolved-manifest.json"), JsonSerializer.Serialize(manifest));

        // act
        var results = _service.ListArtifacts();

        // assert
        Assert.That(results, Has.Count.EqualTo(1));
        var item = results[0];
        Assert.That(item.PackageId, Is.EqualTo(packageId));
        Assert.That(item.Version, Is.EqualTo(version));
        Assert.That(item.IsIncomplete, Is.True, "should be incomplete when only manifest exists");
        Assert.That(item.Channel, Is.EqualTo("stable"));
    }

    [Test]
    public void ListArtifacts_ReturnsCompleteEntries_WhenBothFilesExist()
    {
        // arrange
        var packageId = "pkg-complete";
        var version = "4.0.0";
        var versionDir = Path.Combine(_tempRoot, packageId, version);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "artifact.bin"), "artifact data");
        var manifest = new { PackageId = packageId, Version = version, Channel = "beta" };
        File.WriteAllText(Path.Combine(versionDir, "resolved-manifest.json"), JsonSerializer.Serialize(manifest));

        // act
        var results = _service.ListArtifacts();

        // assert
        Assert.That(results, Has.Count.EqualTo(1));
        var item = results[0];
        Assert.That(item.PackageId, Is.EqualTo(packageId));
        Assert.That(item.Version, Is.EqualTo(version));
        Assert.That(item.IsIncomplete, Is.False, "should not be incomplete when both files exist");
        Assert.That(item.Channel, Is.EqualTo("beta"));
    }

    [Test]
    public void DeleteArtifactAsync_RemovesEntireVersionDirectory()
    {
        // arrange
        var packageId = "pkg-to-delete";
        var version = "5.0.0";
        var versionDir = Path.Combine(_tempRoot, packageId, version);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "artifact.bin"), "artifact");
        File.WriteAllText(Path.Combine(versionDir, "resolved-manifest.json"), "{}");
        File.WriteAllText(Path.Combine(versionDir, "extra-file.txt"), "extra");

        // act
        var result = _service.DeleteArtifactAsync(packageId, version);

        // assert
        Assert.That(result, Is.True, "delete should return true");
        Assert.That(Directory.Exists(versionDir), Is.False, "version directory should be removed");
    }

    [Test]
    public void ExistsAny_ReturnsTrue_WhenArtifactExists()
    {
        // arrange
        var packageId = "pkg-exists-any";
        var version = "6.0.0";
        var versionDir = Path.Combine(_tempRoot, packageId, version);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "artifact.bin"), "artifact");

        // act & assert
        Assert.That(_service.ExistsAny(packageId, version), Is.True);
    }

    [Test]
    public void ExistsAny_ReturnsTrue_WhenManifestExists()
    {
        // arrange
        var packageId = "pkg-exists-manifest";
        var version = "7.0.0";
        var versionDir = Path.Combine(_tempRoot, packageId, version);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "resolved-manifest.json"), "{}");

        // act & assert
        Assert.That(_service.ExistsAny(packageId, version), Is.True);
    }

    [Test]
    public void ExistsAny_ReturnsFalse_WhenNeitherExists()
    {
        Assert.That(_service.ExistsAny("nonexistent", "1.0.0"), Is.False);
    }
}
