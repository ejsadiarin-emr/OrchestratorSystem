using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class WorkloadImportService
{
    private readonly InstallerDbContext _db;

    public WorkloadImportService(InstallerDbContext db)
    {
        _db = db;
    }

    public List<PackageAssignment> MapToPackageAssignments(ResolvedManifest manifest)
    {
        return
        [
            new PackageAssignment
            {
                PackageIndex = 0,
                PackageId = manifest.PackageId,
                Version = manifest.Version,
                Channel = manifest.Channel,
                InstallAdapter = new InstallAdapterConfig
                {
                    Type = manifest.InstallAdapter.Type,
                    Command = manifest.InstallAdapter.Command,
                    Arguments = manifest.InstallAdapter.Arguments,
                    ExpectedExitCodes = manifest.InstallAdapter.ExpectedExitCodes,
                    TimeoutSeconds = manifest.InstallAdapter.TimeoutSeconds
                },
                Detection = new DetectionConfig
                {
                    Type = manifest.Detection.Type,
                    Path = manifest.Detection.Path,
                    ExpectedVersion = manifest.Detection.ExpectedVersion
                }
            }
        ];
    }

    public async Task<List<Guid>> EnsurePackageEntitiesAsync(ResolvedManifest manifest)
    {
        var sourcePath = $"{manifest.PackageId}/{manifest.Version}/artifact.bin";
        var existing = await _db.Packages
            .SingleOrDefaultAsync(p => p.Name == manifest.PackageId && p.Version == manifest.Version);

        if (existing is not null)
        {
            return [existing.PackageId];
        }

        var entity = new PackageEntity
        {
            PackageId = Guid.NewGuid(),
            Name = manifest.PackageId,
            Version = manifest.Version,
            SourcePath = sourcePath,
            InstallType = manifest.InstallAdapter.Type,
            InstallArgs = manifest.InstallAdapter.Arguments,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Packages.Add(entity);
        await _db.SaveChangesAsync();

        return [entity.PackageId];
    }

    public async Task<List<WorkloadPackageEntity>> CreateWorkloadPackageEntitiesAsync(
        Guid revisionId,
        List<(Guid PackageId, int Index)> packageEntries)
    {
        var entities = packageEntries.Select(entry => new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = revisionId,
            PackageId = entry.PackageId,
            PackageIndex = entry.Index
        }).ToList();

        _db.WorkloadPackages.AddRange(entities);
        await _db.SaveChangesAsync();

        return entities;
    }
}