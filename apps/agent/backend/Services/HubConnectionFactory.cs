using Microsoft.AspNetCore.SignalR.Client;

namespace DeploymentPoC.Agent.Services;

public sealed class HubConnectionFactory : IHubConnectionFactory
{
    public IHubConnection Create(string hubUrl)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult("placeholder-token")!;
            })
            .WithAutomaticReconnect()
            .Build();

        return new HubConnectionWrapper(connection);
    }
}
