using DeploymentPoC.Agent.Models;
using DeploymentPoC.Agent.Services;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class AgentEnrollmentServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void SaveConfig_FallsBackToUserConfig_WhenVarLibIsUnauthorized()
    {
        var service = new TestableAgentEnrollmentService(new HttpClient(), _tempDir);
        var config = new AgentConfig
        {
            NodeId = Guid.NewGuid(),
            OrchestratorUrl = "http://test"
        };

        service.SaveConfig(config);

        var fallbackPath = Path.Combine(_tempDir, ".config", "deploymentpoc", "agent.json");
        Assert.That(File.Exists(fallbackPath), Is.True);

        var loaded = service.LoadConfig();
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.NodeId, Is.EqualTo(config.NodeId));
    }

    [Test]
    public void GetConfigPath_ReturnsFallbackPath_OnLinux_WhenUnauthorizedAccess()
    {
        var service = new TestableAgentEnrollmentService(new HttpClient(), _tempDir);
        var path = service.GetConfigPath();

        var fallbackPath = Path.Combine(_tempDir, ".config", "deploymentpoc", "agent.json");
        Assert.That(path, Is.EqualTo(fallbackPath));
    }

    [Test]
    public void LoadConfig_LoadsFromFallback_WhenPrimaryIsUnauthorized()
    {
        var service = new TestableAgentEnrollmentService(new HttpClient(), _tempDir);
        var config = new AgentConfig
        {
            NodeId = Guid.NewGuid(),
            OrchestratorUrl = "http://test"
        };
        service.SaveConfig(config);

        var loaded = service.LoadConfig();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.NodeId, Is.EqualTo(config.NodeId));
        Assert.That(loaded.OrchestratorUrl, Is.EqualTo(config.OrchestratorUrl));
    }

    [Test]
    public void SaveConfig_CreatesDirectory_WhenFallbackDoesNotExist()
    {
        var service = new TestableAgentEnrollmentService(new HttpClient(), _tempDir);
        var config = new AgentConfig
        {
            NodeId = Guid.NewGuid(),
            OrchestratorUrl = "http://test"
        };

        service.SaveConfig(config);

        var fallbackDir = Path.Combine(_tempDir, ".config", "deploymentpoc");
        Assert.That(Directory.Exists(fallbackDir), Is.True);
    }

    private class TestableAgentEnrollmentService : AgentEnrollmentService
    {
        private readonly string _tempHome;

        public TestableAgentEnrollmentService(HttpClient httpClient, string tempHome) : base(httpClient)
        {
            _tempHome = tempHome;
        }

        public override string GetConfigPath()
        {
            var primaryPath = "/var/lib/deploymentpoc/agent.json";
            try
            {
                var directory = Path.GetDirectoryName(primaryPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                return primaryPath;
            }
            catch (UnauthorizedAccessException)
            {
                return Path.Combine(_tempHome, ".config", "deploymentpoc", "agent.json");
            }
        }
    }
}
