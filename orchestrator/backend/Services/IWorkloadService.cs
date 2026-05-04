using Orchestrator.Models;

public interface IWorkloadService
{
    Task<WorkloadImportResult> ImportAsync(IEnumerable<WorkloadDto> dtos);
    Task<Workload?> GetByIdAsync(string workloadId, string version);
    Task<IEnumerable<Workload>> GetAllAsync();
}

public class WorkloadImportResult
{
    public List<Workload> Imported { get; set; } = [];
    public List<Workload> Updated { get; set; } = [];
    public List<(string workloadId, string version, string reason)> Failed { get; set; } = [];
}
