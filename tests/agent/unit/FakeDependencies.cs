using System.Net;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Agent.Tests;

public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient();
    }
}

public sealed class FakeLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }
}

public sealed class CapturingHttpHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;

    public CapturingHttpHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_respond(request, cancellationToken));
    }
}
