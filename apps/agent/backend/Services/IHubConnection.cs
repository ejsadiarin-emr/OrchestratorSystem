using Microsoft.AspNetCore.SignalR.Client;

namespace DeploymentPoC.Agent.Services;

public interface IHubConnection
{
    HubConnectionState State { get; }

    event Func<Exception?, Task>? Reconnecting;
    event Func<string?, Task>? Reconnected;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task InvokeAsync(string methodName, object? arg1, CancellationToken cancellationToken = default);
    IDisposable On<T>(string methodName, Func<T, Task> handler);
    ValueTask DisposeAsync();
}
