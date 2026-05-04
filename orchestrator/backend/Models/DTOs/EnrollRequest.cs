using System.ComponentModel.DataAnnotations;

namespace Orchestrator.Models.DTOs;

public class EnrollRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string Hostname { get; set; } = string.Empty;

    [Required]
    public string IpAddress { get; set; } = string.Empty;
}
