using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
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
    public void AcquireArtifactRequest_DefaultChunkSize_Is2MB()
    {
        var request = new AcquireArtifactRequest();
        Assert.Equal(2 * 1024 * 1024, request.ChunkSizeBytes);
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
}
