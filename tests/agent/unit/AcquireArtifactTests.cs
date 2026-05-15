using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeploymentPoC.Agent.Steps;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class AcquireArtifactTests
{
    private static HttpClient CreateMockHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new TestHandler(handler));
    }

    private class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _handler(request, cancellationToken);
    }

    private class MismatchedHttpContent : HttpContent
    {
        private readonly byte[] _actualData;
        private readonly long _declaredLength;

        public MismatchedHttpContent(byte[] actualData, long declaredLength)
        {
            _actualData = actualData;
            _declaredLength = declaredLength;
            Headers.ContentLength = declaredLength;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_actualData, 0, _actualData.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = _declaredLength;
            return true;
        }
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

    [Test]
    public void IsValidContentRange_StarTotalLength_ReturnsTrue()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999)
        {
            Unit = "bytes"
        };

        Assert.That(AcquireArtifact.IsValidContentRange(contentRange, 0, 999_999, 2_000_000), Is.True);
    }

    [Test]
    public void IsValidContentRange_ExplicitMatchingLength_ReturnsTrue()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999, 2_000_000)
        {
            Unit = "bytes"
        };

        Assert.That(AcquireArtifact.IsValidContentRange(contentRange, 0, 999_999, 2_000_000), Is.True);
    }

    [Test]
    public void IsValidContentRange_ExplicitMismatchedLength_ReturnsFalse()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999, 2_000_000)
        {
            Unit = "bytes"
        };

        Assert.That(AcquireArtifact.IsValidContentRange(contentRange, 0, 999_999, 1_500_000), Is.False);
    }

    [Test]
    public async Task DownloadFullAsync_ContentLengthMismatch_ThrowsInvalidOperationException()
    {
        var handler = new TestHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new MismatchedHttpContent(new byte[50], 100)
            };
            return Task.FromResult(response);
        });

        var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);
        var uri = new Uri("http://example.com/artifact.zip");

        await using var output = new MemoryStream();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => acquire.DownloadFullAsync(uri, output, CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("Full download size mismatch"));
    }

    [Test]
    public async Task ExecuteAsync_ChunkedDownload_200OkFallbackWithLengthMismatch_ReturnsError()
    {
        var handler = new TestHandler((request, ct) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = 100;
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new MismatchedHttpContent(new byte[50], 100)
                };
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-test-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "http://example.com/artifact.zip",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 1024
            }, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("content_length_mismatch on 200 OK"));
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Test]
    public void AcquireArtifactRequest_DefaultChunkSize_Is8MB()
    {
        var request = new AcquireArtifactRequest();
        Assert.That(request.ChunkSizeBytes, Is.EqualTo(8 * 1024 * 1024));
    }

    [Test]
    public void IsValidContentRange_StarWithValidRange_ReturnsTrue()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999)
        {
            Unit = "bytes"
        };

        Assert.That(AcquireArtifact.IsValidContentRange(contentRange, 0, 999_999, 2_000_000), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_ChunkedDownload_RetryOnTransientFailure_Succeeds()
    {
        var chunkAttemptCount = 0;
        var handler = new TestHandler((request, ct) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = 100;
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                chunkAttemptCount++;
                if (chunkAttemptCount <= 2)
                {
                    throw new HttpRequestException("Simulated failure");
                }

                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                var data = new byte[100];
                new Random(42).NextBytes(data);
                response.Content = new ByteArrayContent(data);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 99, 100);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-test-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "http://example.com/artifact.zip",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 1024
            }, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(100));
            Assert.That(chunkAttemptCount, Is.EqualTo(3));
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Test]
    public async Task ExecuteAsync_ChunkedDownload_RetryExhausted_ReturnsError()
    {
        var handler = new TestHandler((request, ct) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = 100;
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                throw new HttpRequestException("Simulated failure");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-test-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "http://example.com/artifact.zip",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 1024
            }, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("chunk_download_failed_after_retries"));
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Test]
    public async Task ExecuteAsync_ChunkedDownload_TimeoutDuringChunk_ReturnsDownloadTimeout()
    {
        var handler = new TestHandler((request, ct) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = 100;
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                return DelayedChunkResponseAsync(ct);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            static async Task<HttpResponseMessage> DelayedChunkResponseAsync(CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                var data = new byte[100];
                new Random(42).NextBytes(data);
                response.Content = new ByteArrayContent(data);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 99, 100);
                return response;
            }
        });

        var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-test-{Guid.NewGuid():N}.zip");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "http://example.com/artifact.zip",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 1024,
                DownloadTimeoutSeconds = 1
            }, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("download_timeout"));
        }
        finally
        {
            stopwatch.Stop();
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1.5)),
            $"Expected quick timeout without retries, but elapsed was {stopwatch.Elapsed}");
    }

    [Test]
    public async Task ExecuteAsync_ChunkedDownload_RetriesWithLongTimeout_Succeeds()
    {
        var chunkAttemptCount = 0;
        var handler = new TestHandler((request, ct) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = 100;
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                chunkAttemptCount++;
                if (chunkAttemptCount <= 2)
                {
                    throw new HttpRequestException("Simulated failure");
                }

                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                var data = new byte[100];
                new Random(42).NextBytes(data);
                response.Content = new ByteArrayContent(data);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 99, 100);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-test-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "http://example.com/artifact.zip",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 1024,
                DownloadTimeoutSeconds = 10
            }, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(100));
            Assert.That(chunkAttemptCount, Is.EqualTo(3));
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulChunkedDownload_WithSha256_Succeeds()
    {
        var data = new byte[100];
        new Random(42).NextBytes(data);
        using var sha = SHA256.Create();
        var expectedHash = Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();

        var handler = new TestHandler((request, ct) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = data.Length;
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                response.Content = new ByteArrayContent(data);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, data.Length - 1, data.Length);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var http = new HttpClient(handler);
        var acquire = new AcquireArtifact(http);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-test-{Guid.NewGuid():N}.zip");

        try
        {
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "http://example.com/artifact.zip",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 1024,
                DownloadTimeoutSeconds = 1,
                ExpectedSha256 = expectedHash
            }, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(data.Length));
            Assert.That(File.Exists(destinationPath), Is.True);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Test]
    public async Task FinalizeResultAsync_WithMatchingHash_ReturnsSuccess()
    {
        var data = new byte[100];
        new Random(42).NextBytes(data);
        using var sha = SHA256.Create();
        var expectedHash = Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();

        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-finalize-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(destinationPath, data);

        try
        {
            var request = new AcquireArtifactRequest
            {
                ExpectedSha256 = expectedHash
            };

            var result = await AcquireArtifact.FinalizeResultAsync(request, destinationPath, data.Length, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(data.Length));
            Assert.That(result.Transport, Is.EqualTo("http"));
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Test]
    public async Task FinalizeResultAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var data = new byte[10];
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-finalize-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(destinationPath, data);

        try
        {
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();

            var request = new AcquireArtifactRequest
            {
                ExpectedSha256 = hash
            };

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var ex = Assert.ThrowsAsync<TaskCanceledException>(() =>
                AcquireArtifact.FinalizeResultAsync(request, destinationPath, data.Length, cts.Token));
            Assert.That(ex, Is.Not.Null);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithRangeCapableServer_DownloadsInChunks()
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
    public async Task ExecuteAsync_WhenRangeNotSupported_FallsBackToFullDownload()
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
    public async Task ExecuteAsync_WithDestinationOutsideRoot_BlocksWithError()
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
    public async Task ExecuteAsync_WithInvalidUrl_RejectsWithError()
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
    public async Task ExecuteAsync_WithHostNotInAllowlist_RejectsWithError()
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
    public async Task ExecuteAsync_WithHostInAllowlist_AllowsDownload()
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
    public async Task ExecuteAsync_WithWhitespaceOnlyAllowlist_AllowsDownload()
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
    public void SourceCode_InspectionHelper_UsesFailClosedCatchAll()
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
    public async Task ExecuteAsync_WithSymlinkAncestorPath_BlocksWithError()
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
    public async Task ExecuteAsync_WithHashMismatch_FailsAndDeletesFile()
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
    public async Task ExecuteAsync_WithMalformedContentRange_ReturnsError()
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
    public async Task ExecuteAsync_WithTruncatedContentBody_ReturnsError()
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
    public async Task ExecuteAsync_WithNonWritableDestination_ReturnsFilesystemError()
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
    public async Task ExecuteAsync_WithNullRequest_ReturnsInvalidRequestError()
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
