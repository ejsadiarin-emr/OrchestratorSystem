using Orchestrator.Models;

public interface IEnrollmentService
{
    Task<EnrollmentToken> GenerateTokenAsync();
    Task<EnrollmentResult> EnrollAsync(string token, string hostname, string ipAddress);
    Task UnregisterAsync(string agentId, string agentSecret);
}
