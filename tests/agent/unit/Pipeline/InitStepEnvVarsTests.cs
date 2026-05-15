using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests.Pipeline;

[TestFixture]
public class InitStepEnvVarsTests
{
    private PipelineContext CreateContext(string mode = "install")
    {
        return new PipelineContext
        {
            Payload = new AssignRunPayload
            {
                RunId = Guid.NewGuid(),
                WorkloadName = "test-workload",
                Mode = mode,
                Packages = new List<PackageAssignment>()
            },
            OrchestratorBaseUrl = "https://unit.test",
            AgentId = "agent-1",
            RunId = Guid.NewGuid().ToString(),
            Sequence = 1
        };
    }

    private static PackageAssignment CreatePackage(string name, string version)
    {
        return new PackageAssignment
        {
            PackageIndex = 0,
            PackageId = name,
            Name = name,
            Version = version,
            InstallAdapter = new InstallAdapterConfig
            {
                Type = "exe",
                Command = "installer",
                TimeoutSeconds = 30
            },
            Detection = new DetectionConfig
            {
                Type = "file",
                Path = name
            }
        };
    }

    [Test]
    public void Build_WithPackageAndArtifactPath_ReturnsAllVars()
    {
        var context = CreateContext();
        var package = CreatePackage("test-pkg", "2.0.0");
        var artifactPath = "/tmp/artifacts/test-pkg.msi";

        var vars = InitStepEnvVars.Build(context, package, artifactPath);

        Assert.That(vars["DEPLOY_RUN_ID"], Is.EqualTo(context.RunId));
        Assert.That(vars["DEPLOY_AGENT_ID"], Is.EqualTo("agent-1"));
        Assert.That(vars["DEPLOY_WORKLOAD_NAME"], Is.EqualTo("test-workload"));
        Assert.That(vars["DEPLOY_ORCHESTRATOR_URL"], Is.EqualTo("https://unit.test"));
        Assert.That(vars["DEPLOY_PACKAGE_NAME"], Is.EqualTo("test-pkg"));
        Assert.That(vars["DEPLOY_PACKAGE_VERSION"], Is.EqualTo("2.0.0"));
        Assert.That(vars["DEPLOY_ARTIFACT_PATH"], Is.EqualTo(artifactPath));
    }

    [Test]
    public void Build_WithoutPackage_OmitsPackageVars()
    {
        var context = CreateContext();
        var artifactPath = "/tmp/artifacts/script.ps1";

        var vars = InitStepEnvVars.Build(context, null, artifactPath);

        Assert.That(vars.ContainsKey("DEPLOY_PACKAGE_NAME"), Is.False);
        Assert.That(vars.ContainsKey("DEPLOY_PACKAGE_VERSION"), Is.False);
        Assert.That(vars["DEPLOY_RUN_ID"], Is.EqualTo(context.RunId));
        Assert.That(vars["DEPLOY_ARTIFACT_PATH"], Is.EqualTo(artifactPath));
    }

    [Test]
    public void Build_WithoutArtifactPath_OmitsArtifactPath()
    {
        var context = CreateContext();
        var package = CreatePackage("test-pkg", "1.0.0");

        var vars = InitStepEnvVars.Build(context, package, null);

        Assert.That(vars.ContainsKey("DEPLOY_ARTIFACT_PATH"), Is.False);
        Assert.That(vars["DEPLOY_PACKAGE_NAME"], Is.EqualTo("test-pkg"));
        Assert.That(vars["DEPLOY_PACKAGE_VERSION"], Is.EqualTo("1.0.0"));
    }

    [Test]
    public void Build_WithoutPackageOrArtifactPath_ReturnsStandardVarsOnly()
    {
        var context = CreateContext();

        var vars = InitStepEnvVars.Build(context, null, null);

        Assert.That(vars, Has.Count.EqualTo(4));
        Assert.That(vars["DEPLOY_RUN_ID"], Is.EqualTo(context.RunId));
        Assert.That(vars["DEPLOY_AGENT_ID"], Is.EqualTo("agent-1"));
        Assert.That(vars["DEPLOY_WORKLOAD_NAME"], Is.EqualTo("test-workload"));
        Assert.That(vars["DEPLOY_ORCHESTRATOR_URL"], Is.EqualTo("https://unit.test"));
    }

    [Test]
    public void Build_ReturnsNewDictionary_EachCall()
    {
        var context = CreateContext();
        var package = CreatePackage("pkg-a", "1.0.0");

        var vars1 = InitStepEnvVars.Build(context, package, "/path/a");
        var vars2 = InitStepEnvVars.Build(context, package, "/path/b");

        Assert.That(vars1["DEPLOY_ARTIFACT_PATH"], Is.EqualTo("/path/a"));
        Assert.That(vars2["DEPLOY_ARTIFACT_PATH"], Is.EqualTo("/path/b"));
    }

    [Test]
    public void Build_DifferentWorkloadNames_ReflectedInVars()
    {
        var context = new PipelineContext
        {
            Payload = new AssignRunPayload
            {
                RunId = Guid.NewGuid(),
                WorkloadName = "team-a-notepad",
                Mode = "install",
                Packages = new List<PackageAssignment>()
            },
            OrchestratorBaseUrl = "http://orchestrator:5000",
            AgentId = "agent-42",
            RunId = "run-abc-123",
            Sequence = 1
        };

        var vars = InitStepEnvVars.Build(context, null, null);

        Assert.That(vars["DEPLOY_WORKLOAD_NAME"], Is.EqualTo("team-a-notepad"));
        Assert.That(vars["DEPLOY_ORCHESTRATOR_URL"], Is.EqualTo("http://orchestrator:5000"));
        Assert.That(vars["DEPLOY_AGENT_ID"], Is.EqualTo("agent-42"));
        Assert.That(vars["DEPLOY_RUN_ID"], Is.EqualTo("run-abc-123"));
    }
}
