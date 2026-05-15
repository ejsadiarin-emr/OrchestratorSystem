using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using NUnit.Framework;

namespace DeploymentPoC.Agent.Tests;

public sealed class DiffEngineTests
{
    [Test]
    public void ComputeDiff_AddedPackages_ArePresentInTargetNotInCurrent()
    {
        var current = new List<PackageAssignment>();
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.That(diff.Added, Has.Count.EqualTo(1));
        Assert.That(diff.Added[0].Name, Is.EqualTo("pkg-a"));
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
    public void ComputeDiff_RemovedPackages_ArePresentInCurrentNotInTarget()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" }
        };
        var target = new List<PackageAssignment>();

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.That(diff.Removed, Has.Count.EqualTo(1));
        Assert.That(diff.Removed[0].Name, Is.EqualTo("pkg-a"));
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
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

        Assert.That(diff.Changed, Has.Count.EqualTo(1));
        Assert.That(diff.Changed[0].Name, Is.EqualTo("pkg-a"));
        Assert.That(diff.Changed[0].Version, Is.EqualTo("2.0.0"));
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
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

        Assert.That(diff.Unchanged, Has.Count.EqualTo(1));
        Assert.That(diff.Unchanged[0].Name, Is.EqualTo("pkg-a"));
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Changed, Is.Empty);
    }

    [Test]
    public void ComputeDiff_EmptyCurrent_AllTargetTreatedAsAdded()
    {
        var current = new List<PackageAssignment>();
        var target = new List<PackageAssignment>
        {
            new() { Name = "pkg-a", Version = "1.0.0" },
            new() { Name = "pkg-b", Version = "2.0.0" }
        };

        var diff = DiffEngine.ComputeDiff(current, target);

        Assert.That(diff.Added.Count, Is.EqualTo(2));
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
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

        Assert.That(diff.Changed, Has.Count.EqualTo(1));
        Assert.That(diff.Changed[0].PackageId, Is.EqualTo("id-2"));
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
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

        Assert.That(diff.Added, Has.Count.EqualTo(1));
        Assert.That(diff.Added[0].Name, Is.EqualTo("pkg-d"));

        Assert.That(diff.Removed, Has.Count.EqualTo(1));
        Assert.That(diff.Removed[0].Name, Is.EqualTo("pkg-a"));

        Assert.That(diff.Changed, Has.Count.EqualTo(1));
        Assert.That(diff.Changed[0].Name, Is.EqualTo("pkg-b"));

        Assert.That(diff.Unchanged, Has.Count.EqualTo(1));
        Assert.That(diff.Unchanged[0].Name, Is.EqualTo("pkg-c"));
    }

    [Test]
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

        Assert.That(diff.Removed.Count, Is.EqualTo(2));
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
    public void ComputeDiff_UninstallMode_RemainsRemoved()
    {
        var current = new List<PackageAssignment>
        {
            new() { Name = "dbeaver", Version = "24.0.0" }
        };
        var target = new List<PackageAssignment>();

        var diff = DiffEngine.ComputeDiff(current, target, null, "uninstall");

        Assert.That(diff.Removed, Has.Count.EqualTo(1));
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
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

        Assert.That(diff.Changed, Has.Count.EqualTo(1));
        Assert.That(diff.Removed, Is.Empty);
    }

    [Test]
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

        Assert.That(diff.Added, Has.Count.EqualTo(1));
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
        Assert.That(diff.Added[0].Name, Is.EqualTo("python"));
        Assert.That(diff.Added[0].Version, Is.EqualTo("3.14.4"));
    }

    [Test]
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

        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Changed, Has.Count.EqualTo(1));
        Assert.That(diff.Unchanged, Is.Empty);
        Assert.That(diff.Changed[0].Name, Is.EqualTo("python"));
        Assert.That(diff.Changed[0].Version, Is.EqualTo("3.14.4"));
    }

    [Test]
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

        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Changed, Has.Count.EqualTo(1));
        Assert.That(diff.Unchanged, Is.Empty);
        Assert.That(diff.Changed[0].Name, Is.EqualTo("python"));
    }

    [Test]
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

        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Removed, Has.Count.EqualTo(1));
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
    }

    [Test]
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

        Assert.That(diff.Added, Has.Count.EqualTo(1));
        Assert.That(diff.Removed, Is.Empty);
        Assert.That(diff.Changed, Is.Empty);
        Assert.That(diff.Unchanged, Is.Empty);
        Assert.That(diff.Added[0].Name, Is.EqualTo("newpkg"));
    }
}
