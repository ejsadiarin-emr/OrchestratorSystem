using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Xunit;

namespace DeploymentPoC.Contracts.Tests;

public class AssignRunPayloadTests
{
    [Fact]
    public void AssignRunPayload_ShouldHaveRequiredWorkloadMetadata()
    {
        // Arrange & Act
        var payload = new AssignRunPayload
        {
            RunId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            WorkloadId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
            WorkloadName = "TestWorkload",
            RevisionId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012"),
            RevisionVersion = "1.0.0",
            Mode = "install",
            NodeId = Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123"),
        };

        // Assert
        Assert.Equal(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), payload.RunId);
        Assert.Equal(Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"), payload.WorkloadId);
        Assert.Equal("TestWorkload", payload.WorkloadName);
        Assert.Equal(Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012"), payload.RevisionId);
        Assert.Equal("1.0.0", payload.RevisionVersion);
        Assert.Equal("install", payload.Mode);
        Assert.Equal(Guid.Parse("d4e5f6a7-b8c9-0123-defa-234567890123"), payload.NodeId);
        Assert.Empty(payload.Packages);
        Assert.Empty(payload.PreWorkloadSteps);
    }

    [Fact]
    public void AssignRunPayload_WithPackages_ShouldPreserveOrder()
    {
        // Arrange
        var payload = new AssignRunPayload
        {
            RunId = Guid.NewGuid(),
            WorkloadId = Guid.NewGuid(),
            WorkloadName = "OrderedWorkload",
            RevisionId = Guid.NewGuid(),
            RevisionVersion = "2.0.0",
            Mode = "update",
            NodeId = Guid.NewGuid(),
            Packages =
            [
                new PackageAssignment
                {
                    PackageIndex = 0,
                    PackageId = "pkg-a",
                    Version = "1.0.0",
                    Channel = "stable",
                    InstallAdapter = new InstallAdapterConfig
                    {
                        Type = "msi",
                        Command = "msiexec.exe",
                        Arguments = "/i pkg-a.msi /qn",
                        ExpectedExitCodes = [0, 3010],
                        TimeoutSeconds = 600
                    },
                    Detection = new DetectionConfig
                    {
                        Type = "product",
                        Path = "Product A"
                    }
                },
                new PackageAssignment
                {
                    PackageIndex = 1,
                    PackageId = "pkg-b",
                    Version = "2.1.0",
                    Channel = "stable",
                    InstallAdapter = new InstallAdapterConfig
                    {
                        Type = "exe",
                        Command = "pkg-b-setup.exe",
                        Arguments = "/SILENT",
                        ExpectedExitCodes = [0],
                        TimeoutSeconds = 300
                    },
                    Detection = new DetectionConfig
                    {
                        Type = "file",
                        Path = @"C:\Program Files\PkgB\bin\pkg-b.exe"
                    }
                }
            ]
        };

        // Act & Assert
        Assert.Equal(2, payload.Packages.Count);
        Assert.Equal(0, payload.Packages[0].PackageIndex);
        Assert.Equal("pkg-a", payload.Packages[0].PackageId);
        Assert.Equal("msi", payload.Packages[0].InstallAdapter.Type);
        Assert.Equal(2, payload.Packages[0].InstallAdapter.ExpectedExitCodes.Count);
        Assert.Equal(3010, payload.Packages[0].InstallAdapter.ExpectedExitCodes[1]);

        Assert.Equal(1, payload.Packages[1].PackageIndex);
        Assert.Equal("exe", payload.Packages[1].InstallAdapter.Type);
        Assert.Equal("file", payload.Packages[1].Detection.Type);
    }

    [Fact]
    public void InstallAdapterConfig_ShouldHaveSensibleDefaults()
    {
        // Arrange & Act
        var config = new InstallAdapterConfig();

        // Assert
        Assert.Empty(config.Type);
        Assert.Empty(config.Command);
        Assert.Empty(config.Arguments);
        Assert.Single(config.ExpectedExitCodes);
        Assert.Equal(0, config.ExpectedExitCodes[0]);
        Assert.Equal(300, config.TimeoutSeconds);
    }

    [Fact]
    public void AssignRunPayload_PostWorkloadSteps_ShouldDefaultToEmpty()
    {
        var payload = new AssignRunPayload();
        Assert.Empty(payload.PostWorkloadSteps);
    }

    [Fact]
    public void AssignRunPayload_DefaultShell_ShouldDefaultToPowershell()
    {
        var payload = new AssignRunPayload();
        Assert.Equal("powershell", payload.DefaultShell);
    }

    [Fact]
    public void AssignRunPayload_WithPostWorkloadSteps_ShouldIncludeThem()
    {
        var payload = new AssignRunPayload
        {
            PostWorkloadSteps = ["cleanup", "restart"]
        };
        Assert.Equal(2, payload.PostWorkloadSteps.Count);
        Assert.Equal("cleanup", payload.PostWorkloadSteps[0]);
        Assert.Equal("restart", payload.PostWorkloadSteps[1]);
    }

    [Fact]
    public void PackageAssignment_PreInitSteps_ShouldDefaultToEmpty()
    {
        var pkg = new PackageAssignment();
        Assert.Empty(pkg.PreInitSteps);
    }

    [Fact]
    public void PackageAssignment_PostInitSteps_ShouldDefaultToEmpty()
    {
        var pkg = new PackageAssignment();
        Assert.Empty(pkg.PostInitSteps);
    }

    [Fact]
    public void DetectionConfig_ShouldHaveEmptyDefaults()
    {
        // Arrange & Act
        var config = new DetectionConfig();

        // Assert
        Assert.Empty(config.Type);
        Assert.Empty(config.Path);
    }

    [Fact]
    public void AssignRunPayload_WithPreWorkloadSteps_ShouldIncludeThem()
    {
        // Arrange
        var payload = new AssignRunPayload
        {
            RunId = Guid.NewGuid(),
            WorkloadId = Guid.NewGuid(),
            WorkloadName = "WithPreSteps",
            RevisionId = Guid.NewGuid(),
            RevisionVersion = "1.0.0",
            Mode = "install",
            NodeId = Guid.NewGuid(),
            PreWorkloadSteps = ["backup", "stop"]
        };

        // Assert
        Assert.Equal(2, payload.PreWorkloadSteps.Count);
        Assert.Equal("backup", payload.PreWorkloadSteps[0]);
        Assert.Equal("stop", payload.PreWorkloadSteps[1]);
    }
}
