using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using DeploymentPoC.Agent.Models;

namespace DeploymentPoC.Agent.Services;

public class AgentEnrollmentService
{
    private readonly HttpClient _httpClient;

    public AgentEnrollmentService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<Guid> ConsumeEnrollmentTokenAsync(string token, string orchestratorUrl)
    {
        var url = $"{orchestratorUrl.TrimEnd('/')}/api/enrollment-tokens/{token}/consume";
        var response = await _httpClient.PostAsync(url, null);

        if (response.StatusCode == HttpStatusCode.Gone)
        {
            throw new InvalidOperationException("Enrollment token expired.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Enrollment token already consumed.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Enrollment token not found.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Enrollment failed: {(int)response.StatusCode} {response.StatusCode}. Response: {body}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var node = JsonSerializer.Deserialize<JsonElement>(content);
        if (node.TryGetProperty("id", out var idProperty))
        {
            return idProperty.GetGuid();
        }

        throw new InvalidOperationException("Enrollment response did not contain a valid NodeId.");
    }

    public virtual string GetConfigPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            return Path.Combine(localAppData, "DeploymentPoC", "agent.json");
        }

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
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "deploymentpoc", "agent.json");
        }
    }

    public AgentConfig? LoadConfig()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AgentConfig>(json);
    }

    public void SaveConfig(AgentConfig config)
    {
        var path = GetConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(path, json);
    }

    public void ResetConfig()
    {
        var path = GetConfigPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
