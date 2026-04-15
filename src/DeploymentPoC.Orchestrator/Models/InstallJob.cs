using System.ComponentModel.DataAnnotations;

namespace DeploymentPoC.Orchestrator.Models;

public class InstallJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public Guid TargetNodeId { get; set; }
    public string TargetNodeHostname { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; } = 2;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<JobStep> Steps { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class JobStep
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? Duration { get; set; }
}

public class CreateJobRequest
{
    [Required]
    public Guid PackageId { get; set; }

    [Required]
    public Guid TargetNodeId { get; set; }
}
