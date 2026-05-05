using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace DeploymentPoC.Orchestrator.IntegrationTests.AgentEnrollment;

[TestFixture]
public class AgentEnrollmentIntegrationTests
{
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private IContainer? _agentContainer;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CustomWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        _client?.Dispose();
        await _factory.DisposeAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        if (_agentContainer is not null)
        {
            try
            {
                await _agentContainer.StopAsync();
            }
            catch
            {
                // container may already be stopped
            }

            await _agentContainer.DisposeAsync();
            _agentContainer = null;
        }
    }

    [Test]
    public async Task HappyPath_Enrollment()
    {
        var token = await IssueTokenAsync();
        _agentContainer = await StartAgentContainerAsync(token);
        await WaitForNodeOnlineAsync(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task AutoReconnect_AfterRestart()
    {
        var token = await IssueTokenAsync();
        var configDir = CreateTempConfigDir();

        _agentContainer = await StartAgentContainerAsync(token, configPath: configDir);
        var firstNode = await WaitForNodeOnlineAsync(TimeSpan.FromSeconds(30));

        await _agentContainer.StopAsync();

        // start new container without --enroll but with same config volume
        _agentContainer = await StartAgentContainerAsync(
            token: "",
            enroll: false,
            orchestratorUrl: _factory.BaseUrl.Replace("0.0.0.0", GetHostIp()),
            configPath: configDir);

        var secondNode = await WaitForNodeOnlineAsync(TimeSpan.FromSeconds(30));
        Assert.That(secondNode.Id, Is.EqualTo(firstNode.Id));
    }

    [Test]
    public async Task ResetAndReEnroll()
    {
        var token = await IssueTokenAsync();
        var configDir = CreateTempConfigDir();

        _agentContainer = await StartAgentContainerAsync(token, configPath: configDir);
        var firstNode = await WaitForNodeOnlineAsync(TimeSpan.FromSeconds(30));

        var execResult = await _agentContainer.ExecAsync(new[] { "./DeploymentPoC.Agent", "--reset" });
        Assert.That(execResult.ExitCode, Is.EqualTo(0));

        await _agentContainer.StopAsync();

        var newToken = await IssueTokenAsync();
        _agentContainer = await StartAgentContainerAsync(newToken, configPath: configDir);
        var secondNode = await WaitForNodeOnlineAsync(TimeSpan.FromSeconds(30));

        Assert.That(secondNode.Id, Is.Not.EqualTo(firstNode.Id));
    }

    [Test]
    public async Task ExpiredToken_ExitsNonZero()
    {
        // create a token via api, then manually expire it in the database
        var token = await IssueTokenAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InstallerDbContext>();
            var entity = await db.EnrollmentTokens.SingleAsync(t => t.Token == token);
            entity.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        _agentContainer = await StartAgentContainerAsync(token);
        await AssertContainerExitsWithErrorAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task ConsumedToken_ExitsNonZero()
    {
        var token = await IssueTokenAsync();
        await ConsumeTokenAsync(token);

        _agentContainer = await StartAgentContainerAsync(token);
        await AssertContainerExitsWithErrorAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task EnrollWithExistingConfig_ExitsNonZero()
    {
        var token = await IssueTokenAsync();
        var configDir = CreateTempConfigDir();

        _agentContainer = await StartAgentContainerAsync(token, configPath: configDir);
        await WaitForNodeOnlineAsync(TimeSpan.FromSeconds(30));

        await _agentContainer.StopAsync();

        // start a new container with --enroll when already enrolled
        _agentContainer = await StartAgentContainerAsync(token, configPath: configDir);
        await AssertContainerExitsWithErrorAsync(TimeSpan.FromSeconds(15));
    }

    private async Task<string> IssueTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/nodes/enroll", new { ttlMinutes = 10, requestedBy = "test", orchestratorUrl = _factory.BaseUrl });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EnrollmentTokenResponse>();
        return result!.Token;
    }

    private async Task ConsumeTokenAsync(string token)
    {
        var response = await _client.PostAsJsonAsync($"/api/enrollment-tokens/{token}/consume", new { });
        response.EnsureSuccessStatusCode();
    }

    private async Task<IContainer> StartAgentContainerAsync(
        string token,
        bool enroll = true,
        string? orchestratorUrl = null,
        string? configPath = null)
    {
        var hostIp = GetHostIp();
        var url = orchestratorUrl ?? _factory.BaseUrl.Replace("0.0.0.0", hostIp);

        var builder = new ContainerBuilder()
            .WithImage("deploymentpoc-agent:test")
            .WithName($"deploymentpoc-agent-{Guid.NewGuid():N}")
            .WithAutoRemove(true);

        if (!string.IsNullOrEmpty(configPath))
        {
            builder = builder.WithBindMount(configPath, "/var/lib/deploymentpoc");
        }

        if (enroll && !string.IsNullOrEmpty(token))
        {
            builder = builder.WithEntrypoint("./DeploymentPoC.Agent", "--enroll", token, "--orchestrator-url", url);
        }
        else
        {
            builder = builder.WithEntrypoint("./DeploymentPoC.Agent", "--orchestrator-url", url);
        }

        var container = builder.Build();
        await container.StartAsync();
        return container;
    }

    private static string CreateTempConfigDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetHostIp()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = "network inspect bridge --format=\"{{range .IPAM.Config}}{{.Gateway}}{{end}}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    }
                };
                process.Start();
                process.WaitForExit();
                var gateway = process.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrWhiteSpace(gateway))
                    return gateway;
            }
            catch
            {
                // ignore and fall back to host.docker.internal
            }
        }

        return "host.docker.internal";
    }

    private async Task<Node> WaitForNodeOnlineAsync(TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            var response = await _client.GetAsync("/api/nodes");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var nodes = await response.Content.ReadFromJsonAsync<List<Node>>();
                var online = nodes?.FirstOrDefault(n => n.Status == "Online");
                if (online is not null)
                    return online;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("Node did not come online within timeout");
    }

    private async Task AssertContainerExitsWithErrorAsync(TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            try
            {
                var logs = await _agentContainer!.GetLogsAsync();
                var combined = logs.Stdout + logs.Stderr;
                if (combined.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("consumed", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("already enrolled", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch
            {
                // container may not have logs yet
            }

            await Task.Delay(500);
        }

        Assert.Fail("Container did not exit with expected error within timeout");
    }

    private class EnrollmentTokenResponse
    {
        public string Token { get; set; } = "";
    }

    private class Node
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
    }
}
