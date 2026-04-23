using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Agent.Pipeline;

public class PipelineExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PipelineExecutor> _logger;

    public PipelineExecutor(IHttpClientFactory httpClientFactory, ILogger<PipelineExecutor> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public virtual async Task<PipelineResult> ExecuteAsync(
        PipelineContext context,
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        CancellationToken ct = default)
    {
        var http = _httpClientFactory.CreateClient();
        var acquire = new AcquireArtifact(http);

        var packages = context.Payload.Packages
            .OrderBy(p => p.PackageIndex)
            .ToList();

        _logger.LogInformation(
            "Pipeline starting: RunId={RunId}, Workload={WorkloadName}, Mode={Mode}, Packages={PackageCount}",
            context.RunId,
            context.Payload.WorkloadName,
            context.Payload.Mode,
            packages.Count);

        foreach (var package in packages)
        {
            var stepCt = CancellationTokenSource.CreateLinkedTokenSource(ct).Token;

            // Step 1: Acquire artifact
            var artifactUrl = $"{context.OrchestratorBaseUrl.TrimEnd('/')}/api/artifacts/{package.PackageId}/{package.Version}";
            var destinationPath = Path.Combine(Path.GetTempPath(), $"agent-artifacts", context.RunId, $"{package.PackageId}-{package.Version}");

            _logger.LogInformation(
                "Step AcquireArtifact: PackageIndex={PackageIndex}, PackageId={PackageId}, Url={ArtifactUrl}",
                package.PackageIndex,
                package.PackageId,
                artifactUrl);

            var acquireResult = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = artifactUrl,
                DestinationPath = destinationPath,
                ChunkSizeBytes = 8 * 1024 * 1024
            }, stepCt);

            await SendStepStatusAsync(sendMessageAsync, context, "AcquireArtifact", package, acquireResult.Success, acquireResult.Error);
            context.RecordStep("AcquireArtifact", package.PackageIndex, package.PackageId, acquireResult.Success, acquireResult.Error);

            if (!acquireResult.Success)
            {
                _logger.LogError(
                    "Pipeline halted at AcquireArtifact: PackageIndex={PackageIndex}, Error={Error}",
                    package.PackageIndex,
                    acquireResult.Error);
                return await FinalizeAsync(sendMessageAsync, context, ct);
            }

            // Step 2: Install or upgrade
            _logger.LogInformation(
                "Step InstallOrUpgrade: PackageIndex={PackageIndex}, PackageId={PackageId}, AdapterType={AdapterType}",
                package.PackageIndex,
                package.PackageId,
                package.InstallAdapter.Type);

            var installResult = await InstallOrUpgrade.ExecuteAsync(package.InstallAdapter, destinationPath, stepCt);
            await SendStepStatusAsync(sendMessageAsync, context, "InstallOrUpgrade", package, installResult.Success, installResult.Error);
            context.RecordStep("InstallOrUpgrade", package.PackageIndex, package.PackageId, installResult.Success, installResult.Error);

            if (!installResult.Success)
            {
                _logger.LogError(
                    "Pipeline halted at InstallOrUpgrade: PackageIndex={PackageIndex}, Error={Error}",
                    package.PackageIndex,
                    installResult.Error);
                return await FinalizeAsync(sendMessageAsync, context, ct);
            }

            // Step 3: Post-install verification
            _logger.LogInformation(
                "Step PostInstallVerify: PackageIndex={PackageIndex}, PackageId={PackageId}, DetectionType={DetectionType}",
                package.PackageIndex,
                package.PackageId,
                package.Detection.Type);

            var verifyResult = await PostInstallVerify.ExecuteAsync(package.Detection, stepCt);
            await SendStepStatusAsync(sendMessageAsync, context, "PostInstallVerify", package, verifyResult.Success, verifyResult.Error);
            context.RecordStep("PostInstallVerify", package.PackageIndex, package.PackageId, verifyResult.Success, verifyResult.Error);

            if (!verifyResult.Success)
            {
                _logger.LogError(
                    "Pipeline halted at PostInstallVerify: PackageIndex={PackageIndex}, Error={Error}",
                    package.PackageIndex,
                    verifyResult.Error);
                return await FinalizeAsync(sendMessageAsync, context, ct);
            }
        }

        return await FinalizeAsync(sendMessageAsync, context, ct);
    }

    private static async Task SendStepStatusAsync(
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        PipelineContext context,
        string stepName,
        PackageAssignment package,
        bool success,
        string? error)
    {
        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.StepStatus,
            RunId = context.RunId,
            AgentId = context.AgentId,
            Sequence = context.Sequence,
            Payload = new StepStatusPayload
            {
                StepName = stepName,
                PackageIndex = package.PackageIndex,
                PackageId = package.PackageId,
                Status = success ? "success" : "failure",
                Error = error
            }
        };

        try
        {
            await sendMessageAsync(envelope, CancellationToken.None);
        }
        catch (Exception)
        {
            // Best-effort: don't let send failures break the pipeline
        }
    }

    private static async Task<PipelineResult> FinalizeAsync(
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        PipelineContext context,
        CancellationToken ct)
    {
        var allSucceeded = context.AllStepsSucceeded && context.StepHistory.Count > 0;
        var error = context.FirstError;

        var envelope = new MessageEnvelope
        {
            MessageType = allSucceeded ? MessageTypes.Complete : MessageTypes.Fail,
            RunId = context.RunId,
            AgentId = context.AgentId,
            Sequence = context.Sequence,
            Payload = new FinalizationPayload
            {
                Result = allSucceeded ? "success" : "failure",
                Error = error,
                StepCount = context.StepHistory.Count
            }
        };

        try
        {
            await sendMessageAsync(envelope, ct);
        }
        catch (Exception)
        {
            // Best-effort
        }

        return new PipelineResult
        {
            Success = allSucceeded,
            Error = error,
            StepsExecuted = context.StepHistory.Count
        };
    }
}

public sealed class PipelineResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int StepsExecuted { get; set; }
}

public sealed class StepStatusPayload
{
    public string StepName { get; set; } = string.Empty;
    public int PackageIndex { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class FinalizationPayload
{
    public string Result { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int StepCount { get; set; }
}
