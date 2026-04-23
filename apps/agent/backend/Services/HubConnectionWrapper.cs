using Microsoft.AspNetCore.SignalR.Client;

namespace DeploymentPoC.Agent.Services;

public sealed class HubConnectionWrapper : IHubConnection
{
    private readonly HubConnection _connection;

    public HubConnectionWrapper(HubConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public HubConnectionState State => _connection.State;

    public event Func<Exception?, Task>? Reconnecting
    {
        add => _connection.Reconnecting += value;
        remove => _connection.Reconnecting -= value;
    }

    public event Func<string?, Task>? Reconnected
    {
        add => _connection.Reconnected += value;
        remove => _connection.Reconnected -= value;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _connection.StartAsync(cancellationToken);

    public Task InvokeAsync(string methodName, object? arg1, CancellationToken cancellationToken = default)
        => _connection.InvokeAsync(methodName, arg1, cancellationToken);

    public IDisposable On<T>(string methodName, Func<T, Task> handler)
        => _connection.On(methodName, handler);

    public ValueTask DisposeAsync()
        => _connection.DisposeAsync();
}
