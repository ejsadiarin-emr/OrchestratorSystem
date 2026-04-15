using System.Linq;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Persistence;

public class InstallerDbContextShapeTests
{
    private static (InstallerDbContext Context, SqliteConnection Connection) CreateSqliteInMemoryContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new InstallerDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    [Test]
    public void InstallerDbContext_ExposesCanonicalEntitySets()
    {
        var dbSetNames = typeof(InstallerDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.Name.StartsWith("DbSet"))
            .Select(p => p.Name)
            .ToHashSet();

        Assert.That(dbSetNames, Does.Contain("Jobs"));
        Assert.That(dbSetNames, Does.Contain("JobSteps"));
        Assert.That(dbSetNames, Does.Contain("Nodes"));
        Assert.That(dbSetNames, Does.Contain("AssignmentLeases"));
        Assert.That(dbSetNames, Does.Contain("ConfigSnapshots"));
    }

    [Test]
    public void InstallerDbContext_Model_HasExpectedUniqueIndexes()
    {
        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new InstallerDbContext(options);
        var model = context.Model;

        var nodeEntity = model.FindEntityType(typeof(NodeEntity));
        Assert.That(nodeEntity, Is.Not.Null);
        Assert.That(
            nodeEntity!.GetIndexes().Any(i =>
                i.IsUnique &&
                i.Properties.Count == 1 &&
                i.Properties[0].Name == nameof(NodeEntity.Hostname)),
            Is.True);

        var leaseEntity = model.FindEntityType(typeof(AssignmentLeaseEntity));
        Assert.That(leaseEntity, Is.Not.Null);
        Assert.That(
            leaseEntity!.GetIndexes().Any(i =>
                i.IsUnique &&
                i.Properties.Count == 1 &&
                i.Properties[0].Name == nameof(AssignmentLeaseEntity.LeaseId)),
            Is.True);

        var stepEntity = model.FindEntityType(typeof(JobStepEntity));
        Assert.That(stepEntity, Is.Not.Null);
        Assert.That(
            stepEntity!.GetIndexes().Any(i =>
                i.IsUnique &&
                i.Properties.Count == 2 &&
                i.Properties[0].Name == nameof(JobStepEntity.JobId) &&
                i.Properties[1].Name == nameof(JobStepEntity.Sequence)),
            Is.True);
    }

    [Test]
    public void InstallerDbContext_Model_HasExpectedLeaseAndSnapshotForeignKeys()
    {
        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new InstallerDbContext(options);
        var model = context.Model;

        var leaseEntity = model.FindEntityType(typeof(AssignmentLeaseEntity));
        Assert.That(leaseEntity, Is.Not.Null);
        Assert.That(
            leaseEntity!.GetForeignKeys().Any(fk =>
                fk.PrincipalEntityType.ClrType == typeof(JobEntity) &&
                fk.Properties.Count == 1 &&
                fk.Properties[0].Name == nameof(AssignmentLeaseEntity.JobId)),
            Is.True);

        var snapshotEntity = model.FindEntityType(typeof(ConfigSnapshotEntity));
        Assert.That(snapshotEntity, Is.Not.Null);
        Assert.That(
            snapshotEntity!.GetForeignKeys().Any(fk =>
                fk.PrincipalEntityType.ClrType == typeof(JobEntity) &&
                fk.Properties.Count == 1 &&
                fk.Properties[0].Name == nameof(ConfigSnapshotEntity.JobId)),
            Is.True);
        Assert.That(
            snapshotEntity.GetForeignKeys().Any(fk =>
                fk.PrincipalEntityType.ClrType == typeof(NodeEntity) &&
                fk.Properties.Count == 1 &&
                fk.Properties[0].Name == nameof(ConfigSnapshotEntity.NodeId)),
            Is.True);
    }

    [Test]
    public void InstallerDbContext_Persistence_DuplicateNodeHostname_Fails()
    {
        var (context, connection) = CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;

        context.Nodes.Add(new NodeEntity { Hostname = "node-01" });
        context.Nodes.Add(new NodeEntity { Hostname = "node-01" });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Test]
    public void InstallerDbContext_Persistence_DuplicateJobStepSequencePerJob_Fails()
    {
        var (context, connection) = CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;

        var job = new JobEntity
        {
            Mode = "install",
            State = "Queued",
            ManifestPackageId = "pkg",
            ManifestTargetVersion = "1.0.0",
            TargetNodeIdsCsv = "node-01"
        };

        context.Jobs.Add(job);
        context.JobSteps.Add(new JobStepEntity
        {
            Job = job,
            Sequence = 1,
            StepId = "step-1",
            Name = "Step 1",
            Status = "Pending"
        });
        context.JobSteps.Add(new JobStepEntity
        {
            Job = job,
            Sequence = 1,
            StepId = "step-2",
            Name = "Step 2",
            Status = "Pending"
        });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Test]
    public void InstallerDbContext_Persistence_DeletingJob_CascadesToJobStepsAndAssignmentLeases()
    {
        var (context, connection) = CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;

        var job = new JobEntity
        {
            Mode = "install",
            State = "Queued",
            ManifestPackageId = "pkg",
            ManifestTargetVersion = "1.0.0",
            TargetNodeIdsCsv = "node-01"
        };

        context.Jobs.Add(job);
        context.JobSteps.Add(new JobStepEntity
        {
            Job = job,
            Sequence = 1,
            StepId = "step-1",
            Name = "Step 1",
            Status = "Pending"
        });
        context.AssignmentLeases.Add(new AssignmentLeaseEntity
        {
            Job = job,
            AgentId = "agent-1",
            LeaseId = "lease-1",
            TtlSeconds = 90,
            LastAckedSequence = 0,
            State = "Assigned"
        });

        context.SaveChanges();

        context.Jobs.Remove(job);
        context.SaveChanges();

        Assert.That(context.JobSteps.Count(), Is.EqualTo(0));
        Assert.That(context.AssignmentLeases.Count(), Is.EqualTo(0));
    }

    [Test]
    public void InstallerDbContext_Persistence_InvalidJobMode_Fails()
    {
        var (context, connection) = CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;

        context.Jobs.Add(new JobEntity
        {
            Mode = "unsupported",
            State = "Queued",
            ManifestPackageId = "pkg",
            ManifestTargetVersion = "1.0.0",
            TargetNodeIdsCsv = "node-01"
        });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Test]
    public void InstallerDbContext_Persistence_NonPositiveLeaseTtl_Fails()
    {
        var (context, connection) = CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;

        var job = new JobEntity
        {
            Mode = "install",
            State = "Queued",
            ManifestPackageId = "pkg",
            ManifestTargetVersion = "1.0.0",
            TargetNodeIdsCsv = "node-01"
        };

        context.Jobs.Add(job);
        context.AssignmentLeases.Add(new AssignmentLeaseEntity
        {
            Job = job,
            AgentId = "agent-1",
            LeaseId = "lease-1",
            TtlSeconds = 0,
            LastAckedSequence = 0,
            State = "Assigned"
        });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Test]
    public void InstallerDbContext_Persistence_NegativeLastAckedSequence_Fails()
    {
        var (context, connection) = CreateSqliteInMemoryContext();
        using var _ = connection;
        using var __ = context;

        var job = new JobEntity
        {
            Mode = "install",
            State = "Queued",
            ManifestPackageId = "pkg",
            ManifestTargetVersion = "1.0.0",
            TargetNodeIdsCsv = "node-01"
        };

        context.Jobs.Add(job);
        context.AssignmentLeases.Add(new AssignmentLeaseEntity
        {
            Job = job,
            AgentId = "agent-1",
            LeaseId = "lease-1",
            TtlSeconds = 90,
            LastAckedSequence = -1,
            State = "Assigned"
        });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }
}
