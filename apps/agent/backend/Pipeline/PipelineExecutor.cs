using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Jobs;
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

        context.PipelineStartUtc = DateTime.UtcNow;

        var targetPackages = context.Payload.Packages
            .OrderBy(p => p.PackageIndex)
            .ToList();

        var baseDiff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, null, context.Payload.Mode);
        var preCheckResults = new Dictionary<string, PreCheckResult>();
        context.PreCheckResults = preCheckResults;
        context.PostVerifyResults = new Dictionary<string, PostVerifyResult>();

        // Phase 0: Pre-Check
        if (!context.ForceInstall)
        {

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

            // Log delta summary
            _logger.LogInformation("[PreCheckProbe] === Delta Summary ===");
            foreach (var package in packagesToProbe)
            {
                if (preCheckResults.TryGetValue(package.Name, out var result))
                {
                    var action = result.Status switch
                    {
                        PreCheckStatus.AlreadySatisfied => string.IsNullOrWhiteSpace(package.Detection.ExpectedVersion) || VersionComparer.Matches(package.Detection.ExpectedVersion, result.ActualVersion)
                            ? "Unchanged"
                            : "InstallOrUpgrade",
                        PreCheckStatus.WrongVersion => "InstallOrUpgrade",
                        PreCheckStatus.NotPresent => "InstallOrUpgrade",
                        _ => "Unknown"
                    };
                    _logger.LogInformation(
                        "[PreCheckProbe] {PackageName}: Expected={ExpectedVersion}, Actual={ActualVersion}, Action={Action}",
                        package.Name,
                        package.Detection.ExpectedVersion,
                        result.ActualVersion,
                        action);
                }
            }
        }

        var diff = DiffEngine.ComputeDiff(context.CurrentPackages, targetPackages, context.ForceInstall ? null : preCheckResults, context.Payload.Mode);
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
                var effectiveConfig = package.InstallAdapter;

                if (string.IsNullOrWhiteSpace(package.InstallAdapter.UninstallCommand))
                {
                    var resolved = UninstallPackage.ResolveRegistryUninstaller(package.Name);
                    if (resolved.Command is not null)
                    {
                        effectiveConfig = new InstallAdapterConfig
                        {
                            Type = package.InstallAdapter.Type,
                            Command = package.InstallAdapter.Command,
                            Arguments = package.InstallAdapter.Arguments,
                            UninstallCommand = resolved.Command,
                            UninstallArgs = resolved.Arguments ?? string.Empty,
                            ExpectedExitCodes = package.InstallAdapter.ExpectedExitCodes,
                            TimeoutSeconds = package.InstallAdapter.TimeoutSeconds,
                            UpgradeBehavior = package.InstallAdapter.UpgradeBehavior,
                        };
                    }
                    else
                    {
                        _logger.LogError(
                            "No uninstall command configured or found in registry for {PackageName}. Uninstall cannot proceed.",
                            package.Name);
                        await SendStepStatusAsync(sendMessageAsync, context, "UninstallSkippedNoCommand", package, false, "no_uninstall_command_available", stepCt);
                        context.RecordStep("UninstallSkippedNoCommand", package.PackageIndex, package.PackageId, false, "no_uninstall_command_available");
                        continue;
                    }
                }

                var needsArtifact = string.Equals(effectiveConfig.Type, "msi", StringComparison.OrdinalIgnoreCase) ||
                                    (effectiveConfig.UninstallCommand?.Contains("{artifactPath}", StringComparison.OrdinalIgnoreCase) == true) ||
                                    (effectiveConfig.UninstallArgs?.Contains("{artifactPath}", StringComparison.OrdinalIgnoreCase) == true);

                if (needsArtifact)
                {
                    var artifactUrl = !string.IsNullOrEmpty(package.DownloadUrl)
                        ? $"{context.OrchestratorBaseUrl.TrimEnd('/')}{package.DownloadUrl}"
                        : $"{context.OrchestratorBaseUrl.TrimEnd('/')}/api/artifacts/{package.PackageId}/download";
                    var destFileName = !string.IsNullOrEmpty(package.ArtifactFileName)
                        ? $"{package.PackageId}-{package.Version}-{package.ArtifactFileName}"
                        : $"{package.PackageId}-{package.Version}";
                    destinationPath = Path.Combine(Path.GetTempPath(), "agent-artifacts", context.RunId, destFileName);

                    _logger.LogInformation(
                        "Step AcquireArtifactForUninstall: PackageIndex={PackageIndex}, PackageId={PackageId}, Url={ArtifactUrl}",
                        package.PackageIndex,
                        package.PackageId,
                        artifactUrl);

                    var chunkSizeBytes = _configuration.GetValue<int?>("Agent:ChunkSizeBytes") ?? 8 * 1024 * 1024;
                    var useChunkedDownload = _configuration.GetValue<bool?>("Agent:UseChunkedDownload") ?? true;

                    var acquireResult = await acquire.ExecuteAsync(new AcquireArtifactRequest
                    {
                        ArtifactUrl = artifactUrl,
                        DestinationPath = destinationPath,
                        ChunkSizeBytes = chunkSizeBytes,
                        UseChunkedDownload = useChunkedDownload,
                        ExpectedSha256 = package.ExpectedSha256
                    }, stepCt);

                    await SendStepStatusAsync(sendMessageAsync, context, "AcquireArtifactForUninstall", package, acquireResult.Success, acquireResult.Error, stepCt);
                    context.RecordStep("AcquireArtifactForUninstall", package.PackageIndex, package.PackageId, acquireResult.Success, acquireResult.Error);

                    if (!acquireResult.Success)
                    {
                        _logger.LogError(
                            "Pipeline halted at AcquireArtifactForUninstall: PackageIndex={PackageIndex}, Error={Error}",
                            package.PackageIndex,
                            acquireResult.Error);
                        return await FinalizeAsync(sendMessageAsync, context, ct);
                    }
                }

                

                var logCommand = effectiveConfig.UninstallCommand?.Replace("{artifactPath}", destinationPath ?? "", StringComparison.OrdinalIgnoreCase) ?? "";
                var logArgs = effectiveConfig.UninstallArgs?.Replace("{artifactPath}", destinationPath ?? "", StringComparison.OrdinalIgnoreCase) ?? "";
                _logger.LogInformation(
                    "Executing uninstall: Command={Command}, Args={Args}, Type={Type}",
                    logCommand,
                    logArgs,
                    effectiveConfig.Type);

                var uninstallResult = await UninstallPackage.ExecuteAsync(effectiveConfig, destinationPath, stepCt);
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

            // Post-uninstall verification with retry for async uninstallers
            foreach (var package in targetPackages)
            {
                var stepCt = ct;
                const int maxRetries = 3;
                const int retryDelaySeconds = 5;
                bool isStillInstalled = false;
                PreCheckResult lastDetectResult = default!;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    _logger.LogInformation(
                        "Step PostUninstallVerify: PackageIndex={PackageIndex}, PackageId={PackageId}, DetectionType={DetectionType}, Attempt={Attempt}",
                        package.PackageIndex,
                        package.PackageId,
                        package.Detection.Type,
                        attempt);

                    lastDetectResult = await PackageDetector.DetectAsync(package.Detection, stepCt);
                    isStillInstalled = lastDetectResult.Status != PreCheckStatus.NotPresent;

                    if (!isStillInstalled)
                        break;

                    if (attempt < maxRetries)
                    {
                        _logger.LogInformation(
                            "PostUninstallVerify retry {Attempt}/{MaxRetries} for {PackageName} — still detected, waiting {Delay}s...",
                            attempt,
                            maxRetries,
                            package.Name,
                            retryDelaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), stepCt);
                    }
                }

                await SendStepStatusAsync(sendMessageAsync, context, "PostUninstallVerify", package, !isStillInstalled, isStillInstalled ? "still_installed" : null, stepCt);
                context.RecordStep("PostUninstallVerify", package.PackageIndex, package.PackageId, !isStillInstalled, isStillInstalled ? "still_installed" : null);

                context.PostVerifyResults[package.Name] = new PostVerifyResult
                {
                    Success = !isStillInstalled,
                    ActualVersion = isStillInstalled ? lastDetectResult.ActualVersion : null,
                    Error = isStillInstalled ? "still_installed" : null
                };

                if (isStillInstalled)
                {
                    _logger.LogError(
                        "Pipeline halted at PostUninstallVerify: PackageIndex={PackageIndex}, PackageId={PackageId}",
                        package.PackageIndex,
                        package.PackageId);
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
                var effectiveConfig = uninstallConfig;

                if (string.IsNullOrWhiteSpace(uninstallConfig.UninstallCommand))
                {
                    var resolved2 = UninstallPackage.ResolveRegistryUninstaller(package.Name);
                    if (resolved2.Command is not null)
                    {
                        effectiveConfig = new InstallAdapterConfig
                        {
                            Type = uninstallConfig.Type,
                            Command = uninstallConfig.Command,
                            Arguments = uninstallConfig.Arguments,
                            UninstallCommand = resolved2.Command,
                            UninstallArgs = resolved2.Arguments ?? string.Empty,
                            ExpectedExitCodes = uninstallConfig.ExpectedExitCodes,
                            TimeoutSeconds = uninstallConfig.TimeoutSeconds,
                            UpgradeBehavior = uninstallConfig.UpgradeBehavior,
                        };
                    }
                    else
                    {
                        _logger.LogError(
                            "No uninstall command configured or found in registry for {PackageName}. Uninstall cannot proceed.",
                            package.Name);
                        await SendStepStatusAsync(sendMessageAsync, context, "UninstallSkippedNoCommand", uninstallPackage, false, "no_uninstall_command_available", stepCt);
                        context.RecordStep("UninstallSkippedNoCommand", uninstallPackage.PackageIndex, uninstallPackage.PackageId, false, "no_uninstall_command_available");
                        continue;
                    }
                }

                var needsArtifact = string.Equals(effectiveConfig.Type, "msi", StringComparison.OrdinalIgnoreCase) ||
                                    (effectiveConfig.UninstallCommand?.Contains("{artifactPath}", StringComparison.OrdinalIgnoreCase) == true) ||
                                    (effectiveConfig.UninstallArgs?.Contains("{artifactPath}", StringComparison.OrdinalIgnoreCase) == true);

                if (needsArtifact)
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

                    var chunkSizeBytes = _configuration.GetValue<int?>("Agent:ChunkSizeBytes") ?? 8 * 1024 * 1024;
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

                var logCommand = effectiveConfig.UninstallCommand?.Replace("{artifactPath}", destinationPath ?? "", StringComparison.OrdinalIgnoreCase) ?? "";
                var logArgs = effectiveConfig.UninstallArgs?.Replace("{artifactPath}", destinationPath ?? "", StringComparison.OrdinalIgnoreCase) ?? "";
                _logger.LogInformation(
                    "Executing uninstall: Command={Command}, Args={Args}, Type={Type}",
                    logCommand,
                    logArgs,
                    effectiveConfig.Type);

                var uninstallResult = await UninstallPackage.ExecuteAsync(effectiveConfig, destinationPath, stepCt);
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

            if (preCheckResults?.TryGetValue(package.Name, out var preCheck) == true
                && preCheck.Status == PreCheckStatus.AlreadySatisfied
                && (string.IsNullOrWhiteSpace(package.Detection.ExpectedVersion) || VersionComparer.Matches(package.Detection.ExpectedVersion, preCheck.ActualVersion)))
            {
                _logger.LogInformation(
                    "Step PreCheckSkipped: PackageIndex={PackageIndex}, PackageId={PackageId}, Reason=version_matches (Expected={ExpectedVersion}, Actual={ActualVersion})",
                    package.PackageIndex,
                    package.PackageId,
                    package.Detection.ExpectedVersion,
                    preCheck.ActualVersion);

                await SendStepStatusAsync(sendMessageAsync, context, "PreCheckSkipped", package, true, null, stepCt);
                context.RecordStep("PreCheckSkipped", package.PackageIndex, package.PackageId, true, null);
                continue;
            }
            else if (preCheckResults?.TryGetValue(package.Name, out preCheck) == true && preCheck.Status == PreCheckStatus.AlreadySatisfied)
            {
                // AlreadySatisfied but version mismatch — this means old version is installed, need update
                _logger.LogInformation(
                    "Step PreCheckWrongVersion: PackageIndex={PackageIndex}, PackageId={PackageId}, Expected={ExpectedVersion}, Actual={ActualVersion}",
                    package.PackageIndex,
                    package.PackageId,
                    package.Detection.ExpectedVersion,
                    preCheck.ActualVersion);
                // DON'T skip — fall through to install
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

            var chunkSizeBytes = _configuration.GetValue<int?>("Agent:ChunkSizeBytes") ?? 8 * 1024 * 1024;
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

            if (verifyResult.Success)
            {
                var detectForReport = await PackageDetector.DetectAsync(package.Detection, stepCt);
                context.PostVerifyResults[package.Name] = new PostVerifyResult
                {
                    Success = true,
                    ActualVersion = detectForReport.ActualVersion
                };
            }
            else
            {
                context.PostVerifyResults[package.Name] = new PostVerifyResult
                {
                    Success = false,
                    Error = verifyResult.Error
                };
            }

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

        var postInstallVerifyFailed = context.StepHistory.Any(s =>
            s.StepName == "PostInstallVerify" && !s.Success);
        var reasonCode = postInstallVerifyFailed
            ? (int)ReasonCodes.PostInstallVerifyFailed
            : (int?)null;

        var report = ReportGenerator.Generate(context, context.PreCheckResults, context.PostVerifyResults);

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
                StepCount = context.StepHistory.Count,
                Report = report,
                ReasonCode = reasonCode
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
            StepsExecuted = context.StepHistory.Count,
            Report = report,
            ReasonCode = reasonCode
        };
    }
}

public sealed class PipelineResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int StepsExecuted { get; set; }
    public string? Report { get; set; }
    public int? ReasonCode { get; set; }
}


