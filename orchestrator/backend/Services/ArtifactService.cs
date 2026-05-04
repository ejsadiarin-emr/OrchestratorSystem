using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orchestrator.Configuration;
using Orchestrator.Models;

namespace Orchestrator.Services;

public class ArtifactService : IArtifactService
{
    private readonly AppDbContext _dbContext;
    private readonly ArtifactOptions _options;

    public ArtifactService(AppDbContext dbContext, IOptions<ArtifactOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<Artifact> UploadAsync(string packageId, string version, string packageName, IFormFile file)
    {
        var basePath = Path.GetFullPath(_options.BasePath);
        Directory.CreateDirectory(basePath);

        var fileName = Path.GetFileName(file.FileName);
        var safeFileName = $"{packageId}_{version}_{fileName}";
        var filePath = Path.Combine(basePath, safeFileName);

        using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream);
        }

        var artifact = new Artifact
        {
            PackageId = packageId,
            PackageName = packageName,
            Version = version,
            InstallerFile = safeFileName,
            ManifestPath = Path.Combine(basePath, $"{packageId}_{version}_manifest.json"),
            BinaryPath = filePath,
            UploadedAt = DateTime.UtcNow
        };

        _dbContext.Artifacts.Add(artifact);
        await _dbContext.SaveChangesAsync();
        return artifact;
    }

    public async Task<IEnumerable<Artifact>> ImportAsync(IEnumerable<IFormFile> files)
    {
        var imported = new List<Artifact>();
        foreach (var file in files)
        {
            // Extract packageId and version from filename: e.g., "MyPackage_1.0.0.msi"
            var fileName = Path.GetFileNameWithoutExtension(file.FileName);
            var parts = fileName.Split('_');
            if (parts.Length >= 2)
            {
                var packageId = parts[0];
                var version = parts[1];
                var artifact = await UploadAsync(packageId, version, packageId, file);
                imported.Add(artifact);
            }
        }
        return imported;
    }

    public async Task<IEnumerable<Artifact>> GetAllAsync()
    {
        return await _dbContext.Artifacts
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync();
    }
}
