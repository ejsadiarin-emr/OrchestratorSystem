using System.Text.Json;
using DeploymentPoC.Orchestrator.Data;
using Microsoft.EntityFrameworkCore;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class PolicyEvaluationService
{
    private readonly ArtifactStoreService _artifactStore;

    public PolicyEvaluationService(ArtifactStoreService artifactStore)
    {
        _artifactStore = artifactStore;
    }

    public async Task<string> EvaluateRunRiskAsync(
        Guid revisionId,
        InstallerDbContext db,
        CancellationToken cancellationToken = default)
    {
        var revision = await db.WorkloadRevisions
            .Include(r => r.Packages)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RevisionId == revisionId, cancellationToken);

        if (revision?.Packages is null || revision.Packages.Count == 0)
        {
            return "low";
        }

        var packageIds = revision.Packages.Select(p => p.PackageId).ToList();
        var packages = await db.Packages
            .AsNoTracking()
            .Where(p => packageIds.Contains(p.PackageId))
            .ToListAsync(cancellationToken);

        var maxRisk = "low";
        foreach (var package in packages)
        {
            var manifestJson = await _artifactStore.GetResolvedManifestAsync(
                package.Name,
                package.Version,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                continue;
            }

            var manifest = JsonSerializer.Deserialize<ResolvedManifest>(
                manifestJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest?.PolicyTags?.RiskLevel is null)
            {
                continue;
            }

            var risk = manifest.PolicyTags.RiskLevel.ToLowerInvariant();
            if (risk == "high")
            {
                return "high";
            }

            if (risk == "medium" && maxRisk == "low")
            {
                maxRisk = "medium";
            }
        }

        return maxRisk;
    }
}
