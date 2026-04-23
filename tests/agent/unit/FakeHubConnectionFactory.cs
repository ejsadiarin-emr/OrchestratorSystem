using DeploymentPoC.Agent.Services;

namespace DeploymentPoC.Agent.Tests;

public sealed class FakeHubConnectionFactory : IHubConnectionFactory
{
    private readonly IHubConnection _connection;

    public FakeHubConnectionFactory(IHubConnection connection)
    {
        _connection = connection;
    }

    public IHubConnection Create(string hubUrl)
    {
        return _connection;
    }
}
