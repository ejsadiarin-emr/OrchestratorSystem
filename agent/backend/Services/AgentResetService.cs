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
            await DeleteConfigAndServiceAsync();
            return 0;
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var unregisterUrl = config.OrchestratorUrl.TrimEnd('/') + $"/api/agents/{config.AgentId}/unregister";
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.AgentSecret);

            var response = await httpClient.PostAsync(unregisterUrl, null);
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

        await DeleteConfigAndServiceAsync();
        return 0;
    }

    private async Task DeleteConfigAndServiceAsync()
    {
        _configService.DeleteConfig();
        Console.WriteLine("Agent configuration deleted.");

        await StopAndDeleteServiceAsync();
    }

    private static async Task StopAndDeleteServiceAsync()
    {
        try
        {
            using var scm = new ScmService();
            await scm.StopServiceAsync("OrchestratorAgent");
            await scm.DeleteServiceAsync("OrchestratorAgent");
            Console.WriteLine("Windows Service stopped and deleted successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Service removal failed: {ex.Message}");
            Console.Error.WriteLine("Run as Administrator to remove the Windows Service.");
        }
    }
}
