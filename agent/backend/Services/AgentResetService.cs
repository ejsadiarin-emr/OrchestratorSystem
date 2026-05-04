using Agent.Models;
using System.Text;
using System.Text.Json;

namespace Agent.Services;

public class AgentResetService
{
    private readonly AgentConfigService _configService;

    public AgentResetService()
    {
        _configService = new AgentConfigService();
    }

    public async Task<int> ExecuteResetAsync()
    {
        var config = _configService.LoadConfig();
        if (config == null)
        {
            Console.WriteLine("No agent configuration found. Nothing to reset.");
            DeleteConfigAndService();
            return 0;
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var unregisterUrl = config.OrchestratorUrl.TrimEnd('/') + "/api/enrollment/unregister";
            var request = new
            {
                config.AgentId,
                config.AgentSecret
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(unregisterUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Successfully unregistered from Orchestrator.");
            }
            else
            {
                Console.WriteLine($"Unregister request failed: {responseBody}");
                Console.WriteLine("Proceeding with local cleanup anyway...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to contact Orchestrator: {ex.Message}");
            Console.WriteLine("Proceeding with local cleanup...");
        }

        DeleteConfigAndService();
        return 0;
    }

    private void DeleteConfigAndService()
    {
        _configService.DeleteConfig();
        Console.WriteLine("Agent configuration deleted.");

        StopAndDeleteService();
    }

    private static void StopAndDeleteService()
    {
        Console.WriteLine("Service removal would occur here (requires admin privileges).");
        Console.WriteLine("Run: sc.exe stop OrchestratorAgent");
        Console.WriteLine("Run: sc.exe delete OrchestratorAgent");
    }
}
