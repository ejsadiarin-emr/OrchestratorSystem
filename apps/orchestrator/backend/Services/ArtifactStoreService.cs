using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class ArtifactStoreService
{
    private static readonly Regex SafeSegmentRegex = new("^[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.Compiled);
    private readonly string _rootPath;
    private readonly string _rootPathWithSeparator;

    public ArtifactStoreService(IConfiguration configuration)
    {
        var configuredRoot = configuration["ArtifactStore:RootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "artifacts");

        _rootPath = Path.GetFullPath(configuredRoot);
        _rootPathWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(_rootPath);
    }

    public readonly record struct ArtifactFileMetadata(long Length, long LastWriteUtcTicks);

    public bool Exists(string packageId, string version)
    {
        return File.Exists(GetArtifactPath(packageId, version));
    }

    public async Task SaveArtifactAsync(string packageId, string version, Stream source, CancellationToken cancellationToken = default)
    {
        var artifactPath = GetArtifactPath(packageId, version);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);

        await using var file = File.Create(artifactPath);
        source.Position = 0;
        await source.CopyToAsync(file, cancellationToken);
    }

    public async Task SaveResolvedManifestAsync(string packageId, string version, string manifestJson, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestPath(packageId, version);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8, cancellationToken);
    }

    public async Task<string?> GetResolvedManifestAsync(string packageId, string version, CancellationToken cancellationToken = default)
    {
        var manifestPath = GetManifestPath(packageId, version);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(manifestPath, Encoding.UTF8, cancellationToken);
    }

    public long GetSize(string packageId, string version)
    {
        return new FileInfo(GetArtifactPath(packageId, version)).Length;
    }

    public Stream OpenRead(string packageId, string version)
    {
        return File.OpenRead(GetArtifactPath(packageId, version));
    }

    public bool TryGetMetadata(string packageId, string version, out ArtifactFileMetadata metadata)
    {
        try
        {
            var fileInfo = new FileInfo(GetArtifactPath(packageId, version));
            if (!fileInfo.Exists)
            {
                metadata = default;
                return false;
            }

            metadata = new ArtifactFileMetadata(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);
            return true;
        }
        catch (FileNotFoundException)
        {
            metadata = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            metadata = default;
            return false;
        }
    }

    public string ComputeSha256(string packageId, string version)
    {
        using var stream = OpenRead(packageId, version);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public List<ArtifactListItem> ListArtifacts()
    {
        var results = new List<ArtifactListItem>();
        if (!Directory.Exists(_rootPath))
        {
            return results;
        }

        foreach (var packageDir in Directory.GetDirectories(_rootPath))
        {
            var packageId = Path.GetFileName(packageDir);
            if (string.IsNullOrWhiteSpace(packageId))
            {
                continue;
            }

            foreach (var versionDir in Directory.GetDirectories(packageDir))
            {
                var version = Path.GetFileName(versionDir);
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                var manifestPath = Path.Combine(versionDir, "resolved-manifest.json");
                var artifactPath = Path.Combine(versionDir, "artifact.bin");
                if (!File.Exists(manifestPath) || !File.Exists(artifactPath))
                {
                    continue;
                }

                try
                {
                    var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
                    var manifest = System.Text.Json.JsonSerializer.Deserialize<ResolvedManifestSummary>(manifestJson, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    long size = 0;
                    try
                    {
                        size = new FileInfo(artifactPath).Length;
                    }
                    catch
                    {
                        // ignore size read failures
                    }

                    string? digest = null;
                    try
                    {
                        digest = ComputeSha256(packageId, version);
                    }
                    catch
                    {
                        // ignore digest compute failures
                    }

                    results.Add(new ArtifactListItem
                    {
                        Id = $"{packageId}-{version}",
                        PackageId = packageId,
                        Version = version,
                        FileName = manifest?.PackageId is not null ? $"{manifest.PackageId}-{version}.bin" : $"{packageId}-{version}.bin",
                        Channel = manifest?.Channel,
                        ArtifactType = manifest?.ArtifactType,
                        VerificationResult = manifest?.OriginMetadata?.VerificationResult,
                        SizeBytes = size,
                        Digest = digest,
                        CreatedAt = File.GetCreationTimeUtc(artifactPath).ToString("O"),
                        InstallAdapterCommand = manifest?.InstallAdapter?.Command,
                        DetectionType = manifest?.Detection?.Type,
                        DetectionPath = manifest?.Detection?.Path,
                        RiskLevel = manifest?.PolicyTags?.RiskLevel,
                    });
                }
                catch
                {
                    // Skip corrupted entries
                }
            }
        }

        return results.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public sealed class ArtifactListItem
    {
        public string Id { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? Channel { get; set; }
        public string? ArtifactType { get; set; }
        public string? VerificationResult { get; set; }
        public long SizeBytes { get; set; }
        public string? Digest { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? InstallAdapterCommand { get; set; }
        public string? DetectionType { get; set; }
        public string? DetectionPath { get; set; }
        public string? RiskLevel { get; set; }
    }

    private sealed class ResolvedManifestSummary
    {
        public string? PackageId { get; set; }
        public string? Version { get; set; }
        public string? Channel { get; set; }
        public string? ArtifactType { get; set; }
        public InstallAdapterSummary? InstallAdapter { get; set; }
        public DetectionSummary? Detection { get; set; }
        public OriginMetadataSummary? OriginMetadata { get; set; }
        public PolicyTagsSummary? PolicyTags { get; set; }
    }

    private sealed class InstallAdapterSummary
    {
        public string? Command { get; set; }
    }

    private sealed class DetectionSummary
    {
        public string? Type { get; set; }
        public string? Path { get; set; }
    }

    private sealed class OriginMetadataSummary
    {
        public string? VerificationResult { get; set; }
    }

    private sealed class PolicyTagsSummary
    {
        public string? RiskLevel { get; set; }
    }

    private string GetArtifactPath(string packageId, string version)
    {
        ValidatePathSegment(packageId, nameof(packageId));
        ValidatePathSegment(version, nameof(version));

        var candidatePath = Path.GetFullPath(Path.Combine(_rootPath, packageId, version, "artifact.bin"));
        if (!candidatePath.StartsWith(_rootPathWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException("Resolved path is outside artifact root.");
        }

        return candidatePath;
    }

    private string GetManifestPath(string packageId, string version)
    {
        ValidatePathSegment(packageId, nameof(packageId));
        ValidatePathSegment(version, nameof(version));

        var candidatePath = Path.GetFullPath(Path.Combine(_rootPath, packageId, version, "resolved-manifest.json"));
        if (!candidatePath.StartsWith(_rootPathWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException("Resolved path is outside artifact root.");
        }

        return candidatePath;
    }

    private static void ValidatePathSegment(string segment, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        if (segment is "." or "..")
        {
            throw new ArgumentException($"{parameterName} cannot contain dot segments.", parameterName);
        }

        if (segment.Contains('/') || segment.Contains('\\'))
        {
            throw new ArgumentException($"{parameterName} cannot contain path separators.", parameterName);
        }

        if (!SafeSegmentRegex.IsMatch(segment))
        {
            throw new ArgumentException($"{parameterName} contains unsupported characters.", parameterName);
        }
    }
}
