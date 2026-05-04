namespace Orchestrator.Models;

public class EnrollmentToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? UsedByAgentId { get; set; }
}
