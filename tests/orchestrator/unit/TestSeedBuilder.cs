using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;

namespace DeploymentPoC.Orchestrator.Tests;

internal sealed class TestSeedBuilder
{
    public static NodeEntity CreateNode(
        Guid? nodeId = null,
        string hostname = "test-node",
        string displayName = "Test Node",
        string status = "Online",
        string ipAddress = "",
        string osVersion = "",
        string agentVersion = "",
        DateTime? lastSeenUtc = null,
        DateTime? firstConnectedUtc = null,
        string description = "")
    {
        return new NodeEntity
        {
            NodeId = nodeId ?? Guid.NewGuid(),
            Hostname = hostname,
            DisplayName = displayName,
            Status = status,
            IpAddress = ipAddress,
            OsVersion = osVersion,
            AgentVersion = agentVersion,
            LastSeenUtc = lastSeenUtc ?? DateTime.UtcNow,
            FirstConnectedUtc = firstConnectedUtc,
            Description = description
        };
    }

    public static WorkloadDefinitionEntity CreateWorkload(
        Guid? workloadId = null,
        string name = "test-workload",
        string? description = null)
    {
        return new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId ?? Guid.NewGuid(),
            Name = name,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public static WorkloadRevisionEntity CreateRevision(
        Guid? revisionId = null,
        Guid? workloadId = null,
        string version = "1.0.0",
        bool isPublished = true)
    {
        return new WorkloadRevisionEntity
        {
            RevisionId = revisionId ?? Guid.NewGuid(),
            WorkloadId = workloadId ?? Guid.NewGuid(),
            Version = version,
            IsPublished = isPublished,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static PackageEntity CreatePackage(
        Guid? packageId = null,
        string name = "test-pkg",
        string version = "1.0.0",
        string installType = "",
        string installArgs = "",
        string uninstallArgs = "",
        string sourcePath = "",
        string detectionConfigJson = "",
        int timeoutSeconds = 300)
    {
        return new PackageEntity
        {
            PackageId = packageId ?? Guid.NewGuid(),
            Name = name,
            Version = version,
            InstallType = installType,
            InstallArgs = installArgs,
            UninstallArgs = uninstallArgs,
            SourcePath = sourcePath,
            DetectionConfigJson = detectionConfigJson,
            TimeoutSeconds = timeoutSeconds,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static NodeWorkloadStateEntity CreateNodeWorkloadState(
        Guid? nodeWorkloadStateId = null,
        Guid? nodeId = null,
        Guid? workloadId = null,
        Guid? currentRevisionId = null,
        string packageStatesJson = "{}",
        string status = "Unknown")
    {
        return new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = nodeWorkloadStateId ?? Guid.NewGuid(),
            NodeId = nodeId ?? Guid.NewGuid(),
            WorkloadId = workloadId ?? Guid.NewGuid(),
            CurrentRevisionId = currentRevisionId,
            PackageStatesJson = packageStatesJson,
            Status = status,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public static WorkloadPackageEntity CreateWorkloadPackage(
        Guid? workloadPackageId = null,
        Guid? revisionId = null,
        Guid? packageId = null,
        int packageIndex = 0)
    {
        return new WorkloadPackageEntity
        {
            WorkloadPackageId = workloadPackageId ?? Guid.NewGuid(),
            RevisionId = revisionId ?? Guid.NewGuid(),
            PackageId = packageId ?? Guid.NewGuid(),
            PackageIndex = packageIndex
        };
    }

    private readonly InstallerDbContext _db;
    private readonly List<NodeEntity> _nodes = [];
    private readonly List<WorkloadDefinitionEntity> _workloads = [];
    private readonly List<WorkloadRevisionEntity> _revisions = [];
    private readonly List<PackageEntity> _packages = [];
    private readonly List<WorkloadPackageEntity> _workloadPackages = [];
    private readonly List<NodeWorkloadStateEntity> _nodeWorkloadStates = [];
    private readonly List<WorkloadRunEntity> _workloadRuns = [];

    public TestSeedBuilder(InstallerDbContext db) => _db = db;

    public TestSeedBuilder WithNode(
        Guid nodeId,
        string hostname = "test-node",
        string displayName = "Test Node",
        string status = "Online",
        string? ipAddress = null,
        string? osVersion = null,
        string? agentVersion = null)
    {
        _nodes.Add(new NodeEntity
        {
            NodeId = nodeId,
            Hostname = hostname,
            DisplayName = displayName,
            Status = status,
            IpAddress = ipAddress ?? string.Empty,
            OsVersion = osVersion ?? string.Empty,
            AgentVersion = agentVersion ?? string.Empty,
            LastSeenUtc = DateTime.UtcNow
        });
        return this;
    }

    public TestSeedBuilder WithWorkload(Guid workloadId, string name = "test-workload", string? description = null)
    {
        _workloads.Add(new WorkloadDefinitionEntity
        {
            WorkloadId = workloadId,
            Name = name,
            Description = description
        });
        return this;
    }

    public TestSeedBuilder WithRevision(
        Guid revisionId,
        Guid workloadId,
        string version = "1.0.0",
        bool published = true)
    {
        _revisions.Add(new WorkloadRevisionEntity
        {
            RevisionId = revisionId,
            WorkloadId = workloadId,
            Version = version,
            IsPublished = published
        });
        return this;
    }

    public TestSeedBuilder WithPackage(
        Guid packageId,
        string name = "test-pkg",
        string version = "1.0.0",
        string? installType = null,
        string? installArgs = null,
        string? uninstallArgs = null,
        string? sourcePath = null,
        string? detectionConfigJson = null,
        string? expectedExitCodesJson = null,
        int? timeoutSeconds = null)
    {
        var entity = new PackageEntity
        {
            PackageId = packageId,
            Name = name,
            Version = version
        };
        if (installType is not null) entity.InstallType = installType;
        if (installArgs is not null) entity.InstallArgs = installArgs;
        if (uninstallArgs is not null) entity.UninstallArgs = uninstallArgs;
        if (sourcePath is not null) entity.SourcePath = sourcePath;
        if (detectionConfigJson is not null) entity.DetectionConfigJson = detectionConfigJson;
        if (expectedExitCodesJson is not null) entity.ExpectedExitCodesJson = expectedExitCodesJson;
        if (timeoutSeconds.HasValue) entity.TimeoutSeconds = timeoutSeconds.Value;
        _packages.Add(entity);
        return this;
    }

    public TestSeedBuilder WithWorkloadPackage(
        Guid revisionId,
        Guid packageId,
        int packageIndex = 0)
    {
        _workloadPackages.Add(new WorkloadPackageEntity
        {
            WorkloadPackageId = Guid.NewGuid(),
            RevisionId = revisionId,
            PackageId = packageId,
            PackageIndex = packageIndex
        });
        return this;
    }

    public TestSeedBuilder WithNodeWorkloadState(
        Guid nodeId,
        Guid workloadId,
        Guid? currentRevisionId = null,
        string packageStatesJson = "{}",
        string status = "Unknown")
    {
        _nodeWorkloadStates.Add(new NodeWorkloadStateEntity
        {
            NodeWorkloadStateId = Guid.NewGuid(),
            NodeId = nodeId,
            WorkloadId = workloadId,
            CurrentRevisionId = currentRevisionId,
            PackageStatesJson = packageStatesJson,
            Status = status
        });
        return this;
    }

    public TestSeedBuilder WithWorkloadRun(
        Guid runId,
        Guid workloadId,
        Guid revisionId,
        Guid nodeId,
        string state = "Queued",
        string? idempotencyKey = null)
    {
        _workloadRuns.Add(new WorkloadRunEntity
        {
            RunId = runId,
            WorkloadId = workloadId,
            RevisionId = revisionId,
            NodeId = nodeId,
            State = state,
            CreatedAtUtc = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        });
        return this;
    }

    public async Task SeedAsync()
    {
        if (_workloads.Count > 0) _db.WorkloadDefinitions.AddRange(_workloads);
        if (_revisions.Count > 0) _db.WorkloadRevisions.AddRange(_revisions);
        if (_packages.Count > 0) _db.Packages.AddRange(_packages);
        if (_workloadPackages.Count > 0) _db.WorkloadPackages.AddRange(_workloadPackages);
        if (_nodes.Count > 0) _db.Nodes.AddRange(_nodes);
        if (_nodeWorkloadStates.Count > 0) _db.NodeWorkloadStates.AddRange(_nodeWorkloadStates);
        if (_workloadRuns.Count > 0) _db.WorkloadRuns.AddRange(_workloadRuns);
        await _db.SaveChangesAsync();
    }
}
