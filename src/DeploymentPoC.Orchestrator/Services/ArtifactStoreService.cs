using System.Security.Cryptography;
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
