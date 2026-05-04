namespace Orchestrator.Models;

public class Workload
{
    public int Id { get; set; }
    public string WorkloadId { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DefinitionPath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public ICollection<WorkloadPackage> Packages { get; set; } = [];
}
