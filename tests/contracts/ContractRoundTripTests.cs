using System.Text.Json;
using DeploymentPoC.Contracts.Runtime;
using DeploymentPoC.Contracts.Runtime.Probes;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Xunit;

namespace DeploymentPoC.Contracts.Tests;

public class ContractRoundTripTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void PendingWorkloadRunResponse_RoundTrips_ThroughJson()
    {
        var original = new PendingWorkloadRunResponse
        {
            RunId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            WorkloadId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
            WorkloadName = "TestWorkload",
            Mode = "Install",
            Packages =
            [
                new PendingPackageDto
                {
                    PackageEntityId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012"),
                    Name = "pkg-a",
                    Version = "1.0.0",
                    Filename = "pkg-a.msi",
                    DownloadUrl = "http://example.com/pkg-a.msi",
                    ExpectedSha256 = "abc123",
                    SizeBytes = 1024,
                    InstallAdapter = new InstallAdapterConfig
                    {
                        Type = "msi",
                        Command = "msiexec.exe",
                        Arguments = "/i pkg-a.msi /qn",
                        UninstallCommand = "msiexec.exe",
                        UninstallArgs = "/x pkg-a.msi /qn",
                        UpgradeBehavior = "InPlace",
                        ExpectedExitCodes = [0, 3010],
                        TimeoutSeconds = 600
                    },
                    Detection = new DetectionConfig
                    {
                        Type = "product",
                        Path = "Product A",
                        ExpectedVersion = "1.0.0"
                    }
                }
            ],
            CurrentPackages =
            [
                new PendingPackageDto
                {
                    PackageEntityId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123"),
                    Name = "pkg-b",
                    Version = "2.0.0",
                    Filename = "pkg-b.exe",
                    DownloadUrl = "http://example.com/pkg-b.exe"
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PendingWorkloadRunResponse>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.RunId, deserialized.RunId);
        Assert.Equal(original.WorkloadId, deserialized.WorkloadId);
        Assert.Equal(original.WorkloadName, deserialized.WorkloadName);
        Assert.Equal(original.Mode, deserialized.Mode);
        Assert.Equal(original.Packages.Count, deserialized.Packages.Count);
        Assert.Equal(original.Packages[0].PackageEntityId, deserialized.Packages[0].PackageEntityId);
        Assert.Equal(original.Packages[0].Name, deserialized.Packages[0].Name);
        Assert.Equal(original.Packages[0].Version, deserialized.Packages[0].Version);
        Assert.Equal(original.Packages[0].Filename, deserialized.Packages[0].Filename);
        Assert.Equal(original.Packages[0].DownloadUrl, deserialized.Packages[0].DownloadUrl);
        Assert.Equal(original.Packages[0].ExpectedSha256, deserialized.Packages[0].ExpectedSha256);
        Assert.Equal(original.Packages[0].SizeBytes, deserialized.Packages[0].SizeBytes);
        Assert.Equal(original.Packages[0].InstallAdapter.Type, deserialized.Packages[0].InstallAdapter.Type);
        Assert.Equal(original.Packages[0].InstallAdapter.Command, deserialized.Packages[0].InstallAdapter.Command);
        Assert.Equal(original.Packages[0].InstallAdapter.Arguments, deserialized.Packages[0].InstallAdapter.Arguments);
        Assert.Equal(original.Packages[0].InstallAdapter.UninstallCommand, deserialized.Packages[0].InstallAdapter.UninstallCommand);
        Assert.Equal(original.Packages[0].InstallAdapter.UninstallArgs, deserialized.Packages[0].InstallAdapter.UninstallArgs);
        Assert.Equal(original.Packages[0].InstallAdapter.UpgradeBehavior, deserialized.Packages[0].InstallAdapter.UpgradeBehavior);
        Assert.Equal(original.Packages[0].InstallAdapter.ExpectedExitCodes, deserialized.Packages[0].InstallAdapter.ExpectedExitCodes);
        Assert.Equal(original.Packages[0].InstallAdapter.TimeoutSeconds, deserialized.Packages[0].InstallAdapter.TimeoutSeconds);
        Assert.Equal(original.Packages[0].Detection.Type, deserialized.Packages[0].Detection.Type);
        Assert.Equal(original.Packages[0].Detection.Path, deserialized.Packages[0].Detection.Path);
        Assert.Equal(original.Packages[0].Detection.ExpectedVersion, deserialized.Packages[0].Detection.ExpectedVersion);
        Assert.Equal(original.CurrentPackages.Count, deserialized.CurrentPackages.Count);
        Assert.Equal(original.CurrentPackages[0].Name, deserialized.CurrentPackages[0].Name);
    }

    [Fact]
    public void PendingPackageDto_RoundTrips_ThroughJson()
    {
        var original = new PendingPackageDto
        {
            PackageEntityId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Name = "TestPackage",
            Version = "3.1.4",
            Filename = "test.msi",
            DownloadUrl = "https://cdn.example.com/test.msi",
            ExpectedSha256 = "def456",
            SizeBytes = 2048000,
            InstallAdapter = new InstallAdapterConfig
            {
                Type = "msi",
                Command = "msiexec.exe",
                Arguments = "/i test.msi /qn",
                UninstallCommand = "msiexec.exe",
                UninstallArgs = "/x test.msi /qn",
                UpgradeBehavior = "UninstallFirst",
                ExpectedExitCodes = [0],
                TimeoutSeconds = 900
            },
            Detection = new DetectionConfig
            {
                Type = "file",
                Path = @"C:\Program Files\Test\test.exe",
                ExpectedVersion = "3.1.4"
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PendingPackageDto>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.PackageEntityId, deserialized.PackageEntityId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Filename, deserialized.Filename);
        Assert.Equal(original.DownloadUrl, deserialized.DownloadUrl);
        Assert.Equal(original.ExpectedSha256, deserialized.ExpectedSha256);
        Assert.Equal(original.SizeBytes, deserialized.SizeBytes);
        Assert.Equal(original.InstallAdapter.Type, deserialized.InstallAdapter.Type);
        Assert.Equal(original.InstallAdapter.Command, deserialized.InstallAdapter.Command);
        Assert.Equal(original.InstallAdapter.Arguments, deserialized.InstallAdapter.Arguments);
        Assert.Equal(original.InstallAdapter.UninstallCommand, deserialized.InstallAdapter.UninstallCommand);
        Assert.Equal(original.InstallAdapter.UninstallArgs, deserialized.InstallAdapter.UninstallArgs);
        Assert.Equal(original.InstallAdapter.UpgradeBehavior, deserialized.InstallAdapter.UpgradeBehavior);
        Assert.Equal(original.InstallAdapter.ExpectedExitCodes, deserialized.InstallAdapter.ExpectedExitCodes);
        Assert.Equal(original.InstallAdapter.TimeoutSeconds, deserialized.InstallAdapter.TimeoutSeconds);
        Assert.Equal(original.Detection.Type, deserialized.Detection.Type);
        Assert.Equal(original.Detection.Path, deserialized.Detection.Path);
        Assert.Equal(original.Detection.ExpectedVersion, deserialized.Detection.ExpectedVersion);
    }

    [Fact]
    public void FinalizationPayload_RoundTrips_ThroughJson()
    {
        var original = new FinalizationPayload
        {
            Result = "Success",
            Error = null,
            StepCount = 5,
            Report = "All steps completed successfully.",
            ReasonCode = 0
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<FinalizationPayload>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Result, deserialized.Result);
        Assert.Null(deserialized.Error);
        Assert.Equal(original.StepCount, deserialized.StepCount);
        Assert.Equal(original.Report, deserialized.Report);
        Assert.Equal(original.ReasonCode, deserialized.ReasonCode);
    }

    [Fact]
    public void FinalizationPayload_WithError_RoundTrips_ThroughJson()
    {
        var original = new FinalizationPayload
        {
            Result = "Failure",
            Error = "Package installation timed out after 300 seconds.",
            StepCount = 3,
            Report = null,
            ReasonCode = 1
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<FinalizationPayload>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Result, deserialized.Result);
        Assert.Equal(original.Error, deserialized.Error);
        Assert.Equal(original.StepCount, deserialized.StepCount);
        Assert.Null(deserialized.Report);
        Assert.Equal(original.ReasonCode, deserialized.ReasonCode);
    }

    [Fact]
    public void StepStatusPayload_RoundTrips_ThroughJson()
    {
        var original = new StepStatusPayload
        {
            StepName = "Install",
            PackageIndex = 0,
            PackageId = "pkg-001",
            Status = "Running",
            Error = null
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<StepStatusPayload>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.StepName, deserialized.StepName);
        Assert.Equal(original.PackageIndex, deserialized.PackageIndex);
        Assert.Equal(original.PackageId, deserialized.PackageId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Null(deserialized.Error);
    }

    [Fact]
    public void StepStatusPayload_WithError_RoundTrips_ThroughJson()
    {
        var original = new StepStatusPayload
        {
            StepName = "PostInit",
            PackageIndex = 2,
            PackageId = "pkg-003",
            Status = "Failed",
            Error = "Process exited with code 1."
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<StepStatusPayload>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.StepName, deserialized.StepName);
        Assert.Equal(original.PackageIndex, deserialized.PackageIndex);
        Assert.Equal(original.PackageId, deserialized.PackageId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.Error, deserialized.Error);
    }

    [Fact]
    public void PackageDetectionRequest_RoundTrips_ThroughJson()
    {
        var original = new PackageDetectionRequest
        {
            PackageId = Guid.Parse("eeeeeeee-1111-2222-3333-444444444444"),
            Name = "DetectableApp",
            Version = "4.2.0",
            Detection = new DetectionConfig
            {
                Type = "version_manifest",
                Path = @"C:\Program Files\DetectableApp\app.exe",
                ExpectedVersion = "4.2.0"
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PackageDetectionRequest>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.PackageId, deserialized.PackageId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Detection.Type, deserialized.Detection.Type);
        Assert.Equal(original.Detection.Path, deserialized.Detection.Path);
        Assert.Equal(original.Detection.ExpectedVersion, deserialized.Detection.ExpectedVersion);
    }

    [Fact]
    public void DetectRequest_RoundTrips_ThroughJson()
    {
        var original = new DetectRequest
        {
            Packages =
            [
                new PackageDetectionRequest
                {
                    PackageId = Guid.Parse("aaaa1111-2222-3333-4444-555555555555"),
                    Name = "AppOne",
                    Version = "1.0.0",
                    Detection = new DetectionConfig { Type = "file", Path = @"C:\Apps\one\one.exe" }
                },
                new PackageDetectionRequest
                {
                    PackageId = Guid.Parse("bbbb2222-3333-4444-5555-666666666666"),
                    Name = "AppTwo",
                    Version = "2.0.0",
                    Detection = new DetectionConfig { Type = "registry", Path = @"HKLM:\Software\AppTwo" }
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DetectRequest>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Packages.Count);
        Assert.Equal(original.Packages[0].PackageId, deserialized.Packages[0].PackageId);
        Assert.Equal(original.Packages[0].Name, deserialized.Packages[0].Name);
        Assert.Equal(original.Packages[0].Version, deserialized.Packages[0].Version);
        Assert.Equal(original.Packages[0].Detection.Type, deserialized.Packages[0].Detection.Type);
        Assert.Equal(original.Packages[0].Detection.Path, deserialized.Packages[0].Detection.Path);
        Assert.Equal(original.Packages[1].PackageId, deserialized.Packages[1].PackageId);
        Assert.Equal(original.Packages[1].Name, deserialized.Packages[1].Name);
        Assert.Equal(original.Packages[1].Detection.Type, deserialized.Packages[1].Detection.Type);
    }

    [Fact]
    public void PackageDetectionResult_RoundTrips_ThroughJson()
    {
        var original = new PackageDetectionResult
        {
            PackageId = Guid.Parse("cccc1111-2222-3333-4444-555555555555"),
            Name = "AlreadyInstalledApp",
            Status = PreCheckStatus.AlreadySatisfied,
            ActualVersion = "3.0.0"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PackageDetectionResult>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.PackageId, deserialized.PackageId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.ActualVersion, deserialized.ActualVersion);
    }

    [Fact]
    public void PackageDetectionResult_WrongVersion_RoundTrips_ThroughJson()
    {
        var original = new PackageDetectionResult
        {
            PackageId = Guid.Parse("dddd1111-2222-3333-4444-555555555555"),
            Name = "OutdatedApp",
            Status = PreCheckStatus.WrongVersion,
            ActualVersion = "1.0.0"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PackageDetectionResult>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.PackageId, deserialized.PackageId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(PreCheckStatus.WrongVersion, deserialized.Status);
        Assert.Equal(original.ActualVersion, deserialized.ActualVersion);
    }

    [Fact]
    public void PackageDetectionResult_NotPresent_RoundTrips_ThroughJson()
    {
        var original = new PackageDetectionResult
        {
            PackageId = Guid.Parse("eeee1111-2222-3333-4444-555555555555"),
            Name = "MissingApp",
            Status = PreCheckStatus.NotPresent,
            ActualVersion = null
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PackageDetectionResult>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.PackageId, deserialized.PackageId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(PreCheckStatus.NotPresent, deserialized.Status);
        Assert.Null(deserialized.ActualVersion);
    }

    [Fact]
    public void DiskInfo_RoundTrips_ThroughJson()
    {
        var original = new DiskInfo
        {
            FreeBytes = 500_000_000_000,
            TotalBytes = 1_000_000_000_000,
            Drive = "C:"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DiskInfo>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.FreeBytes, deserialized.FreeBytes);
        Assert.Equal(original.TotalBytes, deserialized.TotalBytes);
        Assert.Equal(original.Drive, deserialized.Drive);
    }

    [Fact]
    public void NodeDetectResponse_RoundTrips_ThroughJson()
    {
        var original = new NodeDetectResponse
        {
            Results =
            [
                new PackageDetectionResult
                {
                    PackageId = Guid.Parse("1111aaaa-2222-3333-4444-555555555555"),
                    Name = "AppOne",
                    Status = PreCheckStatus.AlreadySatisfied,
                    ActualVersion = "1.2.3"
                },
                new PackageDetectionResult
                {
                    PackageId = Guid.Parse("2222bbbb-3333-4444-5555-666666666666"),
                    Name = "AppTwo",
                    Status = PreCheckStatus.NotPresent
                }
            ],
            DiskInfo = new DiskInfo
            {
                FreeBytes = 100_000_000_000,
                TotalBytes = 500_000_000_000,
                Drive = "C:"
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<NodeDetectResponse>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Results.Count);
        Assert.Equal(original.Results[0].PackageId, deserialized.Results[0].PackageId);
        Assert.Equal(original.Results[0].Name, deserialized.Results[0].Name);
        Assert.Equal(original.Results[0].Status, deserialized.Results[0].Status);
        Assert.Equal(original.Results[0].ActualVersion, deserialized.Results[0].ActualVersion);
        Assert.Equal(original.Results[1].PackageId, deserialized.Results[1].PackageId);
        Assert.Equal(original.Results[1].Status, deserialized.Results[1].Status);
        Assert.Null(deserialized.Results[1].ActualVersion);
        Assert.Equal(original.DiskInfo.FreeBytes, deserialized.DiskInfo.FreeBytes);
        Assert.Equal(original.DiskInfo.TotalBytes, deserialized.DiskInfo.TotalBytes);
        Assert.Equal(original.DiskInfo.Drive, deserialized.DiskInfo.Drive);
    }

    [Fact]
    public void InstallAdapterConfig_RoundTrips_ThroughJson()
    {
        var original = new InstallAdapterConfig
        {
            Type = "exe",
            Command = "setup.exe",
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES",
            UninstallCommand = "uninstall.exe",
            UninstallArgs = "/VERYSILENT",
            UpgradeBehavior = "UninstallFirst",
            ExpectedExitCodes = [0, 1641],
            TimeoutSeconds = 1200
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<InstallAdapterConfig>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Command, deserialized.Command);
        Assert.Equal(original.Arguments, deserialized.Arguments);
        Assert.Equal(original.UninstallCommand, deserialized.UninstallCommand);
        Assert.Equal(original.UninstallArgs, deserialized.UninstallArgs);
        Assert.Equal(original.UpgradeBehavior, deserialized.UpgradeBehavior);
        Assert.Equal(original.ExpectedExitCodes, deserialized.ExpectedExitCodes);
        Assert.Equal(original.TimeoutSeconds, deserialized.TimeoutSeconds);
    }

    [Fact]
    public void InstallAdapterConfig_Defaults_RoundTrip_ThroughJson()
    {
        var original = new InstallAdapterConfig();

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<InstallAdapterConfig>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Type);
        Assert.Empty(deserialized.Command);
        Assert.Empty(deserialized.Arguments);
        Assert.Empty(deserialized.UninstallArgs);
        Assert.Empty(deserialized.UninstallCommand);
        Assert.Equal("InPlace", deserialized.UpgradeBehavior);
        Assert.Equal([0], deserialized.ExpectedExitCodes);
        Assert.Equal(300, deserialized.TimeoutSeconds);
    }

    [Fact]
    public void DetectionConfig_RoundTrips_ThroughJson()
    {
        var original = new DetectionConfig
        {
            Type = "version_manifest",
            Path = @"C:\Program Files\MyApp\myapp.exe",
            ExpectedVersion = "5.0.0"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DetectionConfig>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Path, deserialized.Path);
        Assert.Equal(original.ExpectedVersion, deserialized.ExpectedVersion);
    }

    [Fact]
    public void DetectionConfig_WithoutExpectedVersion_RoundTrips_ThroughJson()
    {
        var original = new DetectionConfig
        {
            Type = "file",
            Path = @"C:\Windows\System32\notepad.exe"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DetectionConfig>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Path, deserialized.Path);
        Assert.Null(deserialized.ExpectedVersion);
    }

    [Fact]
    public void AssignRunPayload_RoundTrips_ThroughJson()
    {
        var original = new AssignRunPayload
        {
            RunId = Guid.Parse("f1f1f1f1-1111-2222-3333-444444444444"),
            WorkloadId = Guid.Parse("a2a2a2a2-1111-2222-3333-444444444444"),
            WorkloadName = "FullWorkload",
            RevisionId = Guid.Parse("b3b3b3b3-1111-2222-3333-444444444444"),
            RevisionVersion = "2.1.0",
            Mode = "install",
            NodeId = Guid.Parse("c4c4c4c4-1111-2222-3333-444444444444"),
            DefaultShell = "powershell",
            ForceInstall = true,
            Packages =
            [
                new PackageAssignment
                {
                    PackageIndex = 0,
                    PackageEntityId = Guid.Parse("d5d5d5d5-1111-2222-3333-444444444444"),
                    PackageId = "pkg-main",
                    Name = "MainApp",
                    Version = "2.1.0",
                    Channel = "stable",
                    ArtifactFileName = "mainapp.msi",
                    DownloadUrl = "https://cdn.example.com/mainapp.msi",
                    ExpectedSha256 = "sha256hash1",
                    SizeBytes = 5000000,
                    PreInitSteps = ["pre-check"],
                    PostInitSteps = ["register-services"],
                    InstallAdapter = new InstallAdapterConfig
                    {
                        Type = "msi",
                        Command = "msiexec.exe",
                        Arguments = "/i mainapp.msi /qn",
                        ExpectedExitCodes = [0, 3010],
                        TimeoutSeconds = 600
                    },
                    Detection = new DetectionConfig
                    {
                        Type = "product",
                        Path = "MainApp",
                        ExpectedVersion = "2.1.0"
                    }
                }
            ],
            PreWorkloadSteps = ["backup-config"],
            PostWorkloadSteps = ["restart-services", "verify-health"],
            PreUninstallSteps = ["stop-services"],
            PostUninstallSteps = ["remove-leftovers"],
            CurrentPackages =
            [
                new PackageAssignment
                {
                    PackageIndex = 0,
                    PackageEntityId = Guid.Parse("e6e6e6e6-1111-2222-3333-444444444444"),
                    PackageId = "pkg-main",
                    Name = "MainApp",
                    Version = "2.0.0",
                    Channel = "stable",
                    Detection = new DetectionConfig { Type = "product", Path = "MainApp" }
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<AssignRunPayload>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.RunId, deserialized.RunId);
        Assert.Equal(original.WorkloadId, deserialized.WorkloadId);
        Assert.Equal(original.WorkloadName, deserialized.WorkloadName);
        Assert.Equal(original.RevisionId, deserialized.RevisionId);
        Assert.Equal(original.RevisionVersion, deserialized.RevisionVersion);
        Assert.Equal(original.Mode, deserialized.Mode);
        Assert.Equal(original.NodeId, deserialized.NodeId);
        Assert.Equal(original.DefaultShell, deserialized.DefaultShell);
        Assert.True(deserialized.ForceInstall);
        Assert.Single(deserialized.Packages);
        Assert.Equal(original.Packages[0].PackageIndex, deserialized.Packages[0].PackageIndex);
        Assert.Equal(original.Packages[0].PackageEntityId, deserialized.Packages[0].PackageEntityId);
        Assert.Equal(original.Packages[0].PackageId, deserialized.Packages[0].PackageId);
        Assert.Equal(original.Packages[0].Name, deserialized.Packages[0].Name);
        Assert.Equal(original.Packages[0].Version, deserialized.Packages[0].Version);
        Assert.Equal(original.Packages[0].Channel, deserialized.Packages[0].Channel);
        Assert.Equal(original.Packages[0].ArtifactFileName, deserialized.Packages[0].ArtifactFileName);
        Assert.Equal(original.Packages[0].DownloadUrl, deserialized.Packages[0].DownloadUrl);
        Assert.Equal(original.Packages[0].ExpectedSha256, deserialized.Packages[0].ExpectedSha256);
        Assert.Equal(original.Packages[0].SizeBytes, deserialized.Packages[0].SizeBytes);
        Assert.Equal(original.Packages[0].PreInitSteps, deserialized.Packages[0].PreInitSteps);
        Assert.Equal(original.Packages[0].PostInitSteps, deserialized.Packages[0].PostInitSteps);
        Assert.Equal(original.Packages[0].InstallAdapter.Type, deserialized.Packages[0].InstallAdapter.Type);
        Assert.Equal(original.Packages[0].InstallAdapter.Command, deserialized.Packages[0].InstallAdapter.Command);
        Assert.Equal(original.Packages[0].InstallAdapter.Arguments, deserialized.Packages[0].InstallAdapter.Arguments);
        Assert.Equal(original.Packages[0].InstallAdapter.ExpectedExitCodes, deserialized.Packages[0].InstallAdapter.ExpectedExitCodes);
        Assert.Equal(original.Packages[0].InstallAdapter.TimeoutSeconds, deserialized.Packages[0].InstallAdapter.TimeoutSeconds);
        Assert.Equal(original.Packages[0].Detection.Type, deserialized.Packages[0].Detection.Type);
        Assert.Equal(original.Packages[0].Detection.Path, deserialized.Packages[0].Detection.Path);
        Assert.Equal(original.Packages[0].Detection.ExpectedVersion, deserialized.Packages[0].Detection.ExpectedVersion);
        Assert.Equal(original.PreWorkloadSteps, deserialized.PreWorkloadSteps);
        Assert.Equal(original.PostWorkloadSteps, deserialized.PostWorkloadSteps);
        Assert.Equal(original.PreUninstallSteps, deserialized.PreUninstallSteps);
        Assert.Equal(original.PostUninstallSteps, deserialized.PostUninstallSteps);
        Assert.Single(deserialized.CurrentPackages);
        Assert.Equal(original.CurrentPackages[0].PackageId, deserialized.CurrentPackages[0].PackageId);
        Assert.Equal(original.CurrentPackages[0].Version, deserialized.CurrentPackages[0].Version);
    }

    [Fact]
    public void PackageAssignment_RoundTrips_ThroughJson()
    {
        var original = new PackageAssignment
        {
            PackageIndex = 3,
            PackageEntityId = Guid.Parse("55551111-2222-3333-4444-555555555555"),
            PackageId = "standalone-pkg",
            Name = "Standalone",
            Version = "7.0.1",
            Channel = "beta",
            ArtifactFileName = "standalone.zip",
            DownloadUrl = "https://cdn.example.com/standalone.zip",
            ExpectedSha256 = "standalone-sha256",
            SizeBytes = 10000000,
            PreInitSteps = ["extract", "validate"],
            PostInitSteps = ["configure", "start-service"],
            InstallAdapter = new InstallAdapterConfig
            {
                Type = "zip",
                Command = "powershell.exe",
                Arguments = "-File deploy.ps1",
                UninstallCommand = "powershell.exe",
                UninstallArgs = "-File uninstall.ps1",
                UpgradeBehavior = "UninstallFirst",
                ExpectedExitCodes = [0],
                TimeoutSeconds = 300
            },
            Detection = new DetectionConfig
            {
                Type = "version_manifest",
                Path = @"C:\Program Files\Standalone\app.exe",
                ExpectedVersion = "7.0.1"
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PackageAssignment>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.PackageIndex, deserialized.PackageIndex);
        Assert.Equal(original.PackageEntityId, deserialized.PackageEntityId);
        Assert.Equal(original.PackageId, deserialized.PackageId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Channel, deserialized.Channel);
        Assert.Equal(original.ArtifactFileName, deserialized.ArtifactFileName);
        Assert.Equal(original.DownloadUrl, deserialized.DownloadUrl);
        Assert.Equal(original.ExpectedSha256, deserialized.ExpectedSha256);
        Assert.Equal(original.SizeBytes, deserialized.SizeBytes);
        Assert.Equal(original.PreInitSteps, deserialized.PreInitSteps);
        Assert.Equal(original.PostInitSteps, deserialized.PostInitSteps);
        Assert.Equal(original.InstallAdapter.Type, deserialized.InstallAdapter.Type);
        Assert.Equal(original.InstallAdapter.Command, deserialized.InstallAdapter.Command);
        Assert.Equal(original.InstallAdapter.Arguments, deserialized.InstallAdapter.Arguments);
        Assert.Equal(original.InstallAdapter.UninstallCommand, deserialized.InstallAdapter.UninstallCommand);
        Assert.Equal(original.InstallAdapter.UninstallArgs, deserialized.InstallAdapter.UninstallArgs);
        Assert.Equal(original.InstallAdapter.UpgradeBehavior, deserialized.InstallAdapter.UpgradeBehavior);
        Assert.Equal(original.InstallAdapter.ExpectedExitCodes, deserialized.InstallAdapter.ExpectedExitCodes);
        Assert.Equal(original.InstallAdapter.TimeoutSeconds, deserialized.InstallAdapter.TimeoutSeconds);
        Assert.Equal(original.Detection.Type, deserialized.Detection.Type);
        Assert.Equal(original.Detection.Path, deserialized.Detection.Path);
        Assert.Equal(original.Detection.ExpectedVersion, deserialized.Detection.ExpectedVersion);
    }

    [Fact]
    public void MessageEnvelope_RoundTrips_ThroughJson()
    {
        var original = new MessageEnvelope
        {
            MessageType = MessageTypes.AssignRun,
            AssignmentId = "assignment-123",
            LeaseId = "lease-456",
            RunId = "run-789",
            AgentId = "agent-001",
            Sequence = 7
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<MessageEnvelope>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.MessageType, deserialized.MessageType);
        Assert.Equal(original.ProtocolVersion, deserialized.ProtocolVersion);
        Assert.Equal(original.MessageId, deserialized.MessageId);
        Assert.Equal(original.AssignmentId, deserialized.AssignmentId);
        Assert.Equal(original.LeaseId, deserialized.LeaseId);
        Assert.Equal(original.RunId, deserialized.RunId);
        Assert.Equal(original.AgentId, deserialized.AgentId);
        Assert.Equal(original.Sequence, deserialized.Sequence);
    }

    [Fact]
    public void MessageEnvelope_WithAllNullables_RoundTrips_ThroughJson()
    {
        var original = new MessageEnvelope
        {
            MessageType = MessageTypes.Complete,
            Sequence = 42,
            AssignmentId = null,
            LeaseId = null,
            RunId = null,
            AgentId = null
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<MessageEnvelope>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.MessageType, deserialized.MessageType);
        Assert.Null(deserialized.AssignmentId);
        Assert.Null(deserialized.LeaseId);
        Assert.Null(deserialized.RunId);
        Assert.Null(deserialized.AgentId);
        Assert.Equal(original.Sequence, deserialized.Sequence);
    }

    [Fact]
    public void MessageTypes_HaveExpectedConstantValues()
    {
        Assert.Equal("AssignRun", MessageTypes.AssignRun);
        Assert.Equal("AckClaim", MessageTypes.AckClaim);
        Assert.Equal("LeaseHeartbeat", MessageTypes.LeaseHeartbeat);
        Assert.Equal("StepStatus", MessageTypes.StepStatus);
        Assert.Equal("Complete", MessageTypes.Complete);
        Assert.Equal("Fail", MessageTypes.Fail);
        Assert.Equal("LeaseClose", MessageTypes.LeaseClose);
    }

    [Fact]
    public void WorkloadAssignmentStatus_HaveExpectedConstantValues()
    {
        Assert.Equal("Current", WorkloadAssignmentStatus.Current);
        Assert.Equal("Drifted", WorkloadAssignmentStatus.Drifted);
        Assert.Equal("Unknown", WorkloadAssignmentStatus.Unknown);
    }

    [Fact]
    public void PreCheckStatus_SerializesAsInteger()
    {
        var original = PreCheckStatus.WrongVersion;
        var json = JsonSerializer.Serialize(original, Options);

        Assert.Equal("1", json);

        var deserialized = JsonSerializer.Deserialize<PreCheckStatus>(json, Options);
        Assert.Equal(PreCheckStatus.WrongVersion, deserialized);
    }

    [Fact]
    public void PreCheckStatus_AllValues_RoundTrip()
    {
        var values = new[] { PreCheckStatus.AlreadySatisfied, PreCheckStatus.WrongVersion, PreCheckStatus.NotPresent };

        foreach (var original in values)
        {
            var json = JsonSerializer.Serialize(original, Options);
            var deserialized = JsonSerializer.Deserialize<PreCheckStatus>(json, Options);
            Assert.Equal(original, deserialized);
        }
    }

    [Fact]
    public void PendingWorkloadRunResponse_HandlesNullPackages()
    {
        var original = new PendingWorkloadRunResponse
        {
            RunId = Guid.NewGuid(),
            WorkloadId = Guid.NewGuid(),
            WorkloadName = "EmptyWorkload",
            Mode = "Uninstall",
            Packages = [],
            CurrentPackages = []
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<PendingWorkloadRunResponse>(json, Options);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Packages);
        Assert.Empty(deserialized.Packages);
        Assert.NotNull(deserialized.CurrentPackages);
        Assert.Empty(deserialized.CurrentPackages);
    }

    [Fact]
    public void DetectRequest_HandlesEmptyPackages()
    {
        var original = new DetectRequest
        {
            Packages = []
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<DetectRequest>(json, Options);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Packages);
        Assert.Empty(deserialized.Packages);
    }

    [Fact]
    public void AssignRunPayload_NullableFields_SerializeAsOmitted()
    {
        var payload = new AssignRunPayload
        {
            RunId = Guid.NewGuid(),
            WorkloadId = Guid.NewGuid(),
            WorkloadName = "Minimal",
            RevisionId = Guid.NewGuid(),
            RevisionVersion = "1.0.0",
            NodeId = Guid.NewGuid()
        };

        var json = JsonSerializer.Serialize(payload, Options);

        Assert.NotNull(json);
    }

    [Fact]
    public void FinalizationPayload_NullableFields_SerializeAsOmitted()
    {
        var payload = new FinalizationPayload
        {
            Result = "Done",
            StepCount = 1
        };

        var json = JsonSerializer.Serialize(payload, Options);

        Assert.NotNull(json);
    }
}
