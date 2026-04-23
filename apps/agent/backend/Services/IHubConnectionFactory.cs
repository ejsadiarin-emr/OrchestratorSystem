namespace DeploymentPoC.Agent.Services;

public interface IHubConnectionFactory
{
    IHubConnection Create(string hubUrl);
}
