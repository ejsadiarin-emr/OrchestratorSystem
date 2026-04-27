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

        var targetPackages = context.Payload.Packages
            .OrderBy(p => p.PackageIndex)
            .ToList();

        var baseDiff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages);
        Dictionary<string, PreCheckResult>? preCheckResults = null;

        // Phase 0: Pre-Check
        if (!context.ForceInstall)
        {
            preCheckResults = new Dictionary<string, PreCheckResult>();

            var packagesToProbe = targetPackages
                .Concat(baseDiff.Removed)
                .GroupBy(p => p.Name)
                .Select(g => g.First())
                .ToList();

            foreach (var package in packagesToProbe)
            {
                var stepCt = CancellationTokenSource.CreateLinkedTokenSource(ct).Token;

                _logger.LogInformation(
                    "Step PreCheckProbe: PackageIndex={PackageIndex}, PackageId={PackageId}",
                    package.PackageIndex,
                    package.PackageId);

                var probeResult = await PreCheckProbe.ExecuteAsync(package.Detection, stepCt);
                preCheckResults[package.Name] = probeResult;

                await SendStepStatusAsync(sendMessageAsync, context, "PreCheckProbe", package, true, probeResult.Error);
                context.RecordStep("PreCheckProbe", package.PackageIndex, package.PackageId, true, probeResult.Error);
            }
        }

        var diff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, preCheckResults);
        var added = diff.Added;
        var removed = diff.Removed;
        var changed = diff.Changed;
        var unchanged = diff.Unchanged;

        _logger.LogInformation(
            "Pipeline diff computed: Added={Added}, Removed={Removed}, Changed={Changed}, Unchanged={Unchanged}",
            added.Count,
            removed.Count,
            changed.Count,
            unchanged.Count);

        _logger.LogInformation(
            "Pipeline starting: RunId={RunId}, Workload={WorkloadName}, Mode={Mode}, TargetPackages={PackageCount}",
            context.RunId,
            context.Payload.WorkloadName,
            context.Payload.Mode,
            targetPackages.Count);

        // Phase 1: Uninstall removed packages in reverse PackageIndex order
        foreach (var package in removed.OrderByDescending(p => p.PackageIndex))
        {
            var stepCt = CancellationTokenSource.CreateLinkedTokenSource(ct).Token;

            if (preCheckResults?.TryGetValue(package.Name, out var preCheck) == true && preCheck.Status == PreCheckStatus.NotPresent)
            {
                _logger.LogInformation(
                    "Step UninstallSkippedAlreadyGone: PackageIndex={PackageIndex}, PackageId={PackageId}",
                    package.PackageIndex,
                    package.PackageId);

                await SendStepStatusAsync(sendMessageAsync, context, "UninstallSkippedAlreadyGone", package, true, null);
                context.RecordStep("UninstallSkippedAlreadyGone", package.PackageIndex, package.PackageId, true, null);
                continue;
            }

            _logger.LogInformation(
                "Step UninstallPackage: PackageIndex={PackageIndex}, PackageId={PackageId}",
                package.PackageIndex,
                package.PackageId);

            var uninstallResult = await UninstallPackage.ExecuteAsync(package.InstallAdapter, stepCt);
            await SendStepStatusAsync(sendMessageAsync, context, "UninstallPackage", package, uninstallResult.Success, uninstallResult.Error);
            context.RecordStep("UninstallPackage", package.PackageIndex, package.PackageId, uninstallResult.Success, uninstallResult.Error);

            if (!uninstallResult.Success)
            {
                _logger.LogError(
                    "Pipeline halted at UninstallPackage: PackageIndex={PackageIndex}, Error={Error}",
                    package.PackageIndex,
                    uninstallResult.Error);
                return await FinalizeAsync(sendMessageAsync, context, ct);
            }
        }

        // Phase 2: Install added and changed packages in normal PackageIndex order
        var packagesToInstall = added.Concat(changed)
            .OrderBy(p => p.PackageIndex)
            .ToList();

        foreach (var package in packagesToInstall)
        {
            var stepCt = CancellationTokenSource.CreateLinkedTokenSource(ct).Token;

            if (preCheckResults?.TryGetValue(package.Name, out var preCheck) == true && preCheck.Status == PreCheckStatus.AlreadySatisfied)
            {
                _logger.LogInformation(
                    "Step PreCheckSkipped: PackageIndex={PackageIndex}, PackageId={PackageId}",
                    package.PackageIndex,
                    package.PackageId);

                await SendStepStatusAsync(sendMessageAsync, context, "PreCheckSkipped", package, true, null);
                context.RecordStep("PreCheckSkipped", package.PackageIndex, package.PackageId, true, null);
                continue;
            }

            // Step 1: Acquire artifact
            var artifactUrl = !string.IsNullOrEmpty(package.DownloadUrl)
                ? $"{context.OrchestratorBaseUrl.TrimEnd('/')}{package.DownloadUrl}"
                : $"{context.OrchestratorBaseUrl.TrimEnd('/')}/api/artifacts/{package.Name}/{package.Version}";
            var destFileName = !string.IsNullOrEmpty(package.ArtifactFileName)
                ? $"{package.PackageId}-{package.Version}-{package.ArtifactFileName}"
                : $"{package.PackageId}-{package.Version}";
            var destinationPath = Path.Combine(Path.GetTempPath(), "agent-artifacts", context.RunId, destFileName);

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

            var installResult = await InstallOrUpgrade.ExecuteAsync(package.InstallAdapter, destinationPath, _logger, stepCt);
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
