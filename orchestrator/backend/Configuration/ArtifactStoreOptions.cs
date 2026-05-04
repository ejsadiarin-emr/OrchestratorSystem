namespace Orchestrator.Configuration;

public class ArtifactStoreOptions
{
    public string BasePath { get; set; } = "./artifacts";

    public string ResolvePath() =>
        Path.IsPathRooted(BasePath) ? BasePath : Path.Combine(AppContext.BaseDirectory, BasePath);
}
