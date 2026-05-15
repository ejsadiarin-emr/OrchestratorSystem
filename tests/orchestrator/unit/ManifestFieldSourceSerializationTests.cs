using System.Text.Json;
using DeploymentPoC.Orchestrator.Services;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests;

public class ManifestFieldSourceSerializationTests
{
    [Test]
    public void ManifestFieldSource_SerializesAsString_NotAsInteger()
    {
        var manifest = new ResolvedManifestSources
        {
            ArtifactType = ManifestFieldSource.Admin,
            InstallAdapter = ManifestFieldSource.Template,
            Detection = ManifestFieldSource.Analyzer,
            PolicyTagsComposite = ManifestFieldSource.Default
        };

        var json = JsonSerializer.Serialize(manifest);

        Assert.That(json, Does.Contain("\"Admin\""), "Expected Admin as string");
        Assert.That(json, Does.Contain("\"Template\""), "Expected Template as string");
        Assert.That(json, Does.Contain("\"Analyzer\""), "Expected Analyzer as string");
        Assert.That(json, Does.Contain("\"Default\""), "Expected Default as string");
        Assert.That(json, Does.Not.Contain("\"ArtifactType\":0"), "Should not serialize as integer 0");
        Assert.That(json, Does.Not.Contain("\"ArtifactType\":1"), "Should not serialize as integer 1");
    }

    [Test]
    public void ManifestFieldSource_RoundtripsThroughSerialization()
    {
        var manifest = new ResolvedManifestSources
        {
            ArtifactType = ManifestFieldSource.Admin,
            InstallAdapter = ManifestFieldSource.Template,
            Detection = ManifestFieldSource.Analyzer,
            PolicyTagsComposite = ManifestFieldSource.Default
        };

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<ResolvedManifestSources>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.ArtifactType, Is.EqualTo(ManifestFieldSource.Admin));
        Assert.That(deserialized.InstallAdapter, Is.EqualTo(ManifestFieldSource.Template));
        Assert.That(deserialized.Detection, Is.EqualTo(ManifestFieldSource.Analyzer));
        Assert.That(deserialized.PolicyTagsComposite, Is.EqualTo(ManifestFieldSource.Default));
    }

    [Test]
    public void ManifestFieldSource_DeserializesFromIntegerValues_ForBackwardCompatibility()
    {
        var json = """{"ArtifactType":0,"InstallAdapter":1,"Detection":2,"PolicyTagsComposite":3}""";

        var deserialized = JsonSerializer.Deserialize<ResolvedManifestSources>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.ArtifactType, Is.EqualTo(ManifestFieldSource.Admin));
        Assert.That(deserialized.InstallAdapter, Is.EqualTo(ManifestFieldSource.Template));
        Assert.That(deserialized.Detection, Is.EqualTo(ManifestFieldSource.Analyzer));
        Assert.That(deserialized.PolicyTagsComposite, Is.EqualTo(ManifestFieldSource.Default));
    }

    [Test]
    public void ResolvedManifest_SourcesField_RoundtripsWithStringEnumValues()
    {
        var original = new ResolvedManifest
        {
            PackageId = "test-pkg",
            Version = "1.0.0",
            Channel = "stable",
            ArtifactType = "msi",
            Sources = new ResolvedManifestSources
            {
                ArtifactType = ManifestFieldSource.Admin,
                InstallAdapter = ManifestFieldSource.Template,
                Detection = ManifestFieldSource.Analyzer,
                PolicyTagsComposite = ManifestFieldSource.Default
            }
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ResolvedManifest>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Sources.ArtifactType, Is.EqualTo(ManifestFieldSource.Admin));
        Assert.That(deserialized.Sources.InstallAdapter, Is.EqualTo(ManifestFieldSource.Template));
        Assert.That(deserialized.Sources.Detection, Is.EqualTo(ManifestFieldSource.Analyzer));
        Assert.That(deserialized.Sources.PolicyTagsComposite, Is.EqualTo(ManifestFieldSource.Default));
    }
}