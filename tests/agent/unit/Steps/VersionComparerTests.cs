using NUnit.Framework;
using DeploymentPoC.Agent.Steps;

namespace DeploymentPoC.Agent.Tests.Steps;

[TestFixture]
public class VersionComparerTests
{
    [Test]
    public void Matches_ExactMatch_ReturnsTrue()
    {
        Assert.That(VersionComparer.Matches("3.14.4", "3.14.4"), Is.True);
    }

    [Test]
    public void Matches_PrefixMatch_ReturnsTrue()
    {
        Assert.That(VersionComparer.Matches("3.14", "3.14.4150"), Is.True);
    }

    [Test]
    public void Matches_Mismatch_ReturnsFalse()
    {
        Assert.That(VersionComparer.Matches("26.0.3", "24.3.0"), Is.False);
    }

    [Test]
    public void NormalizeVersion_MarketingVersion_ReturnsSegments()
    {
        var result = VersionComparer.NormalizeVersion("Python 3.13.3");
        Assert.That(result, Is.EqualTo(new long[] { 3, 13, 3 }));
    }

    [Test]
    public void NormalizeVersion_DBeaverVersion_ReturnsSegments()
    {
        var result = VersionComparer.NormalizeVersion("24.3.0.202412091607");
        Assert.That(result, Is.EqualTo(new long[] { 24, 3, 0, 202412091607 }));
    }

    [Test]
    public void NormalizeVersion_GitVersion_ReturnsSegments()
    {
        var result = VersionComparer.NormalizeVersion("2.48.1.windows.1");
        Assert.That(result, Is.EqualTo(new long[] { 2, 48, 1 }));
    }

    [Test]
    public void Matches_NullExpected_ReturnsFalse()
    {
        Assert.That(VersionComparer.Matches(null, "1.0.0"), Is.False);
    }

    [Test]
    public void Matches_EmptyExpected_ReturnsFalse()
    {
        Assert.That(VersionComparer.Matches("", "1.0.0"), Is.False);
    }

    [Test]
    public void Matches_NullActual_ReturnsFalse()
    {
        Assert.That(VersionComparer.Matches("1.0.0", null), Is.False);
    }

    [Test]
    public void Matches_SqlServerPrefix_ReturnsTrue()
    {
        Assert.That(VersionComparer.Matches("16.0", "16.0.18025.20160"), Is.True);
    }

    [Test]
    public void NormalizeVersion_EmptyString_ReturnsEmptyArray()
    {
        var result = VersionComparer.NormalizeVersion("");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void NormalizeVersion_Null_ReturnsEmptyArray()
    {
        var result = VersionComparer.NormalizeVersion(null!);
        Assert.That(result, Is.Empty);
    }
}
