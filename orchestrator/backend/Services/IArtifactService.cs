using Orchestrator.Models;

public interface IArtifactService
{
    Task<Artifact> UploadAsync(string packageId, string version, string packageName, IFormFile file);
    Task<IEnumerable<Artifact>> ImportAsync(IEnumerable<IFormFile> files);
    Task<IEnumerable<Artifact>> GetAllAsync();
}
