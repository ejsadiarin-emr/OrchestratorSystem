using System.Text.Json;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using DeploymentPoC.Orchestrator.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class WorkloadRunDispatcher
{
    private readonly InstallerDbContext _db;
    private readonly IHubContext<AgentRuntimeHub> _hubContext;
    private readonly ArtifactStoreService _artifactStore;
    private readonly ILogger<WorkloadRunDispatcher> _logger;

    public WorkloadRunDispatcher(InstallerDbContext db, IHubContext<AgentRuntimeHub> hubContext, ArtifactStoreService artifactStore, ILogger<WorkloadRunDispatcher> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _artifactStore = artifactStore;
        _logger = logger;
    }

    public async Task DispatchAsync(WorkloadRunEntity run, CancellationToken ct = default)
    {
        if (!run.NodeId.HasValue)
        {
            _logger.LogWarning("Cannot dispatch run {RunId}: NodeId is null", run.RunId);
            return;
        }

        var workload = await _db.WorkloadDefinitions.AsNoTracking().FirstOrDefaultAsync(w => w.WorkloadId == run.WorkloadId, ct);
        var revision = await _db.WorkloadRevisions
            .AsNoTracking()
            .Include(r => r.Packages)
            .FirstOrDefaultAsync(r => r.RevisionId == run.RevisionId, ct);

        if (workload is null || revision is null)
        {
            _logger.LogWarning("Cannot dispatch run {RunId}: workload or revision not found", run.RunId);
            return;
        }

        var packageEntities = await _db.Packages
            .AsNoTracking()
            .Where(p => revision.Packages.Select(wp => wp.PackageId).Contains(p.PackageId))
            .ToListAsync(ct);

        var packageAssignments = BuildPackageAssignments(revision.Packages.ToList(), packageEntities);

        var nodeState = await _db.NodeWorkloadStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.NodeId == run.NodeId && s.WorkloadId == run.WorkloadId, ct);

        List<PackageAssignment> currentPackages = new();
        if (nodeState?.CurrentRevisionId is not null && nodeState.CurrentRevisionId != Guid.Empty)
        {
            var currentRevisionPackages = await _db.WorkloadPackages
                .AsNoTracking()
                .Where(wp => wp.RevisionId == nodeState.CurrentRevisionId)
                .OrderBy(wp => wp.PackageIndex)
                .ToListAsync(ct);

            var currentPackageIds = currentRevisionPackages.Select(wp => wp.PackageId).ToList();
            var currentPackageEntities = await _db.Packages
                .AsNoTracking()
                .Where(p => currentPackageIds.Contains(p.PackageId))
                .ToListAsync(ct);

            currentPackages = BuildPackageAssignments(currentRevisionPackages, currentPackageEntities);
        }

        var payload = new AssignRunPayload
        {
            RunId = run.RunId,
            WorkloadId = run.WorkloadId,
            WorkloadName = workload.Name,
            RevisionId = run.RevisionId,
            RevisionVersion = revision.Version,
            Mode = run.Mode,
            NodeId = run.NodeId.Value,
            Packages = packageAssignments,
            CurrentPackages = currentPackages,
            ForceInstall = run.ForceInstall,
            PreUpgradeActions = new List<string>()
        };

        var envelope = new MessageEnvelope
        {
            MessageType = MessageTypes.AssignRun,
            RunId = run.RunId.ToString(),
            AgentId = run.NodeId.ToString(),
            Sequence = 0,
            Payload = payload
        };

        var groupName = $"node-{run.NodeId}";
        _logger.LogInformation("Sending AssignRun to group {GroupName} for RunId={RunId}", groupName, run.RunId);

        // TODO: remove after polling validated — SignalR push disabled in favor of HTTP polling
        // await _hubContext.Clients.Group(groupName).SendAsync("AssignRun", envelope, ct);
        _logger.LogInformation("AssignRun queued (SignalR push disabled — agent will poll). Group={GroupName}, RunId={RunId}", groupName, run.RunId);
    }

    public async Task DispatchQueuedRunsAsync(Guid nodeId, CancellationToken ct = default)
    {
        var queuedRuns = await _db.WorkloadRuns
            .Where(r => r.NodeId == nodeId && r.State == "Queued")
            .ToListAsync(ct);

        if (queuedRuns.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} queued runs for NodeId={NodeId}, re-sending AssignRun", queuedRuns.Count, nodeId);

        foreach (var run in queuedRuns)
        {
            try
            {
                await DispatchAsync(run, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-send AssignRun for RunId={RunId}", run.RunId);
            }
        }
    }

    private List<PackageAssignment> BuildPackageAssignments(List<WorkloadPackageEntity> workloadPackages, List<PackageEntity> packageEntities)
    {
        return workloadPackages
            .OrderBy(p => p.PackageIndex)
            .Select(wp =>
            {
                const string artifactPath = "{artifactPath}";
                var pkg = packageEntities.FirstOrDefault(p => p.PackageId == wp.PackageId);
                var rawInstallType = pkg?.InstallType ?? "exe";
                var installType = string.IsNullOrWhiteSpace(rawInstallType) || rawInstallType.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                    ? "exe"
                    : rawInstallType;
                string command = pkg?.SourcePath ?? artifactPath;
                string arguments = pkg?.InstallArgs ?? "";

                var expectedExitCodes = new List<int>();
                if (!string.IsNullOrWhiteSpace(pkg?.ExpectedExitCodesJson))
                {
                    try
                    {
                        expectedExitCodes = JsonSerializer.Deserialize<List<int>>(pkg.ExpectedExitCodesJson) ?? new List<int>();
                    }
                    catch (JsonException)
                    {
                        expectedExitCodes = new List<int>();
                    }
                }

                if (expectedExitCodes.Count == 0)
                {
                    expectedExitCodes = new List<int> { 0 };
                }

                _artifactStore.TryGetArtifactFileName(pkg?.Name ?? "", pkg?.Version ?? "", out var artifactFileName);

                var timeoutSeconds = pkg?.TimeoutSeconds > 0 ? pkg.TimeoutSeconds : 300;
                return new PackageAssignment
                {
                    PackageIndex = wp.PackageIndex,
                    PackageId = wp.PackageId.ToString(),
                    Name = pkg?.Name ?? "",
                    Version = pkg?.Version ?? "",
                    Channel = "stable",
                    ArtifactFileName = artifactFileName,
                    InstallAdapter = new InstallAdapterConfig
                    {
                        Type = installType,
                        Command = command,
                        Arguments = arguments,
                        UninstallArgs = pkg?.UninstallArgs ?? "",
                        ExpectedExitCodes = expectedExitCodes,
                        TimeoutSeconds = timeoutSeconds
                    },
                    Detection = BuildDetectionConfig(pkg)
                };
            })
            .ToList();
    }

    private static DetectionConfig BuildDetectionConfig(PackageEntity? pkg)
    {
        if (!string.IsNullOrWhiteSpace(pkg?.DetectionConfigJson))
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<DetectionConfig>(pkg.DetectionConfigJson)
                    ?? new DetectionConfig();
            }
            catch
            {
            }
        }

        return new DetectionConfig
        {
            Type = "file",
            Path = pkg?.Name ?? "",
            ExpectedVersion = pkg?.Version ?? ""
        };
    }
}
