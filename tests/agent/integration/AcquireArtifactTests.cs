using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using DeploymentPoC.Agent.Steps;
using NUnit.Framework;

namespace DeploymentPoC.Agent.IntegrationTests;

public sealed class AcquireArtifactTests
{
    [Test]
    public async Task AcquireArtifact_DownloadsUsingHttpOnly_AndUsesRangeLoop()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "https://unit.test/artifact.bin",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 5
            });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Transport, Is.EqualTo("http"));
            Assert.That(result.BytesWritten, Is.EqualTo(payload.Length));

            var bytes = await File.ReadAllBytesAsync(destinationPath);
            Assert.That(bytes, Is.EqualTo(payload));

            Assert.That(handler.HeadRequestCount, Is.EqualTo(1));
            Assert.That(handler.GetRequestCount, Is.EqualTo(3));
            Assert.That(handler.RangeRequests, Is.EqualTo(new[] { "bytes=0-4", "bytes=5-9", "bytes=10-11" }));
        }
        finally
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
    }

    [Test]
    public async Task AcquireArtifact_FallsBackToFullGet_WhenRangeNotSupported()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: false);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "https://unit.test/artifact.bin",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 4
            });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Transport, Is.EqualTo("http"));
            Assert.That(result.BytesWritten, Is.EqualTo(payload.Length));

            var bytes = await File.ReadAllBytesAsync(destinationPath);
            Assert.That(bytes, Is.EqualTo(payload));

            Assert.That(handler.HeadRequestCount, Is.EqualTo(1));
            Assert.That(handler.GetRequestCount, Is.EqualTo(1));
            Assert.That(handler.RangeRequests, Is.EqualTo(new[] { "bytes=0-3" }));
        }
        finally
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
    }

    [Test]
    public async Task AcquireArtifact_BlocksDestinationOutsideConfiguredRoot()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);

        var rootPath = Path.Combine(Path.GetTempPath(), $"artifact-root-{Guid.NewGuid():N}");
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.bin");
        var acquire = new AcquireArtifact(http, new AcquireArtifactOptions { ArtifactRootPath = rootPath });

        var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
        {
            ArtifactUrl = "https://unit.test/artifact.bin",
            DestinationPath = outsidePath,
            ChunkSizeBytes = 4
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("invalid_destination_path"));
        Assert.That(File.Exists(outsidePath), Is.False);
        Assert.That(handler.HeadRequestCount, Is.EqualTo(0));
        Assert.That(handler.GetRequestCount, Is.EqualTo(0));
    }

    [Test]
    public async Task AcquireArtifact_RejectsInvalidArtifactUrl()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
        {
            ArtifactUrl = "../relative/path",
            DestinationPath = destinationPath,
            ChunkSizeBytes = 4
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("invalid_artifact_url"));
        Assert.That(File.Exists(destinationPath), Is.False);
        Assert.That(handler.HeadRequestCount, Is.EqualTo(0));
        Assert.That(handler.GetRequestCount, Is.EqualTo(0));
    }

    [Test]
    public async Task AcquireArtifact_RejectsArtifactHostNotInAllowlist()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http, new AcquireArtifactOptions
        {
            AllowedHosts = new[] { "trusted.test" }
        });

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
        {
            ArtifactUrl = "https://unit.test/artifact.bin",
            DestinationPath = destinationPath,
            ChunkSizeBytes = 4
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("untrusted_artifact_host"));
        Assert.That(File.Exists(destinationPath), Is.False);
        Assert.That(handler.HeadRequestCount, Is.EqualTo(0));
        Assert.That(handler.GetRequestCount, Is.EqualTo(0));
    }

    [Test]
    public async Task AcquireArtifact_AllowsArtifactHostWhenInAllowlist()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http, new AcquireArtifactOptions
        {
            AllowedHosts = new[] { "unit.test" }
        });

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "https://unit.test/artifact.bin",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 5
            });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Transport, Is.EqualTo("http"));
            Assert.That(result.BytesWritten, Is.EqualTo(payload.Length));

            var bytes = await File.ReadAllBytesAsync(destinationPath);
            Assert.That(bytes, Is.EqualTo(payload));

            Assert.That(handler.HeadRequestCount, Is.EqualTo(1));
            Assert.That(handler.GetRequestCount, Is.EqualTo(3));
        }
        finally
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
    }

    [Test]
    public async Task AcquireArtifact_DoesNotBlockWhenAllowlistContainsOnlyWhitespace()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http, new AcquireArtifactOptions
        {
            AllowedHosts = new[] { "   ", "\t", "" }
        });

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "https://unit.test/artifact.bin",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 5
            });

            Assert.That(result.Success, Is.True);
            Assert.That(handler.HeadRequestCount, Is.EqualTo(1));
            Assert.That(handler.GetRequestCount, Is.EqualTo(3));
        }
        finally
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
    }

    [Test]
    public void AcquireArtifact_InspectionHelper_UsesFailClosedCatchAll()
    {
        var repoRoot = FindRepoRoot();
        var sourcePath = Path.Combine(repoRoot, "apps", "agent", "backend", "Steps", "AcquireArtifact.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.That(
            Regex.IsMatch(source, @"catch\s*\{\s*return true;\s*\}"),
            Is.True,
            "Catch-all inspection failure handling should be fail-closed.");
    }

    [Test]
    public async Task AcquireArtifact_RejectsDestinationWhenAncestorDirectoryIsSymlink()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);

        var rootPath = Path.Combine(Path.GetTempPath(), $"artifact-root-{Guid.NewGuid():N}");
        var externalTargetPath = Path.Combine(Path.GetTempPath(), $"artifact-target-{Guid.NewGuid():N}");
        var symlinkPath = Path.Combine(rootPath, "link");
        var destinationPath = Path.Combine(symlinkPath, "nested", "artifact.bin");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(externalTargetPath);

        try
        {
            if (!TryCreateDirectorySymlink(symlinkPath, externalTargetPath))
            {
                Assert.Ignore("Directory symlinks are not available in this environment.");
                return;
            }

            var acquire = new AcquireArtifact(http, new AcquireArtifactOptions
            {
                ArtifactRootPath = rootPath
            });

            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "https://unit.test/artifact.bin",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 4
            });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("invalid_destination_path"));
            Assert.That(handler.HeadRequestCount, Is.EqualTo(0));
            Assert.That(handler.GetRequestCount, Is.EqualTo(0));
            Assert.That(File.Exists(destinationPath), Is.False);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }

            if (Directory.Exists(externalTargetPath))
            {
                Directory.Delete(externalTargetPath, recursive: true);
            }
        }
    }

    [Test]
    public async Task AcquireArtifact_FailsHashValidation_AndDeletesPartialFile()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
        {
            ArtifactUrl = "https://unit.test/artifact.bin",
            DestinationPath = destinationPath,
            ChunkSizeBytes = 5,
            ExpectedSha256 = new string('0', 64)
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("hash_mismatch"));
        Assert.That(File.Exists(destinationPath), Is.False);
    }

    [Test]
    public async Task AcquireArtifact_FailsWhenPartialContentRangeIsMalformed()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true, malformedPartialContentRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
        {
            ArtifactUrl = "https://unit.test/artifact.bin",
            DestinationPath = destinationPath,
            ChunkSizeBytes = 5
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("invalid_partial_content_range"));
        Assert.That(File.Exists(destinationPath), Is.False);
    }

    [Test]
    public async Task AcquireArtifact_FailsWhenPartialContentBodyIsTruncated()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true, truncatedPartialContentBody: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid():N}.bin");

        var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
        {
            ArtifactUrl = "https://unit.test/artifact.bin",
            DestinationPath = destinationPath,
            ChunkSizeBytes = 5
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("invalid_partial_content_length"));
        Assert.That(File.Exists(destinationPath), Is.False);
    }

    [Test]
    public async Task AcquireArtifact_FailsWithStructuredError_WhenDestinationPathIsNotWritable()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"artifact-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destinationPath);

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "https://unit.test/artifact.bin",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 5
            });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("filesystem_access_denied"));
        }
        finally
        {
            if (Directory.Exists(destinationPath))
            {
                Directory.Delete(destinationPath);
            }
        }
    }

    [Test]
    public async Task AcquireArtifact_RejectsNullRequest()
    {
        var payload = Encoding.UTF8.GetBytes("Hello World!");
        var handler = new StubArtifactHandler(payload, supportsRange: true);
        using var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);

        var result = await acquire.ExecuteAsync(request: null!);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("invalid_request"));
        Assert.That(handler.HeadRequestCount, Is.EqualTo(0));
        Assert.That(handler.GetRequestCount, Is.EqualTo(0));
    }

    private sealed class StubArtifactHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly bool _supportsRange;
        private readonly bool _malformedPartialContentRange;
        private readonly bool _truncatedPartialContentBody;

        public StubArtifactHandler(
            byte[] payload,
            bool supportsRange,
            bool malformedPartialContentRange = false,
            bool truncatedPartialContentBody = false)
        {
            _payload = payload;
            _supportsRange = supportsRange;
            _malformedPartialContentRange = malformedPartialContentRange;
            _truncatedPartialContentBody = truncatedPartialContentBody;
        }

        public int HeadRequestCount { get; private set; }

        public int GetRequestCount { get; private set; }

        public List<string> RangeRequests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                HeadRequestCount++;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };

                response.Content.Headers.ContentLength = _payload.Length;
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                GetRequestCount++;

                if (request.Headers.Range is not null)
                {
                    RangeRequests.Add(FormatRange(request.Headers.Range));
                }

                if (!_supportsRange)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_payload)
                    });
                }

                var range = request.Headers.Range?.Ranges.SingleOrDefault();
                if (range is null || range.From is null || range.To is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
                }

                var from = (int)range.From.Value;
                var to = (int)range.To.Value;
                var count = to - from + 1;
                var chunk = _payload.Skip(from).Take(count).ToArray();
                if (_truncatedPartialContentBody && chunk.Length > 0)
                {
                    chunk = chunk[..^1];
                }

                var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(chunk)
                };
                if (_malformedPartialContentRange)
                {
                    partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from + 1, to, _payload.Length);
                }
                else
                {
                    partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _payload.Length);
                }

                return Task.FromResult(partial);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        }

        private static string FormatRange(RangeHeaderValue range)
        {
            var item = range.Ranges.Single();
            return $"bytes={item.From}-{item.To}";
        }
    }

    private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "DeploymentPoC.sln");
            if (File.Exists(solutionPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
