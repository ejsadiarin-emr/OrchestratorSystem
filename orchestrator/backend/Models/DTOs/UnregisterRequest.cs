using System.ComponentModel.DataAnnotations;

namespace Orchestrator.Models.DTOs;

public class UnregisterRequest
{
    [Required]
    public string AgentId { get; set; } = string.Empty;

    [Required]
    public string AgentSecret { get; set; } = string.Empty;
}
