using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orchestrator.Configuration;
using Orchestrator.Models;

namespace Orchestrator.Services;

public class WorkloadService : IWorkloadService
{
    private readonly AppDbContext _dbContext;
    private readonly WorkloadDefinitionStoreOptions _options;
    private readonly ILogger<WorkloadService> _logger;

    public WorkloadService(AppDbContext dbContext, IOptions<WorkloadDefinitionStoreOptions> options, ILogger<WorkloadService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WorkloadImportResult> ImportAsync(IEnumerable<WorkloadDto> dtos)
    {
        var result = new WorkloadImportResult();

        foreach (var dto in dtos)
        {
            try
            {
                var existing = await _dbContext.Workloads
                    .Include(w => w.Packages)
                    .FirstOrDefaultAsync(w => w.WorkloadId == dto.WorkloadId && w.Version == dto.Version);

                Workload workload;
                bool isUpdate = existing != null;
                if (existing != null)
                {
                    workload = existing;
                    workload.WorkloadName = dto.WorkloadName;
                    _dbContext.WorkloadPackages.RemoveRange(workload.Packages);
                }
                else
                {
                    workload = new Workload
                    {
                        WorkloadId = dto.WorkloadId,
                        WorkloadName = dto.WorkloadName,
                        Version = dto.Version
                    };
                    _dbContext.Workloads.Add(workload);
                }

                foreach (var pkgDto in dto.Packages)
                {
                    var package = new WorkloadPackage
                    {
                        WorkloadId = dto.WorkloadId,
                        WorkloadVersion = dto.Version,
                        PackageId = pkgDto.PackageId,
                        PackageVersion = pkgDto.Version,
                        PreInitSteps = pkgDto.PreInitSteps != null ? string.Join("\n", pkgDto.PreInitSteps) : null,
                        PostInitSteps = pkgDto.PostInitSteps != null ? string.Join("\n", pkgDto.PostInitSteps) : null
                    };
                    workload.Packages.Add(package);
                }

                await _dbContext.SaveChangesAsync();

                // Save JSON definition to file
                var definitionsPath = Path.GetFullPath(_options.Path);
                var workloadDir = Path.Combine(definitionsPath, dto.WorkloadId);
                Directory.CreateDirectory(workloadDir);
                var filePath = Path.Combine(workloadDir, $"{dto.Version}.json");
                var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(filePath, json);
                workload.DefinitionPath = filePath;
                await _dbContext.SaveChangesAsync();

                if (isUpdate)
                    result.Updated.Add(workload);
                else
                    result.Imported.Add(workload);
            }
            catch (Exception ex)
            {
                result.Failed.Add((dto.WorkloadId, dto.Version, ex.Message));
                _logger.LogWarning(ex, "failed to import workload {WorkloadId} v{Version}", dto.WorkloadId, dto.Version);
            }
        }

        return result;
    }

    public async Task<Workload?> GetByIdAsync(string workloadId, string version)
    {
        return await _dbContext.Workloads
            .Include(w => w.Packages)
            .FirstOrDefaultAsync(w => w.WorkloadId == workloadId && w.Version == version);
    }

    public async Task<IEnumerable<Workload>> GetAllAsync()
    {
        return await _dbContext.Workloads
            .Include(w => w.Packages)
            .OrderByDescending(w => w.UploadedAt)
            .ToListAsync();
    }
}
