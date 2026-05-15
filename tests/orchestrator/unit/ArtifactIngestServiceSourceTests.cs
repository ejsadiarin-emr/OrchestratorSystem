using DeploymentPoC.Orchestrator.Services;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests;

public class ArtifactIngestServiceSourceTests
{
    private static ArtifactIngestManifest CreateMinimalManifest(string packageId, string? adapterType = null, string? arguments = null)
    {
        return new ArtifactIngestManifest
        {
            PackageId = packageId,
            Version = "1.0.0",
            Channel = "stable",
            InstallAdapter = new InstallAdapterInput
            {
                Type = adapterType,
                Arguments = arguments ?? "/silent"
            },
            Detection = new DetectionInput
            {
                Type = "registry",
                Path = "HKLM\\Software\\Test"
            },
            PolicyTags = new PolicyTagsInput
            {
                RiskLevel = "low",
                RetryabilityClass = "retryable",
                IdempotencyMode = "strict"
            }
        };
    }

    [Test]
    public void Ingest_TemplatePackageId_ProducesTemplateSource()
    {
        var service = new ArtifactIngestService();
        var manifest = CreateMinimalManifest("template-test-pkg", arguments: "/qn");

        var emptyStream = new MemoryStream();
        var result = service.Ingest("setup.msi", emptyStream, manifest, "test-actor");

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ResolvedManifest, Is.Not.Null);
        Assert.That(result.ResolvedManifest!.Sources.InstallAdapterSources.Type, Is.EqualTo("template"));
    }

    [Test]
    public void Ingest_MsiMagicBytes_ProducesAnalyzerSource()
    {
        var service = new ArtifactIngestService();
        var manifest = CreateMinimalManifest("analyzer-test-pkg", arguments: "/qn");

        // MSI magic bytes: D0 CF 11 E0 (OLE compound document)
        var msiBytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0x00, 0x00, 0x00, 0x00 };
        var stream = new MemoryStream(msiBytes);
        var result = service.Ingest("setup.msi", stream, manifest, "test-actor");

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ResolvedManifest, Is.Not.Null);
        Assert.That(result.ResolvedManifest!.Sources.InstallAdapterSources.Type, Is.EqualTo("analyzer"));
    }

    [Test]
    public void Ingest_ExeMagicBytes_ProducesAnalyzerSource()
    {
        var service = new ArtifactIngestService();
        var manifest = CreateMinimalManifest("analyzer-exe-pkg", arguments: "/S");

        // PE executable magic: MZ header
        var exeBytes = new byte[] { 0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var stream = new MemoryStream(exeBytes);
        var result = service.Ingest("setup.exe", stream, manifest, "test-actor");

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ResolvedManifest, Is.Not.Null);
        Assert.That(result.ResolvedManifest!.Sources.InstallAdapterSources.Type, Is.EqualTo("analyzer"));
    }

    [Test]
    public void Ingest_AdminExplicitType_ProducesAdminSource()
    {
        var service = new ArtifactIngestService();
        var manifest = CreateMinimalManifest("admin-test-pkg", adapterType: "custom", arguments: "/silent");

        var emptyStream = new MemoryStream();
        var result = service.Ingest("setup.exe", emptyStream, manifest, "test-actor");

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ResolvedManifest, Is.Not.Null);
        Assert.That(result.ResolvedManifest!.Sources.InstallAdapterSources.Type, Is.EqualTo("admin"));
    }

    [Test]
    public void Ingest_ExtensionOnly_NoMagicBytes_ProducesDefaultSource()
    {
        var service = new ArtifactIngestService();
        var manifest = CreateMinimalManifest("default-test-pkg", arguments: "/silent");

        var emptyStream = new MemoryStream();
        var result = service.Ingest("setup.msi", emptyStream, manifest, "test-actor");

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ResolvedManifest, Is.Not.Null);
        Assert.That(result.ResolvedManifest!.Sources.InstallAdapterSources.Type, Is.EqualTo("default"));
    }
}
