namespace DeploymentPoC.Agent.Steps;

public sealed class AcquireArtifactRequest
{
    public string ArtifactUrl { get; set; } = string.Empty;

    public string DestinationPath { get; set; } = string.Empty;

    public int ChunkSizeBytes { get; set; } = 8 * 1024 * 1024;

    public bool UseChunkedDownload { get; set; } = true;

    public string? ExpectedSha256 { get; set; }

    public int DownloadTimeoutSeconds { get; set; }
}

public sealed class AcquireArtifactOptions
{
    public string? ArtifactRootPath { get; init; }

    public IReadOnlyCollection<string>? AllowedHosts { get; init; }
}

public sealed class AcquireArtifactResult
{
    public bool Success { get; set; }

    public string Transport { get; set; } = "http";

    public long BytesWritten { get; set; }

    public string? Error { get; set; }
}
