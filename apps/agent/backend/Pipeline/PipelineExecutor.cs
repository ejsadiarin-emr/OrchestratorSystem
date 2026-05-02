using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Agent.Pipeline;

public class PipelineExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PipelineExecutor> _logger;
    private readonly IConfiguration _configuration;

    public PipelineExecutor(IHttpClientFactory httpClientFactory, ILogger<PipelineExecutor> logger, IConfiguration? configuration = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new ConfigurationBuilder().Build();
    }

    public virtual async Task<PipelineResult> ExecuteAsync(
        PipelineContext context,
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        CancellationToken ct = default)
    {
        var http = _httpClientFactory.CreateClient();
        var acquire = new AcquireArtifact(http, logger: _logger);

        var targetPackages = context.Payload.Packages
            .OrderBy(p => p.PackageIndex)
            .ToList();

        var baseDiff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, null, context.Payload.Mode);
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
                var stepCt = ct;

                _logger.LogInformation(
                    "Step PreCheckProbe: PackageIndex={PackageIndex}, PackageId={PackageId}",
                    package.PackageIndex,
                    package.PackageId);

                var probeResult = await PreCheckProbe.ExecuteAsync(package.Detection, stepCt);
                preCheckResults[package.Name] = probeResult;

                await SendStepStatusAsync(sendMessageAsync, context, "PreCheckProbe", package, true, probeResult.Error, stepCt);
                context.RecordStep("PreCheckProbe", package.PackageIndex, package.PackageId, true, probeResult.Error);
            }
        }

        var diff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, preCheckResults, context.Payload.Mode);
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

        // Phase 0.5: Pre-workload init steps
        var isUninstall = string.Equals(context.Payload.Mode, "uninstall", StringComparison.OrdinalIgnoreCase);
        var defaultShell = string.IsNullOrWhiteSpace(context.Payload.DefaultShell) ? "powershell" : context.Payload.DefaultShell;
        var initStepExecutor = new InitStepExecutor();
        var initStepCb = CreateInitStepStatusCallback(sendMessageAsync, context, ct);

        if (!isUninstall)
        {
            var preWorkloadSteps = context.Payload.PreWorkloadSteps ?? new List<string>();
            for (int i = 0; i < preWorkloadSteps.Count; i++)
            {
                var stepName = $"PreWorkload_{i}";
                var envVars = InitStepEnvVars.Build(context, null, null);
                var result = await initStepExecutor.ExecuteAsync(preWorkloadSteps[i], defaultShell, stepName, envVars, 60, -1, initStepCb, ct);
                context.RecordStep(stepName, -1, "", result.Success, result.ErrorOutput);
                if (!result.Success)
                {
                    _logger.LogError("PreWorkload step {StepName} failed: {Error}", stepName, result.ErrorOutput);
                    return await FinalizeAsync(sendMessageAsync, context, ct);
                }
            }
        }
        else
        {
            var preUninstallSteps = context.Payload.PreUninstallSteps ?? new List<string>();
            for (int i = 0; i < preUninstallSteps.Count; i++)
            {
                var stepName = $"PreUninstall_{i}";
                var envVars = InitStepEnvVars.Build(context, null, null);
                var result = await initStepExecutor.ExecuteAsync(preUninstallSteps[i], defaultShell, stepName, envVars, 60, -1, initStepCb, ct);
                context.RecordStep(stepName, -1, "", result.Success, result.ErrorOutput);
                if (!result.Success)
                {
                    _logger.LogError("PreUninstall step {StepName} failed: {Error}", stepName, result.ErrorOutput);
                    return await FinalizeAsync(sendMessageAsync, context, ct);
                }
            }
        }

        // Phase 1: Uninstall packages
        if (isUninstall)
        {
            // Uninstall all detected packages in reverse PackageIndex order
            var packagesToUninstall = targetPackages
                .OrderByDescending(p => p.PackageIndex)
                .ToList();

            foreach (var package in packagesToUninstall)
            {
                var stepCt = ct;

                if (preCheckResults?.TryGetValue(package.Name, out var preCheck) == true && preCheck.Status == PreCheckStatus.NotPresent)
                {
                    _logger.LogInformation(
                        "Step UninstallSkippedAlreadyGone: PackageIndex={PackageIndex}, PackageId={PackageId}",
                        package.PackageIndex,
                        package.PackageId);

                    await SendStepStatusAsync(sendMessageAsync, context, "UninstallSkippedAlreadyGone", package, true, null, stepCt);
                    context.RecordStep("UninstallSkippedAlreadyGone", package.PackageIndex, package.PackageId, true, null);
                    continue;
                }

                _logger.LogInformation(
                    "Step UninstallPackage: PackageIndex={PackageIndex}, PackageId={PackageId}",
                    package.PackageIndex,
                    package.PackageId);

                string? destinationPath = null;
                var hasUninstallCommand = !string.IsNullOrWhiteSpace(package.InstallAdapter.UninstallCommand);

                if (!hasUninstallCommand)
                {
                    _logger.LogError(
                        "Pipeline halted: UninstallCommand is required for package {PackageName} in uninstall mode",
                        package.Name);
                    await SendStepStatusAsync(sendMessageAsync, context, "UninstallPackage", package, false, "missing_uninstall_command", stepCt);
                    context.RecordStep("UninstallPackage", package.PackageIndex, package.PackageId, false, "missing_uninstall_command");
                    return await FinalizeAsync(sendMessageAsync, context, ct);
                }

                var uninstallResult = await UninstallPackage.ExecuteAsync(package.InstallAdapter, destinationPath, stepCt);
                await SendStepStatusAsync(sendMessageAsync, context, "UninstallPackage", package, uninstallResult.Success, uninstallResult.Error, stepCt);
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
        }
        else
        {
            // Uninstall removed packages AND UninstallFirst changed packages in reverse PackageIndex order
            var currentPackagesByName = context.CurrentPackages.ToDictionary(p => p.Name);
            var packagesToUninstall = removed
                .Concat(changed.Where(p =>
                    string.Equals(p.InstallAdapter.UpgradeBehavior, "UninstallFirst", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.PackageIndex)
                .ToList();

            foreach (var package in packagesToUninstall)
            {
                var stepCt = ct;

                if (preCheckResults?.TryGetValue(package.Name, out var preCheck) == true && preCheck.Status == PreCheckStatus.NotPresent)
                {
                    _logger.LogInformation(
                        "Step UninstallSkippedAlreadyGone: PackageIndex={PackageIndex}, PackageId={PackageId}",
                        package.PackageIndex,
                        package.PackageId);

                    await SendStepStatusAsync(sendMessageAsync, context, "UninstallSkippedAlreadyGone", package, true, null, stepCt);
                    context.RecordStep("UninstallSkippedAlreadyGone", package.PackageIndex, package.PackageId, true, null);
                    continue;
                }

                _logger.LogInformation(
                    "Step UninstallPackage: PackageIndex={PackageIndex}, PackageId={PackageId}",
                    package.PackageIndex,
                    package.PackageId);

                // For changed packages, use the OLD package's InstallAdapter (from CurrentPackages) for uninstall
                var isRemoved = removed.Contains(package);
                var uninstallPackage = isRemoved
                    ? package
                    : (currentPackagesByName.TryGetValue(package.Name, out var oldPkg)
                        ? oldPkg
                        : package);
                var uninstallConfig = uninstallPackage.InstallAdapter;

                string? destinationPath = null;
                var hasUninstallCommand = !string.IsNullOrWhiteSpace(uninstallConfig.UninstallCommand);

                // Only acquire artifact if no dedicated uninstall command is configured
                if (!hasUninstallCommand)
                {
                    var artifactUrl = !string.IsNullOrEmpty(uninstallPackage.DownloadUrl)
                        ? $"{context.OrchestratorBaseUrl.TrimEnd('/')}{uninstallPackage.DownloadUrl}"
                        : $"{context.OrchestratorBaseUrl.TrimEnd('/')}/api/artifacts/{uninstallPackage.PackageId}/download";
                    var destFileName = !string.IsNullOrEmpty(uninstallPackage.ArtifactFileName)
                        ? $"{uninstallPackage.PackageId}-{uninstallPackage.Version}-{uninstallPackage.ArtifactFileName}"
                        : $"{uninstallPackage.PackageId}-{uninstallPackage.Version}";
                    destinationPath = Path.Combine(Path.GetTempPath(), "agent-artifacts", context.RunId, destFileName);

                    _logger.LogInformation(
                        "Step AcquireArtifactForUninstall: PackageIndex={PackageIndex}, PackageId={PackageId}, Url={ArtifactUrl}",
                        uninstallPackage.PackageIndex,
                        uninstallPackage.PackageId,
                        artifactUrl);

                    var chunkSizeBytes = _configuration.GetValue<int?>("Agent:ChunkSizeBytes") ?? 2 * 1024 * 1024;
                    var useChunkedDownload = _configuration.GetValue<bool?>("Agent:UseChunkedDownload") ?? true;

                    var acquireResult = await acquire.ExecuteAsync(new AcquireArtifactRequest
                    {
                        ArtifactUrl = artifactUrl,
                        DestinationPath = destinationPath,
                        ChunkSizeBytes = chunkSizeBytes,
                        UseChunkedDownload = useChunkedDownload,
                        ExpectedSha256 = uninstallPackage.ExpectedSha256
                    }, stepCt);

                    await SendStepStatusAsync(sendMessageAsync, context, "AcquireArtifactForUninstall", uninstallPackage, acquireResult.Success, acquireResult.Error, stepCt);
                    context.RecordStep("AcquireArtifactForUninstall", uninstallPackage.PackageIndex, uninstallPackage.PackageId, acquireResult.Success, acquireResult.Error);

                    if (!acquireResult.Success)
                    {
                        _logger.LogError(
                            "Pipeline halted at AcquireArtifactForUninstall: PackageIndex={PackageIndex}, Error={Error}",
                            uninstallPackage.PackageIndex,
                            acquireResult.Error);
                        return await FinalizeAsync(sendMessageAsync, context, ct);
                    }
                }

                var uninstallResult = await UninstallPackage.ExecuteAsync(uninstallConfig, destinationPath, stepCt);
                await SendStepStatusAsync(sendMessageAsync, context, "UninstallPackage", uninstallPackage, uninstallResult.Success, uninstallResult.Error, stepCt);
                context.RecordStep("UninstallPackage", uninstallPackage.PackageIndex, uninstallPackage.PackageId, uninstallResult.Success, uninstallResult.Error);

                if (!uninstallResult.Success)
                {
                    _logger.LogError(
                        "Pipeline halted at UninstallPackage: PackageIndex={PackageIndex}, Error={Error}",
                        uninstallPackage.PackageIndex,
                        uninstallResult.Error);
                    return await FinalizeAsync(sendMessageAsync, context, ct);
                }
            }
        }

        // Phase 2: Install added and changed packages in normal PackageIndex order
        var packagesToInstall = added.Concat(changed)
            .OrderBy(p => p.PackageIndex)
            .ToList();

        if (!isUninstall)
        {
            foreach (var package in packagesToInstall)
        {
            var stepCt = ct;

            if (preCheckResults?.TryGetValue(package.Name, out var preCheck) == true && preCheck.Status == PreCheckStatus.AlreadySatisfied)
            {
                _logger.LogInformation(
                    "Step PreCheckSkipped: PackageIndex={PackageIndex}, PackageId={PackageId}",
                    package.PackageIndex,
                    package.PackageId);

                await SendStepStatusAsync(sendMessageAsync, context, "PreCheckSkipped", package, true, null, stepCt);
                context.RecordStep("PreCheckSkipped", package.PackageIndex, package.PackageId, true, null);
                continue;
            }

            // Step 0.1: Pre-init steps (per package)
            if (!isUninstall)
            {
                var preInitSteps = package.PreInitSteps ?? new List<string>();
                var preInitFailed = false;
                for (int si = 0; si < preInitSteps.Count; si++)
                {
                    var stepName = $"PreInit_{package.PackageIndex}_{si}";
                    var envVars = InitStepEnvVars.Build(context, package, null);
                    var result = await initStepExecutor.ExecuteAsync(preInitSteps[si], defaultShell, stepName, envVars, 60, package.PackageIndex, initStepCb, ct);
                    context.RecordStep(stepName, package.PackageIndex, package.PackageId, result.Success, result.ErrorOutput);
                    if (!result.Success)
                    {
                        _logger.LogWarning("PreInit step {StepName} failed for package {PackageName}: {Error} — skipping install", stepName, package.Name, result.ErrorOutput);
                        preInitFailed = true;
                        break;
                    }
                }

                if (preInitFailed)
                {
                    continue;
                }
            }

            // Step 1: Acquire artifact
            var artifactUrl = !string.IsNullOrEmpty(package.DownloadUrl)
                ? $"{context.OrchestratorBaseUrl.TrimEnd('/')}{package.DownloadUrl}"
                : $"{context.OrchestratorBaseUrl.TrimEnd('/')}/api/artifacts/{package.PackageId}/download";
            var destFileName = !string.IsNullOrEmpty(package.ArtifactFileName)
                ? $"{package.PackageId}-{package.Version}-{package.ArtifactFileName}"
                : $"{package.PackageId}-{package.Version}";
            var destinationPath = Path.Combine(Path.GetTempPath(), "agent-artifacts", context.RunId, destFileName);

            _logger.LogInformation(
                "Step AcquireArtifact: PackageIndex={PackageIndex}, PackageId={PackageId}, Url={ArtifactUrl}",
                package.PackageIndex,
                package.PackageId,
                artifactUrl);

            var chunkSizeBytes = _configuration.GetValue<int?>("Agent:ChunkSizeBytes") ?? 2 * 1024 * 1024;
            var useChunkedDownload = _configuration.GetValue<bool?>("Agent:UseChunkedDownload") ?? true;

            var acquireResult = await acquire.ExecuteAsync(new AcquireArtifactRequest
            {
                ArtifactUrl = artifactUrl,
                DestinationPath = destinationPath,
                ChunkSizeBytes = chunkSizeBytes,
                UseChunkedDownload = useChunkedDownload,
                ExpectedSha256 = package.ExpectedSha256
            }, stepCt);

            await SendStepStatusAsync(sendMessageAsync, context, "AcquireArtifact", package, acquireResult.Success, acquireResult.Error, stepCt);
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
            await SendStepStatusAsync(sendMessageAsync, context, "InstallOrUpgrade", package, installResult.Success, installResult.Error, stepCt);
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
            await SendStepStatusAsync(sendMessageAsync, context, "PostInstallVerify", package, verifyResult.Success, verifyResult.Error, stepCt);
            context.RecordStep("PostInstallVerify", package.PackageIndex, package.PackageId, verifyResult.Success, verifyResult.Error);

            if (!verifyResult.Success)
            {
                _logger.LogError(
                    "Pipeline halted at PostInstallVerify: PackageIndex={PackageIndex}, Error={Error}",
                    package.PackageIndex,
                    verifyResult.Error);
                return await FinalizeAsync(sendMessageAsync, context, ct);
            }

            // Step 3.1: Post-init steps (per package)
            if (!isUninstall)
            {
                var postInitSteps = package.PostInitSteps ?? new List<string>();
                for (int si = 0; si < postInitSteps.Count; si++)
                {
                    var stepName = $"PostInit_{package.PackageIndex}_{si}";
                    var envVars = InitStepEnvVars.Build(context, package, destinationPath);
                    var result = await initStepExecutor.ExecuteAsync(postInitSteps[si], defaultShell, stepName, envVars, 120, package.PackageIndex, initStepCb, ct);
                    context.RecordStep(stepName, package.PackageIndex, package.PackageId, result.Success, result.ErrorOutput);
                    if (!result.Success)
                    {
                        _logger.LogWarning("PostInit step {StepName} failed for package {PackageName}: {Error}", stepName, package.Name, result.ErrorOutput);
                    }
                }
            }
        }
        }

        // Phase 3: Post-workload / Post-uninstall init steps
        if (!isUninstall)
        {
            var postWorkloadSteps = context.Payload.PostWorkloadSteps ?? new List<string>();
            string? lastArtifactPath = null;
            if (packagesToInstall.Count > 0)
            {
                var lastPkg = packagesToInstall[^1];
                var lastDestFileName = !string.IsNullOrEmpty(lastPkg.ArtifactFileName)
                    ? $"{lastPkg.PackageId}-{lastPkg.Version}-{lastPkg.ArtifactFileName}"
                    : $"{lastPkg.PackageId}-{lastPkg.Version}";
                lastArtifactPath = Path.Combine(Path.GetTempPath(), "agent-artifacts", context.RunId, lastDestFileName);
            }

            for (int i = 0; i < postWorkloadSteps.Count; i++)
            {
                var stepName = $"PostWorkload_{i}";
                var envVars = InitStepEnvVars.Build(context, null, lastArtifactPath);
                var result = await initStepExecutor.ExecuteAsync(postWorkloadSteps[i], defaultShell, stepName, envVars, 180, -1, initStepCb, ct);
                context.RecordStep(stepName, -1, "", result.Success, result.ErrorOutput);
                if (!result.Success)
                {
                    _logger.LogError("PostWorkload step {StepName} failed: {Error}", stepName, result.ErrorOutput);
                }
            }
        }
        else
        {
            var postUninstallSteps = context.Payload.PostUninstallSteps ?? new List<string>();
            for (int i = 0; i < postUninstallSteps.Count; i++)
            {
                var stepName = $"PostUninstall_{i}";
                var envVars = InitStepEnvVars.Build(context, null, null);
                var result = await initStepExecutor.ExecuteAsync(postUninstallSteps[i], defaultShell, stepName, envVars, 180, -1, initStepCb, ct);
                context.RecordStep(stepName, -1, "", result.Success, result.ErrorOutput);
                if (!result.Success)
                {
                    _logger.LogError("PostUninstall step {StepName} failed: {Error}", stepName, result.ErrorOutput);
                }
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
        string? error,
        CancellationToken ct)
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
            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(TimeSpan.FromSeconds(5));
            await sendMessageAsync(envelope, sendCts.Token);
        }
        catch (Exception)
        {
            // Best-effort: don't let send failures break the pipeline
        }
    }

    private static Func<StepStatusPayload, Task> CreateInitStepStatusCallback(
        Func<MessageEnvelope, CancellationToken, Task> sendMessageAsync,
        PipelineContext context, CancellationToken ct)
    {
        return async (payload) =>
        {
            var envelope = new MessageEnvelope
            {
                MessageType = MessageTypes.StepStatus,
                RunId = context.RunId,
                AgentId = context.AgentId,
                Sequence = context.Sequence,
                Payload = payload
            };
            try
            {
                using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                sendCts.CancelAfter(TimeSpan.FromSeconds(5));
                await sendMessageAsync(envelope, sendCts.Token);
            }
            catch { /* best-effort */ }
        };
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
