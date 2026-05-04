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

public class NodeDetailResponse
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public DateTime LastSeenAt { get; set; }
    public DateTime? FirstConnectedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public List<NodeWorkloadAssignment> Workloads { get; set; } = new();
    public NodePreCheckSummary LatestPreCheck { get; set; } = new();
}

public class NodeWorkloadAssignment
{
    public Guid WorkloadId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
}

public class NodePreCheckSummary
{
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public List<PreCheckItem> Items { get; set; } = new();
    public string OverallStatus => Items.Count == 0 ? "passed" :
        Items.Any(i => i.Status == "failed") ? "failed" :
        Items.Any(i => i.Status == "warning") ? "warning" :
        Items.Any(i => i.Status == "info") ? "info" : "passed";
}

public class PreCheckItem
{
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? ActualVersion { get; set; }
}

public class RunPreCheckRequest
{
    public List<Guid> NodeIds { get; set; } = new();
    public Guid? WorkloadId { get; set; }
}

public class NodePreCheckResponse
{
    public Guid NodeId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string? Error { get; set; }
    public NodePreCheckSummary Summary { get; set; } = new();
}

public class RunPreCheckSummaryRequest
{
    public List<Guid> NodeIds { get; set; } = new();
    public Guid WorkloadId { get; set; }
    public Guid RevisionId { get; set; }
}

public class PreCheckSummaryResponse
{
    public List<PreCheckSummaryNode> Nodes { get; set; } = new();
}

public class PreCheckSummaryNode
{
    public Guid NodeId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string WorkloadStatus { get; set; } = string.Empty; // "Current", "Drifted", "Absent", "Unknown"
    public string Action { get; set; } = string.Empty; // "Skip", "FreshInstall", "Update", "InstallMissing", "Reinstall", "BlockedDowngrade"
    public string? ActionDetail { get; set; }
    public List<PreCheckSummaryPackage> Packages { get; set; } = new();
}

public class PreCheckSummaryPackage
{
    public Guid PackageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "AlreadySatisfied", "WrongVersion", "NotPresent"
    public string? Comparison { get; set; }
    public string? ActualVersion { get; set; }
    public string? ExpectedVersion { get; set; }
}
