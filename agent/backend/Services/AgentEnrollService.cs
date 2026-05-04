using System.Reflection;
using System.Text;
using System.Text.Json;
using Agent.Models;

namespace Agent.Services;

public class AgentEnrollService
{
    private readonly string _configPath;

    public AgentEnrollService()
    {
        _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OrchestratorAgent", "agent.json");
    }

    public async Task<int> ExecuteEnrollAsync(string token, string url)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var enrollUrl = url.TrimEnd('/') + "/api/agents/enroll";
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

            var pollingInterval = 30;
            if (result.TryGetProperty("pollingIntervalSeconds", out var pollingProp))
                pollingInterval = pollingProp.GetInt32();

            var config = new AgentConfig
            {
                AgentId = agentId,
                AgentSecret = agentSecret,
                OrchestratorUrl = url.TrimEnd('/'),
                PollingIntervalSeconds = pollingInterval
            };

            var configService = new AgentConfigService();
            configService.SaveConfig(config);

            Console.WriteLine($"Enrollment successful. Agent ID: {agentId}");
            Console.WriteLine($"Configuration written to: {_configPath}");

            // Register and start Windows Service
            await RegisterAndStartServiceAsync();

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

    private static async Task RegisterAndStartServiceAsync()
    {
        try
        {
            using var scm = new ScmService();
            await scm.InstallServiceAsync("OrchestratorAgent", Assembly.GetExecutingAssembly().Location);
            await scm.StartServiceAsync("OrchestratorAgent");
            Console.WriteLine("Windows Service installed and started successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Service registration failed: {ex.Message}");
            Console.Error.WriteLine("Run as Administrator to register the Windows Service.");
        }
    }
}
