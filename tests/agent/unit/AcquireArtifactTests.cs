using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using DeploymentPoC.Agent.Steps;
using Xunit;

namespace DeploymentPoC.Agent.Tests;

public sealed class AcquireArtifactTests
{
    private static bool InvokeIsValidContentRange(ContentRangeHeaderValue? contentRange, long from, long to, long expectedLength)
    {
        var method = typeof(AcquireArtifact).GetMethod("IsValidContentRange", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object?[] { contentRange, from, to, expectedLength })!;
    }

    private static async Task<long> InvokeDownloadFullAsync(AcquireArtifact instance, Uri uri, Stream output, CancellationToken ct)
    {
        var method = typeof(AcquireArtifact).GetMethod("DownloadFullAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return await (Task<long>)method!.Invoke(instance, new object[] { uri, output, ct })!;
    }

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

    [Fact]
    public void IsValidContentRange_StarTotalLength_ReturnsTrue()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999)
        {
            Unit = "bytes"
        };

        Assert.True(InvokeIsValidContentRange(contentRange, 0, 999_999, 2_000_000));
    }

    [Fact]
    public void IsValidContentRange_ExplicitMatchingLength_ReturnsTrue()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999, 2_000_000)
        {
            Unit = "bytes"
        };

        Assert.True(InvokeIsValidContentRange(contentRange, 0, 999_999, 2_000_000));
    }

    [Fact]
    public void IsValidContentRange_ExplicitMismatchedLength_ReturnsFalse()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999, 2_000_000)
        {
            Unit = "bytes"
        };

        Assert.False(InvokeIsValidContentRange(contentRange, 0, 999_999, 1_500_000));
    }

    [Fact]
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeDownloadFullAsync(acquire, uri, output, CancellationToken.None));
        Assert.Contains("Full download size mismatch", ex.Message);
    }

    [Fact]
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

            Assert.False(result.Success);
            Assert.Contains("content_length_mismatch on 200 OK", result.Error);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Fact]
    public void AcquireArtifactRequest_DefaultChunkSize_Is8MB()
    {
        var request = new AcquireArtifactRequest();
        Assert.Equal(8 * 1024 * 1024, request.ChunkSizeBytes);
    }

    [Fact]
    public void IsValidContentRange_StarWithValidRange_ReturnsTrue()
    {
        var contentRange = new ContentRangeHeaderValue(0, 999_999)
        {
            Unit = "bytes"
        };

        Assert.True(InvokeIsValidContentRange(contentRange, 0, 999_999, 2_000_000));
    }

    [Fact]
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

            Assert.True(result.Success);
            Assert.Equal(100, result.BytesWritten);
            Assert.Equal(3, chunkAttemptCount);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Fact]
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

            Assert.False(result.Success);
            Assert.Contains("chunk_download_failed_after_retries", result.Error);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Fact]
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
                // Delay longer than the 1-second download timeout so cancellation fires.
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                // This line should not be reached because the token is cancelled first.
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

            Assert.False(result.Success);
            Assert.Equal("download_timeout", result.Error);
        }
        finally
        {
            stopwatch.Stop();
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }

        // If retries occurred, attempt 0 would delay 1s, attempt 1 would delay 2s, etc.
        // The timeout fires at 1s, so total elapsed must be well under 2s if no retries ran.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1.5),
            $"Expected quick timeout without retries, but elapsed was {stopwatch.Elapsed}");
    }

    [Fact]
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
                DownloadTimeoutSeconds = 10 // Long timeout — must not fire before retries succeed
            }, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(100, result.BytesWritten);
            Assert.Equal(3, chunkAttemptCount);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Fact]
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
            // Short timeout: if FinalizeResultAsync still used the download-scoped token,
            // the timer could fire during SHA-256 verification and fail the download.
            var result = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = "http://example.com/artifact.zip",
                DestinationPath = destinationPath,
                ChunkSizeBytes = 1024,
                DownloadTimeoutSeconds = 1,
                ExpectedSha256 = expectedHash
            }, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(data.Length, result.BytesWritten);
            Assert.True(File.Exists(destinationPath));
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Fact]
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
            var method = typeof(AcquireArtifact).GetMethod("FinalizeResultAsync", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var request = new AcquireArtifactRequest
            {
                ExpectedSha256 = expectedHash
            };

            var result = await (Task<AcquireArtifactResult>)method!.Invoke(null, new object[] { request, destinationPath, data.Length, CancellationToken.None })!;

            Assert.True(result.Success);
            Assert.Equal(data.Length, result.BytesWritten);
            Assert.Equal("http", result.Transport);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }

    [Fact]
    public async Task FinalizeResultAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var data = new byte[10];
        var destinationPath = Path.Combine(Path.GetTempPath(), $"acquire-finalize-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(destinationPath, data);

        try
        {
            var method = typeof(AcquireArtifact).GetMethod("FinalizeResultAsync", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();

            var request = new AcquireArtifactRequest
            {
                ExpectedSha256 = hash
            };

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var ex = await Assert.ThrowsAsync<TaskCanceledException>(() =>
                (Task<AcquireArtifactResult>)method!.Invoke(null, new object[] { request, destinationPath, data.Length, cts.Token })!);
            Assert.NotNull(ex);
        }
        finally
        {
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
        }
    }
}
