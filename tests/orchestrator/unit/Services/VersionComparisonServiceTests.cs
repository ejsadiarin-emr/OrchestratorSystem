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
    [TestCase("3.14", "3.14.4", 0)] // prefix matching behavior
    [TestCase("24.3.0", "24.3.0.202412091607", 0)]
    public void CompareVersions_NumericVersions_ReturnsExpected(string a, string b, int expected)
    {
        var result = VersionComparisonService.CompareVersions(a, b);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("1.0.0-alpha", "1.0.0", null)]
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
}
