using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orchestrator.Configuration;
using Orchestrator.Models;
using System.IO.Compression;

namespace Orchestrator.Services;

public class ArtifactService : IArtifactService
{
    private readonly AppDbContext _dbContext;
    private readonly ArtifactStoreOptions _options;
    private readonly ILogger<ArtifactService> _logger;

    public ArtifactService(AppDbContext dbContext, IOptions<ArtifactStoreOptions> options, ILogger<ArtifactService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Artifact> UploadAsync(PackageManifest manifest, IFormFile binaryFile)
    {
        manifest.Validate();

        var binaryFileName = Path.GetFileName(binaryFile.FileName);
        if (!string.Equals(manifest.InstallerFile, binaryFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"manifest 'installerFile' ({manifest.InstallerFile}) does not match binary file name ({binaryFileName})");

        var existing = await _dbContext.Artifacts
            .FirstOrDefaultAsync(a => a.PackageId == manifest.PackageId && a.Version == manifest.Version);

        if (existing != null)
            throw new InvalidOperationException($"artifact with packageId '{manifest.PackageId}' and version '{manifest.Version}' already exists");

        var basePath = Path.GetFullPath(_options.BasePath);
        var artifactDir = Path.Combine(basePath, manifest.PackageId, manifest.Version);
        Directory.CreateDirectory(artifactDir);

        var manifestPath = Path.Combine(artifactDir, $"{manifest.PackageId}_{manifest.Version}_manifest.json");
        var binaryPath = Path.Combine(artifactDir, binaryFileName);

        await using (var manifestStream = File.Create(manifestPath))
        {
            await System.Text.Json.JsonSerializer.SerializeAsync(manifestStream, manifest, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        await using (var binaryStream = File.Create(binaryPath))
        {
            await binaryFile.CopyToAsync(binaryStream);
        }

        var artifact = new Artifact
        {
            PackageId = manifest.PackageId,
            PackageName = manifest.PackageName,
            Version = manifest.Version,
            InstallerFile = binaryFileName,
            ManifestPath = manifestPath,
            BinaryPath = binaryPath,
            UploadedAt = DateTime.UtcNow
        };

        _dbContext.Artifacts.Add(artifact);
        await _dbContext.SaveChangesAsync();
        return artifact;
    }

    public async Task<(List<Artifact> imported, List<(string fileName, string reason)> failed)> ImportZipAsync(IFormFile zipFile)
    {
        var imported = new List<Artifact>();
        var failed = new List<(string fileName, string reason)>();

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "upload.zip");
            await using (var zipStream = File.Create(zipPath))
            {
                await zipFile.CopyToAsync(zipStream);
            }

            var extractDir = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extractDir);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    var entryPath = Path.Combine(extractDir, entry.Name);
                    entry.ExtractToFile(entryPath, overwrite: true);
                }
            }

            var files = Directory.GetFiles(extractDir);
            var manifests = files.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var manifestPath in manifests)
            {
                var stem = Path.GetFileNameWithoutExtension(manifestPath);
                var binaryPath = files.FirstOrDefault(f =>
                {
                    var fileStem = Path.GetFileNameWithoutExtension(f);
                    return string.Equals(fileStem, stem, StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                });

                if (binaryPath == null)
                {
                    failed.Add((Path.GetFileName(manifestPath), $"no binary found for stem '{stem}'"));
                    continue;
                }

                try
                {
                    PackageManifest manifest;
                    await using (var stream = File.OpenRead(manifestPath))
                    {
                        manifest = await System.Text.Json.JsonSerializer.DeserializeAsync<PackageManifest>(stream)
                            ?? throw new InvalidOperationException("manifest is empty or invalid");
                    }

                    await using var binaryStream = File.OpenRead(binaryPath);
                    var formFile = new FormFile(binaryStream, 0, binaryStream.Length, "file", Path.GetFileName(binaryPath))
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = "application/octet-stream"
                    };

                    var artifact = await UploadAsync(manifest, formFile);
                    imported.Add(artifact);
                }
                catch (Exception ex)
                {
                    failed.Add((Path.GetFileName(manifestPath), ex.Message));
                    _logger.LogWarning(ex, "failed to import artifact from manifest {ManifestPath}", manifestPath);
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "failed to clean up temp directory {TempDir}", tempDir);
            }
        }

        return (imported, failed);
    }

    public async Task<IEnumerable<Artifact>> GetAllAsync()
    {
        return await _dbContext.Artifacts
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync();
    }
}
