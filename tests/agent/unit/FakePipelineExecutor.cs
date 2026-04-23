using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime;

namespace DeploymentPoC.Agent.Tests;

public sealed class FakePipelineExecutor : PipelineExecutor
{
    public FakePipelineExecutor()
        : base(new FakeHttpClientFactory(), new FakeLogger<PipelineExecutor>())
    {
    }

    public override Task<PipelineResult> ExecuteAsync(
        PipelineContext context,
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        CancellationToken ct = default)
    {
        return Task.FromResult(new PipelineResult
        {
            Success = true,
            StepsExecuted = 0
        });
    }
}
