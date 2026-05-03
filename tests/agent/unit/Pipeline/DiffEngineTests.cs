using Xunit;
using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;

namespace DeploymentPoC.Agent.Tests.Pipeline;

public class DiffEngineTests
{
    [Fact]
    public void ComputeDiff_ChangedPackageNotPresent_PreCheckOverridesToAdded()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "python", Version = "3.13.3", PackageIndex = 0 }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "python", Version = "3.14.4", PackageIndex = 0 }
        };
        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["python"] = new() { Status = PreCheckStatus.NotPresent }
        };

        var diff = DiffEngine.ComputeDiff(current, target, preCheck);

        Assert.Single(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Unchanged);
        Assert.Equal("python", diff.Added[0].Name);
        Assert.Equal("3.14.4", diff.Added[0].Version);
    }

    [Fact]
    public void ComputeDiff_ChangedPackageAlreadySatisfied_RemainsChanged()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "python", Version = "3.13.3", PackageIndex = 0 }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "python", Version = "3.14.4", PackageIndex = 0 }
        };
        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["python"] = new() { Status = PreCheckStatus.AlreadySatisfied }
        };

        var diff = DiffEngine.ComputeDiff(current, target, preCheck);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Single(diff.Changed);
        Assert.Empty(diff.Unchanged);
        Assert.Equal("python", diff.Changed[0].Name);
        Assert.Equal("3.14.4", diff.Changed[0].Version);
    }

    [Fact]
    public void ComputeDiff_ChangedPackageWrongVersion_NoOverride()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "python", Version = "3.13.3", PackageIndex = 0 }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "python", Version = "3.14.4", PackageIndex = 0 }
        };
        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["python"] = new() { Status = PreCheckStatus.WrongVersion, ActualVersion = "3.13.3" }
        };

        var diff = DiffEngine.ComputeDiff(current, target, preCheck);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Single(diff.Changed);
        Assert.Empty(diff.Unchanged);
        Assert.Equal("python", diff.Changed[0].Name);
    }

    [Fact]
    public void ComputeDiff_RemovedPackageNotPresent_SkipsUninstall()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "oldpkg", Version = "1.0", PackageIndex = 0 }
        };
        var target = new List<PackageAssignment>();
        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["oldpkg"] = new() { Status = PreCheckStatus.NotPresent }
        };

        var diff = DiffEngine.ComputeDiff(current, target, preCheck);

        Assert.Empty(diff.Added);
        Assert.Single(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_AddedPackageAlreadySatisfied_RemainsAdded()
    {
        var current = new List<PackageAssignment>();
        var target = new List<PackageAssignment>
        {
            new() { Name = "newpkg", Version = "2.0", PackageIndex = 0 }
        };
        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["newpkg"] = new() { Status = PreCheckStatus.AlreadySatisfied }
        };

        var diff = DiffEngine.ComputeDiff(current, target, preCheck);

        Assert.Single(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
        Assert.Empty(diff.Unchanged);
        Assert.Equal("newpkg", diff.Added[0].Name);
    }

    [Fact]
    public void ComputeDiff_NoPreCheck_StandardDiff()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "pkg1", Version = "1.0", PackageIndex = 0 },
            new() { Name = "pkg2", Version = "1.0", PackageIndex = 1 }
        };
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg1", Version = "2.0", PackageIndex = 0 },
            new() { Name = "pkg3", Version = "1.0", PackageIndex = 2 }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.Single(diff.Added);
        Assert.Equal("pkg3", diff.Added[0].Name);
        Assert.Single(diff.Removed);
        Assert.Equal("pkg2", diff.Removed[0].Name);
        Assert.Single(diff.Changed);
        Assert.Equal("pkg1", diff.Changed[0].Name);
        Assert.Empty(diff.Unchanged);
    }
}
