using System.Security.Cryptography;
using System.Text;
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
                    UninstallArgs = manifest.InstallAdapter.UninstallArgs,
                    UninstallCommand = manifest.InstallAdapter.UninstallCommand,
                    UpgradeBehavior = manifest.InstallAdapter.UpgradeBehavior,
                    ExpectedExitCodes = manifest.InstallAdapter.ExpectedExitCodes,
                    TimeoutSeconds = manifest.InstallAdapter.TimeoutSeconds
                },
                Detection = new DetectionConfig
                {
                    Type = manifest.Detection.Type,
                    Path = manifest.Detection.Path
                }
            }
        ];
    }

    public async Task<List<Guid>> EnsurePackageEntitiesAsync(ResolvedManifest manifest)
    {
        var sourcePath = manifest.InstallAdapter?.Command ?? string.Empty;
        var existing = await _db.Packages
            .SingleOrDefaultAsync(p => p.Name == manifest.PackageId && p.Version == manifest.Version);

        if (existing is not null)
        {
            var hasUpdates = false;
            if (string.IsNullOrEmpty(existing.UninstallCommand))
            {
                existing.UninstallCommand = manifest.InstallAdapter?.UninstallCommand ?? string.Empty;
                hasUpdates = true;
            }
            if (string.IsNullOrEmpty(existing.UninstallArgs))
            {
                existing.UninstallArgs = manifest.InstallAdapter?.UninstallArgs ?? string.Empty;
                hasUpdates = true;
            }
            if (hasUpdates)
            {
                await _db.SaveChangesAsync();
            }
            return [existing.PackageId];
        }

        var entity = new PackageEntity
        {
            PackageId = DeterministicGuid($"{manifest.PackageId}-{manifest.Version}"),
            Name = manifest.PackageId,
            Version = manifest.Version,
            SourcePath = sourcePath,
            InstallType = manifest.InstallAdapter?.Type ?? "exe",
            InstallArgs = manifest.InstallAdapter?.Arguments ?? string.Empty,
            UninstallArgs = manifest.InstallAdapter?.UninstallArgs ?? string.Empty,
            UninstallCommand = manifest.InstallAdapter?.UninstallCommand ?? string.Empty,
            UpgradeBehavior = manifest.InstallAdapter?.UpgradeBehavior ?? "InPlace",
            DetectionConfigJson = System.Text.Json.JsonSerializer.Serialize(manifest.Detection),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Packages.Add(entity);
        await _db.SaveChangesAsync();

        return [entity.PackageId];
    }

    private static Guid DeterministicGuid(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
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