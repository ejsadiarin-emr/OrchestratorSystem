using System.Text;
using System.Text.Json;
using Agent.Models;

namespace Agent.Services;

public class AgentEnrollService
{
    private readonly string _configPath;

    public AgentEnrollService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "agent.json");
    }

    public async Task<int> ExecuteEnrollAsync(string token, string url)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var enrollUrl = url.TrimEnd('/') + "/api/enrollment/enroll";
            var hostname = Environment.MachineName;
            var ipAddress = GetLocalIpAddress();

            var request = new
            {
                Token = token,
                Hostname = hostname,
                IpAddress = ipAddress
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(enrollUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Enrollment failed: {responseBody}");
                return 1;
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var agentId = result.GetProperty("agentId").GetString()!;
            var agentSecret = result.GetProperty("agentSecret").GetString()!;

            var config = new AgentConfig
            {
                AgentId = agentId,
                AgentSecret = agentSecret,
                OrchestratorUrl = url.TrimEnd('/')
            };

            var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_configPath, configJson);

            Console.WriteLine($"Enrollment successful. Agent ID: {agentId}");
            Console.WriteLine($"Configuration written to: {_configPath}");

            // Register and start Windows Service
            RegisterAndStartService();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Enrollment error: {ex.Message}");
            return 1;
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch
        {
            // ignore
        }
        return "127.0.0.1";
    }

    private static void RegisterAndStartService()
    {
        Console.WriteLine("Service registration would occur here (requires admin privileges).");
        Console.WriteLine("Run: sc create OrchestratorAgent binPath= \"Agent.exe\" start= auto");
        Console.WriteLine("Run: sc start OrchestratorAgent");
    }
}
