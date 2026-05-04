using Orchestrator.Models;

public interface IWorkloadService
{
    Task<Workload> UpsertAsync(WorkloadDto dto);
    Task<Workload?> GetByIdAsync(string workloadId, string version);
    Task<IEnumerable<Workload>> GetAllAsync();
}
