using DeploymentPoC.Orchestrator.Services;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests;

public class InstallAdapterDefaultsTests
{
    [Test]
    public void InstallAdapterDefault_ExpectedExitCodes_ShouldContainZero()
    {
        var adapter = new InstallAdapter();

        Assert.That(adapter.ExpectedExitCodes, Is.Not.Null);
        Assert.That(adapter.ExpectedExitCodes, Does.Contain(0),
            "Default ExpectedExitCodes should contain 0 to match contract defaults");
    }

    [Test]
    public void InstallAdapterDefault_TimeoutSeconds_ShouldBe300()
    {
        var adapter = new InstallAdapter();

        Assert.That(adapter.TimeoutSeconds, Is.EqualTo(300),
            "Default TimeoutSeconds should be 300 to match contract defaults");
    }

    [Test]
    public void InstallAdapter_ExpectedExitCodesDefaults_MatchContract()
    {
        var contractDefaults = new List<int> { 0 };
        var adapter = new InstallAdapter();

        Assert.That(adapter.ExpectedExitCodes, Is.EqualTo(contractDefaults),
            "InstallAdapter ExpectedExitCodes defaults should match InstallAdapterConfig contract defaults");
    }
}