using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Xunit;

namespace DeploymentPoC.Agent.Tests;

public sealed class DiffEngineTests
{
    [Fact]
    public void ComputeDiff_AddedPackages_ArePresentInTargetNotInCurrent()
    {
        var current = new List<PackageAssignment>();
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Single(diff.Added);
        Assert.Equal("pkg-a", diff.Added[0].Name);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_RemovedPackages_ArePresentInCurrentNotInTarget()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" }
        };
        var target = new List<PackageAssignment>();

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Single(diff.Removed);
        Assert.Equal("pkg-a", diff.Removed[0].Name);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_ChangedPackages_ArePresentInBothWithDifferentVersion()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "2.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Single(diff.Changed);
        Assert.Equal("pkg-a", diff.Changed[0].Name);
        Assert.Equal("2.0.0", diff.Changed[0].Version);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_UnchangedPackages_AreSkipped()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Single(diff.Unchanged);
        Assert.Equal("pkg-a", diff.Unchanged[0].Name);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void ComputeDiff_EmptyCurrent_AllTargetTreatedAsAdded()
    {
        var current = new List<PackageAssignment>();
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" },
            new() { Name = "pkg-b", Version = "2.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Equal(2, diff.Added.Count);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_DiffersByName_NotPackageId()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", PackageId = "id-1", Version = "1.0.0" }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", PackageId = "id-2", Version = "2.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Single(diff.Changed);
        Assert.Equal("id-2", diff.Changed[0].PackageId);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_MixedScenario_ComputesAllCategories()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" },
            new() { Name = "pkg-b", Version = "1.0.0" },
            new() { Name = "pkg-c", Version = "1.0.0" }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-b", Version = "2.0.0" },
            new() { Name = "pkg-c", Version = "1.0.0" },
            new() { Name = "pkg-d", Version = "1.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Single(diff.Added);
        Assert.Equal("pkg-d", diff.Added[0].Name);

        Assert.Single(diff.Removed);
        Assert.Equal("pkg-a", diff.Removed[0].Name);

        Assert.Single(diff.Changed);
        Assert.Equal("pkg-b", diff.Changed[0].Name);

        Assert.Single(diff.Unchanged);
        Assert.Equal("pkg-c", diff.Unchanged[0].Name);
    }

    [Fact]
    public void ComputeDiff_UninstallMode_ChangedBecomeRemoved()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "dbeaver", Version = "24.0.0" },
            new() { Name = "python", Version = "3.13.0" }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "dbeaver", Version = "26.0.0" },
            new() { Name = "python", Version = "3.14.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target, null, "uninstall");

        Assert.Equal(2, diff.Removed.Count);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_UninstallMode_RemainsRemoved()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "dbeaver", Version = "24.0.0" }
        };
        var target = new List<PackageAssignment>();

        var diff = DiffEngine.ComputeDiff(current, target, null, "uninstall");

        Assert.Single(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_InstallMode_ChangedStayChanged()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "dbeaver", Version = "24.0.0" }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "dbeaver", Version = "26.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target, null, "install");

        Assert.Single(diff.Changed);
        Assert.Empty(diff.Removed);
    }
}
