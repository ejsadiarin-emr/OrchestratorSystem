using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class ProgramConfigurationTests
{
    [Test]
    public void ParseAgentArgs_PreservesUrlsArg_ForAspNetCoreOverride()
    {
        var args = new[] { "--enroll", "token123", "--orchestrator-url", "http://host", "--urls", "http://localhost:9999" };

        var result = AgentProgram.ParseArgs(args);

        Assert.That(result.EnrollToken, Is.EqualTo("token123"));
        Assert.That(result.OrchestratorUrl, Is.EqualTo("http://host"));
        Assert.That(result.RemainingArgs, Does.Contain("--urls"));
        Assert.That(result.RemainingArgs, Does.Contain("http://localhost:9999"));
    }

    [Test]
    public void ParseAgentArgs_ReturnsEmptyRemainingArgs_WhenOnlyKnownArgsProvided()
    {
        var args = new[] { "--enroll", "token123", "--orchestrator-url", "http://host" };

        var result = AgentProgram.ParseArgs(args);

        Assert.That(result.RemainingArgs, Is.Empty);
    }
}
