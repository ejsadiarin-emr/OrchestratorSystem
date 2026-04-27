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

public sealed class DelayingPipelineExecutor : PipelineExecutor
{
    private readonly TimeSpan _delay;
    private readonly TaskCompletionSource _startedTcs = new();

    public DelayingPipelineExecutor(TimeSpan delay)
        : base(new FakeHttpClientFactory(), new FakeLogger<PipelineExecutor>())
    {
        _delay = delay;
    }

    public Task Started => _startedTcs.Task;

    public override async Task<PipelineResult> ExecuteAsync(
        PipelineContext context,
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        CancellationToken ct = default)
    {
        _startedTcs.TrySetResult();
        // Deliberately ignore ct so the task continues after stopping token cancellation
        await Task.Delay(_delay, CancellationToken.None);
        return new PipelineResult
        {
            Success = true,
            StepsExecuted = 1
        };
    }
}

public sealed class CancellablePipelineExecutor : PipelineExecutor
{
    private readonly TaskCompletionSource _startedTcs = new();
    private readonly TaskCompletionSource _cancelledTcs = new();

    public CancellablePipelineExecutor()
        : base(new FakeHttpClientFactory(), new FakeLogger<PipelineExecutor>())
    {
    }

    public Task Started => _startedTcs.Task;
    public Task Cancelled => _cancelledTcs.Task;

    public override async Task<PipelineResult> ExecuteAsync(
        PipelineContext context,
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        CancellationToken ct = default)
    {
        _startedTcs.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            _cancelledTcs.TrySetResult();
            throw;
        }
        return new PipelineResult { Success = true, StepsExecuted = 1 };
    }
}
