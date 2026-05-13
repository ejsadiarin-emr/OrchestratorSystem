using DeploymentPoC.Agent.Pipeline;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Xunit;

namespace DeploymentPoC.Agent.Tests;

public sealed class ReportGeneratorTests
{
    private static PipelineContext CreateContext(
        string mode = "install",
        bool allSucceeded = true,
        string? error = null)
    {
        var context = new PipelineContext
        {
            Payload = new AssignRunPayload
            {
                RunId = Guid.NewGuid(),
                WorkloadName = "test-workload",
                NodeId = Guid.NewGuid(),
                Mode = mode,
                RevisionVersion = "1.0.0",
                Packages = new List<PackageAssignment>(),
            },
            OrchestratorBaseUrl = "https://test",
            AgentId = "agent-1",
            RunId = Guid.NewGuid().ToString(),
            Sequence = 1,
            PipelineStartUtc = DateTime.UtcNow.AddMinutes(-5),
        };

        return context;
    }

    private static PackageAssignment CreatePackage(
        int index,
        string name,
        string version = "1.0.0",
        string? expectedVersion = null)
    {
        return new PackageAssignment
        {
            PackageIndex = index,
            PackageId = name,
            Name = name,
            Version = version,
            Detection = new DetectionConfig
            {
                Type = "file",
                Path = $"C:\\test\\{name}.exe",
                ExpectedVersion = expectedVersion ?? version,
            },
        };
    }

    [Fact]
    public void Generate_Success_HasAllSections()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["pkg-1"] = new() { Status = PreCheckStatus.NotPresent, ActualVersion = null },
        };
        var postVerify = new Dictionary<string, PostVerifyResult>
        {
            ["pkg-1"] = new() { Success = true, ActualVersion = "1.0.0" },
        };

        var report = ReportGenerator.Generate(context, preCheck, postVerify);

        Assert.Contains("=== Deployment Report ===", report);
        Assert.Contains("Result:       SUCCESS", report);
        Assert.Contains("--- Pre-Run Detection ---", report);
        Assert.Contains("--- Post-Run Detection ---", report);
        Assert.Contains("--- Run Summary ---", report);
        Assert.Contains("--- Step Timeline ---", report);
    }

    [Fact]
    public void Generate_Failure_ShowsFailedInHeaderAndTimeline()
    {
        var context = CreateContext(allSucceeded: false, error: "install_failed");
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, false, "install_failed");

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("Result:       FAILED [install_failed]", report);
        Assert.Contains("Failed", report);
    }

    [Fact]
    public void Generate_NullPreCheck_ShowsForceInstall()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("ForceInstall", report);
        Assert.Contains("(skipped)", report);
    }

    [Fact]
    public void Generate_PackageNotReached_ShowsNotReachedInPostRun()
    {
        var context = CreateContext(allSucceeded: false, error: "install_failed");
        var pkg1 = CreatePackage(0, "pkg-1");
        var pkg2 = CreatePackage(1, "pkg-2");
        context.Payload.Packages.Add(pkg1);
        context.Payload.Packages.Add(pkg2);
        context.RecordStep("PreCheckProbe", 0, pkg1.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg1.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg1.PackageId, false, "install_failed");

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("(not reached)", report);
    }

    [Fact]
    public void Generate_EmptyPackageList_ShowsZeroCount()
    {
        var context = CreateContext();

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("Packages processed:  0", report);
        Assert.Contains("Installed:         0", report);
        Assert.Contains("Failed/Skipped:    0", report);
    }

    [Fact]
    public void Generate_InstalledCount_WhenNotPresent()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["pkg-1"] = new() { Status = PreCheckStatus.NotPresent },
        };

        var report = ReportGenerator.Generate(context, preCheck, null);

        Assert.Contains("Installed:         1", report);
        Assert.Contains("Updated:           0", report);
    }

    [Fact]
    public void Generate_UpdatedCount_WhenAlreadySatisfied()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["pkg-1"] = new() { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
        };

        var report = ReportGenerator.Generate(context, preCheck, null);

        Assert.Contains("Updated:           1", report);
        Assert.Contains("Installed:         0", report);
    }

    [Fact]
    public void Generate_UpdatedCount_WhenWrongVersion()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["pkg-1"] = new() { Status = PreCheckStatus.WrongVersion, ActualVersion = "0.9.0" },
        };

        var report = ReportGenerator.Generate(context, preCheck, null);

        Assert.Contains("Updated:           1", report);
    }

    [Fact]
    public void Generate_UninstalledCount_WhenUninstallMode()
    {
        var context = CreateContext(mode: "uninstall");
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("UninstallPackage", 0, pkg.PackageId, true);

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("Uninstalled:       1", report);
    }

    [Fact]
    public void Generate_FailedCount_WhenInstallFails()
    {
        var context = CreateContext(allSucceeded: false, error: "install_failed");
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, false, "install_failed");

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("Failed/Skipped:    1", report);
    }

    [Fact]
    public void Generate_TruncatesLongName()
    {
        var context = CreateContext();
        var longName = "very-long-package-name-that-exceeds-limit";
        var pkg = CreatePackage(0, longName);
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("very-long-pac...", report);
    }

    [Fact]
    public void Generate_TruncatesLongVersion()
    {
        var context = CreateContext();
        var longVersion = "123456789.10.11-beta";
        var pkg = CreatePackage(0, "pkg-1");
        pkg.Detection.ExpectedVersion = longVersion;
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var report = ReportGenerator.Generate(context, null, null);

        Assert.DoesNotContain(longVersion, report);
        Assert.Contains("123456789...", report);
    }

    [Fact]
    public void Generate_PreCheckStatusMapped_AlreadySatisfied()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["pkg-1"] = new() { Status = PreCheckStatus.AlreadySatisfied, ActualVersion = "1.0.0" },
        };

        var report = ReportGenerator.Generate(context, preCheck, null);

        Assert.Contains("AlreadySatisfied", report);
    }

    [Fact]
    public void Generate_PreCheckStatusMapped_WrongVersion()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["pkg-1"] = new() { Status = PreCheckStatus.WrongVersion, ActualVersion = "0.9.0" },
        };

        var report = ReportGenerator.Generate(context, preCheck, null);

        Assert.Contains("WrongVersion", report);
    }

    [Fact]
    public void Generate_PreCheckStatusMapped_NotPresent()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var preCheck = new Dictionary<string, PreCheckResult>
        {
            ["pkg-1"] = new() { Status = PreCheckStatus.NotPresent },
        };

        var report = ReportGenerator.Generate(context, preCheck, null);

        Assert.Contains("NotPresent", report);
    }

    [Fact]
    public void Generate_PostVerifySuccess_ShowsVersion()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var postVerify = new Dictionary<string, PostVerifyResult>
        {
            ["pkg-1"] = new() { Success = true, ActualVersion = "2.5.0" },
        };

        var report = ReportGenerator.Generate(context, null, postVerify);

        Assert.Contains("2.5.0", report);
        Assert.Contains("AlreadySatisfied", report);
    }

    [Fact]
    public void Generate_PostVerifyFailure_ShowsError()
    {
        var context = CreateContext(allSucceeded: false, error: "verify_failed");
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, false, "verify_failed");

        var postVerify = new Dictionary<string, PostVerifyResult>
        {
            ["pkg-1"] = new() { Success = false, Error = "version mismatch" },
        };

        var report = ReportGenerator.Generate(context, null, postVerify);

        Assert.Contains("NotDetected", report);
        Assert.Contains("version mismatch", report);
    }

    [Fact]
    public void Generate_PostVerifyNull_ShowsAssumedInstalledOnSuccess()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("AssumedInstalled", report);
    }

    [Fact]
    public void Generate_TimelineShowsAllStepRecords()
    {
        var context = CreateContext();
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, true);
        context.RecordStep("PostInstallVerify", 0, pkg.PackageId, true);

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("PreCheckProbe", report);
        Assert.Contains("AcquireArtifact", report);
        Assert.Contains("InstallOrUpgrade", report);
        Assert.Contains("PostInstallVerify", report);
        Assert.Contains("Completed", report);
    }

    [Fact]
    public void Generate_TimelineShowsFailedStepWithDetail()
    {
        var context = CreateContext(allSucceeded: false, error: "disk full");
        var pkg = CreatePackage(0, "pkg-1");
        context.Payload.Packages.Add(pkg);
        context.RecordStep("PreCheckProbe", 0, pkg.PackageId, true);
        context.RecordStep("AcquireArtifact", 0, pkg.PackageId, true);
        context.RecordStep("InstallOrUpgrade", 0, pkg.PackageId, false, "disk full");

        var report = ReportGenerator.Generate(context, null, null);

        Assert.Contains("Failed", report);
        Assert.Contains("disk full", report);
    }
}
