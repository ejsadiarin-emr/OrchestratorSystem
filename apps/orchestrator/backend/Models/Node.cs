using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Models;

public class Node
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Hostname { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? FirstConnectedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
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

    [StringLength(255)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string IpAddress { get; set; } = string.Empty;

    [StringLength(512)]
    public string Description { get; set; } = string.Empty;
}

public class UpdateNodeDisplayNameRequest
{
    [StringLength(255)]
    public string DisplayName { get; set; } = string.Empty;
}
