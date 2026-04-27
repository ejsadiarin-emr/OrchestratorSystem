using DeploymentPoC.Agent.Services;
using DeploymentPoC.Contracts.Runtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace DeploymentPoC.Agent.Tests;

public sealed class FakeHubConnection : IHubConnection
{
    public HubConnectionState State { get; set; } = HubConnectionState.Connected;

    public event Func<Exception?, Task>? Reconnecting;
    public event Func<string?, Task>? Reconnected;
    public event Func<Exception?, Task>? Closed;

    public List<(string Method, object? Arg)> Invocations { get; } = new();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task InvokeAsync(string methodName, object? arg1, CancellationToken cancellationToken = default)
    {
        Invocations.Add((methodName, arg1));
        return Task.CompletedTask;
    }

    public IDisposable On<T>(string methodName, Func<T, Task> handler)
    {
        return new NullDisposable();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async Task RaiseReconnectingAsync(Exception? ex)
    {
        if (Reconnecting is not null)
        {
            await Reconnecting.Invoke(ex);
        }
    }

    public async Task RaiseReconnectedAsync(string? connectionId)
    {
        if (Reconnected is not null)
        {
            await Reconnected.Invoke(connectionId);
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
