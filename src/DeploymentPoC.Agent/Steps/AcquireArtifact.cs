using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace DeploymentPoC.Agent.Steps;

public sealed class AcquireArtifact
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly HttpClient _http;
    private readonly string? _artifactRootPath;
    private readonly HashSet<string>? _allowedHosts;

    public AcquireArtifact(HttpClient http, AcquireArtifactOptions? options = null)
    {
        _http = http;

        if (!string.IsNullOrWhiteSpace(options?.ArtifactRootPath))
        {
            _artifactRootPath = NormalizeDirectoryPath(options.ArtifactRootPath!);
        }

        if (options?.AllowedHosts is { Count: > 0 })
        {
            var filteredAllowedHosts = options.AllowedHosts
                .Where(static host => !string.IsNullOrWhiteSpace(host))
                .Select(static host => host.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (filteredAllowedHosts.Count > 0)
            {
                _allowedHosts = filteredAllowedHosts;
            }
        }
    }

    public async Task<AcquireArtifactResult> ExecuteAsync(AcquireArtifactRequest request, CancellationToken ct = default)
    {
        if (request is null)
        {
            return new AcquireArtifactResult { Success = false, Error = "invalid_request" };
        }

        if (string.IsNullOrWhiteSpace(request.ArtifactUrl) || string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return new AcquireArtifactResult { Success = false, Error = "invalid_request" };
        }

        if (request.ChunkSizeBytes <= 0)
        {
            return new AcquireArtifactResult { Success = false, Error = "invalid_chunk_size" };
        }

        if (!TryValidateArtifactUri(request.ArtifactUrl, out var artifactUri, out var urlValidationError))
        {
            return new AcquireArtifactResult { Success = false, Error = urlValidationError };
        }

        if (!TryResolveDestinationPath(request.DestinationPath, out var destinationPath, out var destinationValidationError))
        {
            return new AcquireArtifactResult { Success = false, Error = destinationValidationError };
        }

        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, artifactUri);
            using var headResponse = await _http.SendAsync(headRequest, ct);
            if (!headResponse.IsSuccessStatusCode)
            {
                return new AcquireArtifactResult { Success = false, Error = $"head_{(int)headResponse.StatusCode}" };
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var expectedLength = headResponse.Content.Headers.ContentLength;

            AcquireArtifactResult? downloadFailure = null;
            long bytesWritten;

            await using (var output = File.Create(destinationPath))
            {
                if (expectedLength is null or <= 0)
                {
                    bytesWritten = await DownloadFullAsync(artifactUri, output, ct);
                }
                else
                {
                    var from = 0L;

                    while (from < expectedLength.Value)
                    {
                        var to = Math.Min(from + request.ChunkSizeBytes - 1, expectedLength.Value - 1);

                        using var getRequest = new HttpRequestMessage(HttpMethod.Get, artifactUri);
                        getRequest.Headers.Range = new RangeHeaderValue(from, to);

                        using var response = await _http.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                        if (response.StatusCode == HttpStatusCode.PartialContent)
                        {
                            if (!IsValidContentRange(response.Content.Headers.ContentRange, from, to, expectedLength.Value))
                            {
                                downloadFailure = new AcquireArtifactResult
                                {
                                    Success = false,
                                    Error = "invalid_partial_content_range"
                                };
                                break;
                            }

                            var expectedChunkLength = to - from + 1;
                            await using var responseBody = await response.Content.ReadAsStreamAsync(ct);
                            var copiedChunkLength = await CopyToAsyncCountingBytes(responseBody, output, ct);
                            if (copiedChunkLength != expectedChunkLength)
                            {
                                downloadFailure = new AcquireArtifactResult
                                {
                                    Success = false,
                                    Error = "invalid_partial_content_length"
                                };
                                break;
                            }

                            from = to + 1;
                            continue;
                        }

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            output.SetLength(0);
                            output.Position = 0;
                            await response.Content.CopyToAsync(output, ct);
                            break;
                        }

                        downloadFailure = new AcquireArtifactResult
                        {
                            Success = false,
                            Error = $"range_{(int)response.StatusCode}"
                        };
                        break;
                    }

                    bytesWritten = output.Length;
                }
            }

            if (downloadFailure is not null)
            {
                TryDeletePartialFile(destinationPath);
                return downloadFailure;
            }

            var finalResult = await FinalizeResultAsync(request, destinationPath, bytesWritten, ct);
            if (!finalResult.Success)
            {
                TryDeletePartialFile(destinationPath);
            }

            return finalResult;
        }
        catch (HttpRequestException)
        {
            TryDeletePartialFile(destinationPath);
            return new AcquireArtifactResult { Success = false, Error = "http_request_failed" };
        }
        catch (UnauthorizedAccessException)
        {
            TryDeletePartialFile(destinationPath);
            return new AcquireArtifactResult { Success = false, Error = "filesystem_access_denied" };
        }
        catch (System.Security.SecurityException)
        {
            TryDeletePartialFile(destinationPath);
            return new AcquireArtifactResult { Success = false, Error = "filesystem_access_denied" };
        }
        catch (IOException)
        {
            TryDeletePartialFile(destinationPath);
            return new AcquireArtifactResult { Success = false, Error = "io_failure" };
        }
    }

    private async Task<long> DownloadFullAsync(Uri artifactUri, Stream output, CancellationToken ct)
    {
        using var response = await _http.GetAsync(artifactUri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(output, ct);
        return output.Length;
    }

    private static async Task<long> CopyToAsyncCountingBytes(Stream source, Stream destination, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long totalCopied = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalCopied += bytesRead;
        }

        return totalCopied;
    }

    private static async Task<AcquireArtifactResult> FinalizeResultAsync(
        AcquireArtifactRequest request,
        string destinationPath,
        long bytesWritten,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256))
        {
            await using var verify = File.OpenRead(destinationPath);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(verify, ct);
            var actual = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(actual, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new AcquireArtifactResult { Success = false, Error = "hash_mismatch" };
            }
        }

        return new AcquireArtifactResult
        {
            Success = true,
            Transport = "http",
            BytesWritten = bytesWritten
        };
    }

    private bool TryValidateArtifactUri(string artifactUrl, out Uri artifactUri, out string error)
    {
        if (!Uri.TryCreate(artifactUrl, UriKind.Absolute, out artifactUri!))
        {
            error = "invalid_artifact_url";
            return false;
        }

        if (artifactUri.Scheme is not ("http" or "https"))
        {
            error = "invalid_artifact_url";
            return false;
        }

        if (_allowedHosts is not null && !_allowedHosts.Contains(artifactUri.Host))
        {
            error = "untrusted_artifact_host";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryResolveDestinationPath(string destinationPath, out string resolvedDestinationPath, out string error)
    {
        try
        {
            resolvedDestinationPath = CanonicalizePathBestEffort(destinationPath);
        }
        catch (Exception) when (destinationPath is not null)
        {
            resolvedDestinationPath = string.Empty;
            error = "invalid_destination_path";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_artifactRootPath))
        {
            error = string.Empty;
            return true;
        }

        if (!IsPathUnderRoot(resolvedDestinationPath, _artifactRootPath))
        {
            error = "invalid_destination_path";
            return false;
        }

        // Best-effort hardening: if any existing ancestor directory from destination parent
        // up to configured root is a symlink (or cannot be safely inspected), reject path.
        if (HasSymlinkTraversalRisk(resolvedDestinationPath, _artifactRootPath))
        {
            error = "invalid_destination_path";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string CanonicalizePathBestEffort(string path)
    {
        // Path.GetFullPath removes relative segments and normalizes separators.
        // Existing filesystem entries are re-read through FileSystemInfo for stable canonical text.
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
        {
            return new FileInfo(fullPath).FullName;
        }

        if (Directory.Exists(fullPath))
        {
            return new DirectoryInfo(fullPath).FullName;
        }

        return fullPath;
    }

    private static bool HasSymlinkTraversalRisk(string destinationFullPath, string normalizedRootPath)
    {
        var parent = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var rootPath = TrimTrailingDirectorySeparators(normalizedRootPath);
        var current = Path.GetFullPath(parent);

        while (current.StartsWith(rootPath, PathComparison))
        {
            if (IsDirectorySymlinkOrInspectionFailed(current))
            {
                return true;
            }

            if (string.Equals(current, rootPath, PathComparison))
            {
                break;
            }

            var next = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, current, PathComparison))
            {
                break;
            }

            current = next;
        }

        return false;
    }

    private static bool IsDirectorySymlinkOrInspectionFailed(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsValidContentRange(ContentRangeHeaderValue? contentRange, long from, long to, long expectedLength)
    {
        if (contentRange is null || !contentRange.HasRange || contentRange.From is null || contentRange.To is null)
        {
            return false;
        }

        if (!string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (contentRange.From.Value != from || contentRange.To.Value != to)
        {
            return false;
        }

        return contentRange.Length is not null && contentRange.Length.Value == expectedLength;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    private static bool IsPathUnderRoot(string fullPath, string normalizedRootPath)
    {
        return fullPath.StartsWith(normalizedRootPath, PathComparison);
    }

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
