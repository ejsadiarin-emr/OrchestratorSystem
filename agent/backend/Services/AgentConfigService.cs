using Agent.Models;
using System.Text.Json;

namespace Agent.Services;

public class AgentConfigService
{
    private readonly string _configPath;

    public AgentConfigService()
    {
        _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OrchestratorAgent", "agent.json");
    }

    public AgentConfig? LoadConfig()
    {
        if (!File.Exists(_configPath))
            return null;

        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<AgentConfig>(json);
    }

    public void SaveConfig(AgentConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_configPath, json);
    }

    public void DeleteConfig()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }
}
