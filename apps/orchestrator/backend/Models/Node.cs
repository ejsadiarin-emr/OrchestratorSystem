using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Models;

public class Node
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
}

public class CreateNodeRequest
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Hostname { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string IpAddress { get; set; } = string.Empty;

    [StringLength(512)]
    public string Description { get; set; } = string.Empty;
}

public class UpdateNodeRequest
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Hostname { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string IpAddress { get; set; } = string.Empty;

    [StringLength(512)]
    public string Description { get; set; } = string.Empty;
}
