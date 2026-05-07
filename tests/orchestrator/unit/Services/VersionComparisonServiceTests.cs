using DeploymentPoC.Orchestrator.Services;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class VersionComparisonServiceTests
{
    [TestCase("1.0.0", "1.0.0", 0)]
    [TestCase("1.0", "1.0.0", 0)]
    [TestCase("1.0.0", "1.1.0", -1)]
    [TestCase("1.1.0", "1.0.0", 1)]
    [TestCase("2.0.0", "1.5.0", 1)]
    [TestCase("1.5.0", "2.0.0", -1)]
    [TestCase("1.0.10", "1.0.2", 1)]
    [TestCase("3.14", "3.14.4", -1)] // shorter version is zero-padded: 3.14 < 3.14.4
    [TestCase("24.3.0", "24.3.0.202412091607", -1)] // same logic
    public void CompareVersions_NumericVersions_ReturnsExpected(string a, string b, int expected)
    {
        var result = VersionComparisonService.CompareVersions(a, b);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("1.0.0-alpha", "1.0.0", 0)] // normalization strips alpha suffix
    [TestCase("abc", "1.0.0", null)]
    [TestCase("1.0.0", "", null)]
    [TestCase(null, "1.0.0", null)]
    public void CompareVersions_NonComparable_ReturnsNull(string a, string b, int? expected)
    {
        var result = VersionComparisonService.CompareVersions(a, b);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("2.0.0", "1.0.0", true)]
    [TestCase("1.0.0", "2.0.0", false)]
    [TestCase("1.0.0", "1.0.0", false)]
    [TestCase(null, "1.0.0", false)]
    public void IsDowngrade_ReturnsExpected(string current, string target, bool expected)
    {
        Assert.That(VersionComparisonService.IsDowngrade(current, target), Is.EqualTo(expected));
    }

    [TestCase("1.0.0", "2.0.0", true)]
    [TestCase("2.0.0", "1.0.0", false)]
    [TestCase("1.0.0", "1.0.0", false)]
    public void IsUpgrade_ReturnsExpected(string current, string target, bool expected)
    {
        Assert.That(VersionComparisonService.IsUpgrade(current, target), Is.EqualTo(expected));
    }

    [TestCase("1.0.0", "1.0.0", true)]
    [TestCase("1.0.0.123", "1.0.0", true)]
    [TestCase("1.0", "1.0.0", true)]
    [TestCase("24.3.0", "24.3.0.202412091607", true)]
    [TestCase("2.0.0", "1.0.0", false)]
    [TestCase("1.0.1", "1.0.0", false)]
    [TestCase("1.0.0-alpha", "1.0.0", true)] // normalization strips alpha suffix
    [TestCase(null, "1.0.0", false)]
    [TestCase("", "1.0.0", false)]
    public void Matches_ReturnsExpected(string? expected, string? actual, bool shouldMatch)
    {
        Assert.That(VersionComparisonService.Matches(expected, actual), Is.EqualTo(shouldMatch));
    }

    [TestCase("1.0.0", "1.1.0", new[] { "1.0.0", "1.1.0", "2.0.0" }, true)]
    [TestCase("1.0.0", "2.0.0", new[] { "1.0.0", "1.1.0", "2.0.0" }, false)]
    [TestCase("1.1.0", "2.0.0", new[] { "1.0.0", "1.1.0", "2.0.0" }, true)]
    [TestCase("1.0.0", "1.0.0", new[] { "1.0.0", "1.1.0" }, false)]
    [TestCase(null, "1.0.0", new[] { "1.0.0", "1.1.0" }, true)]
    [TestCase("1.0.0", "1.1.0", new[] { "1.0.0" }, false)]
    [TestCase("1.0.0", "1.1.0", new string[0], false)]
    public void IsSequentialRevision_ReturnsExpected(string? current, string target, string[] allVersions, bool expected)
    {
        Assert.That(VersionComparisonService.IsSequentialRevision(current, target, allVersions), Is.EqualTo(expected));
    }
}
