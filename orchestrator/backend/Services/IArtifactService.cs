using Orchestrator.Models;

public interface IArtifactService
{
    Task<Artifact> UploadAsync(PackageManifest manifest, IFormFile binaryFile);
    Task<(List<Artifact> imported, List<(string fileName, string reason)> failed)> ImportZipAsync(IFormFile zipFile);
    Task<IEnumerable<Artifact>> GetAllAsync();
}
