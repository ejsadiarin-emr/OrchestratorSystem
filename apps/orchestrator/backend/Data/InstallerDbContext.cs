using DeploymentPoC.Orchestrator.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeploymentPoC.Orchestrator.Data;

public sealed class InstallerDbContext : DbContext
{
    public InstallerDbContext(DbContextOptions<InstallerDbContext> options) : base(options)
    {
    }

    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<JobStepEntity> JobSteps => Set<JobStepEntity>();
    public DbSet<NodeEntity> Nodes => Set<NodeEntity>();
    public DbSet<PackageEntity> Packages => Set<PackageEntity>();
    public DbSet<AssignmentLeaseEntity> AssignmentLeases => Set<AssignmentLeaseEntity>();
    public DbSet<ConfigSnapshotEntity> ConfigSnapshots => Set<ConfigSnapshotEntity>();
    public DbSet<WorkloadDefinitionEntity> WorkloadDefinitions => Set<WorkloadDefinitionEntity>();
    public DbSet<WorkloadRevisionEntity> WorkloadRevisions => Set<WorkloadRevisionEntity>();
    public DbSet<WorkloadPackageEntity> WorkloadPackages => Set<WorkloadPackageEntity>();
    public DbSet<WorkloadRunEntity> WorkloadRuns => Set<WorkloadRunEntity>();
    public DbSet<NodeWorkloadStateEntity> NodeWorkloadStates => Set<NodeWorkloadStateEntity>();
    public DbSet<EnrollmentTokenEntity> EnrollmentTokens => Set<EnrollmentTokenEntity>();
    public DbSet<WorkloadRunTimelineEntity> WorkloadRunTimelines => Set<WorkloadRunTimelineEntity>();

    public override int SaveChanges()
    {
        EnforceWorkloadRevisionImmutability();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceWorkloadRevisionImmutability();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.HasKey(x => x.JobId);
            entity.Property(x => x.State).HasMaxLength(64);
            entity.Property(x => x.Mode).HasMaxLength(32);
            entity.Property(x => x.ManifestPackageId).HasMaxLength(128);
            entity.Property(x => x.ManifestTargetVersion).HasMaxLength(64);
            entity.Property(x => x.TargetNodeIdsCsv).HasMaxLength(2048);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.IdempotencyRequestHash).HasMaxLength(64);
            entity.Property(x => x.CancelReason).HasMaxLength(512);
            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique();
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Jobs_Mode", "\"Mode\" IN ('install','upgrade','rollback','modify','cancel')");
                t.HasCheckConstraint("CK_Jobs_State", "\"State\" IN ('Queued','Running','Completed','Failed','Cancelled')");
            });
        });

        modelBuilder.Entity<JobStepEntity>(entity =>
        {
            entity.HasKey(x => x.JobStepId);
            entity.HasOne(x => x.Job)
                .WithMany(x => x.Steps)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.JobId, x.Sequence })
                .IsUnique();
            entity.Property(x => x.StepId).HasMaxLength(128);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Status).HasMaxLength(64);
            entity.Property(x => x.TelemetryRef).HasMaxLength(256);
            entity.Property(x => x.Detail).HasMaxLength(2048);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_JobSteps_Status", "\"Status\" IN ('Pending','Running','Completed','Failed','Cancelled')");
            });
        });

        modelBuilder.Entity<NodeEntity>(entity =>
        {
            entity.HasKey(x => x.NodeId);
            entity.HasIndex(x => x.Hostname).IsUnique();
            entity.Property(x => x.AgentId).HasMaxLength(128);
            entity.Property(x => x.Hostname).HasMaxLength(255);
            entity.Property(x => x.DisplayName).HasMaxLength(255);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.Description).HasMaxLength(512);
            entity.Property(x => x.AgentVersion).HasMaxLength(64);
            entity.Property(x => x.OsVersion).HasMaxLength(255);
            entity.Property(x => x.Status).HasMaxLength(64);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Nodes_Status", "\"Status\" IN ('Offline','Online')");
            });
        });

        modelBuilder.Entity<EnrollmentTokenEntity>(entity =>
        {
            entity.HasKey(x => x.TokenId);
            entity.HasIndex(x => x.Token).IsUnique();
            entity.Property(x => x.Token).HasMaxLength(128);
            entity.Property(x => x.RequestedBy).HasMaxLength(255);
            entity.Property(x => x.OrchestratorUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<PackageEntity>(entity =>
        {
            entity.HasKey(x => x.PackageId);
            entity.Property(x => x.Name).HasMaxLength(255);
            entity.Property(x => x.Version).HasMaxLength(64);
            entity.Property(x => x.SourcePath).HasMaxLength(1024);
            entity.Property(x => x.InstallType).HasMaxLength(64);
            entity.Property(x => x.InstallArgs).HasMaxLength(2048);
            entity.Property(x => x.UninstallCommand).HasMaxLength(2048);
            entity.Property(x => x.ExpectedExitCodesJson).HasMaxLength(256);
            entity.Property(x => x.DetectionConfigJson).HasMaxLength(2048);
            entity.Property(x => x.TimeoutSeconds).HasDefaultValue(300);
        });

        modelBuilder.Entity<AssignmentLeaseEntity>(entity =>
        {
            entity.HasKey(x => x.AssignmentId);
            entity.HasIndex(x => x.LeaseId).IsUnique();
            entity.HasOne(x => x.Job)
                .WithMany(x => x.AssignmentLeases)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.LeaseId).HasMaxLength(64);
            entity.Property(x => x.AgentId).HasMaxLength(128);
            entity.Property(x => x.State).HasMaxLength(64);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_AssignmentLeases_TtlSeconds", "\"TtlSeconds\" > 0");
                t.HasCheckConstraint("CK_AssignmentLeases_LastAckedSequence", "\"LastAckedSequence\" >= 0");
                t.HasCheckConstraint("CK_AssignmentLeases_State", "\"State\" IN ('Assigned','Released','Expired')");
            });
        });

        modelBuilder.Entity<ConfigSnapshotEntity>().HasKey(x => x.ConfigSnapshotId);
        modelBuilder.Entity<ConfigSnapshotEntity>()
            .HasIndex(x => new { x.JobId, x.NodeId, x.PackageId, x.CapturedAtUtc });
        modelBuilder.Entity<ConfigSnapshotEntity>()
            .HasOne(x => x.Job)
            .WithMany(x => x.ConfigSnapshots)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ConfigSnapshotEntity>()
            .HasOne(x => x.Node)
            .WithMany(x => x.ConfigSnapshots)
            .HasForeignKey(x => x.NodeId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ConfigSnapshotEntity>().Property(x => x.PackageId).HasMaxLength(128);
        modelBuilder.Entity<ConfigSnapshotEntity>().Property(x => x.SourceSchemaVersion).HasMaxLength(64);
        modelBuilder.Entity<ConfigSnapshotEntity>().Property(x => x.StorageLocation).HasMaxLength(512);
        modelBuilder.Entity<ConfigSnapshotEntity>().Property(x => x.IntegrityHash).HasMaxLength(128);

        modelBuilder.Entity<WorkloadDefinitionEntity>(entity =>
        {
            entity.HasKey(x => x.WorkloadId);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Description).HasMaxLength(512);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasOne(x => x.PublishedRevision)
                .WithMany()
                .HasForeignKey(x => x.PublishedRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkloadRevisionEntity>(entity =>
        {
            entity.HasKey(x => x.RevisionId);
            entity.Property(x => x.Version).HasMaxLength(64);
            entity.Property(x => x.PreWorkloadStepsJson).HasMaxLength(4096);
            entity.Property(x => x.PostWorkloadStepsJson).HasMaxLength(4096);
            entity.Property(x => x.PreUninstallStepsJson).HasMaxLength(4096).IsRequired().HasDefaultValue("[]");
            entity.Property(x => x.PostUninstallStepsJson).HasMaxLength(4096).IsRequired().HasDefaultValue("[]");
            entity.Property(x => x.DefaultShell).HasMaxLength(64);
            entity.HasOne(x => x.Workload)
                .WithMany(x => x.Revisions)
                .HasForeignKey(x => x.WorkloadId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.WorkloadId, x.Version }).IsUnique();
        });

        modelBuilder.Entity<WorkloadPackageEntity>(entity =>
        {
            entity.HasKey(x => x.WorkloadPackageId);
            entity.Property(x => x.PreInitStepsJson).HasMaxLength(4096);
            entity.Property(x => x.PostInitStepsJson).HasMaxLength(4096);
            entity.HasOne(x => x.Revision)
                .WithMany(x => x.Packages)
                .HasForeignKey(x => x.RevisionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.RevisionId, x.PackageIndex }).IsUnique();
        });

        modelBuilder.Entity<WorkloadRunEntity>(entity =>
        {
            entity.HasKey(x => x.WorkloadRunRecordId);
            entity.HasIndex(x => x.RunId);
            entity.Property(x => x.Mode).HasMaxLength(32);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.IdempotencyRequestHash).HasMaxLength(64);
            entity.Property(x => x.CancelReason).HasMaxLength(512);
            entity.Property(x => x.NodeDisplayName).HasMaxLength(255);
            entity.HasOne(x => x.Workload)
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.WorkloadId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Revision)
                .WithMany()
                .HasForeignKey(x => x.RevisionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Node)
                .WithMany(x => x.WorkloadRuns)
                .HasForeignKey(x => x.NodeId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.RevisionSnapshotJson).HasMaxLength(8192);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => new { x.NodeId, x.WorkloadId })
                .HasDatabaseName("IX_WorkloadRuns_NodeId_WorkloadId_Active")
                .HasFilter("\"State\" IN ('Queued','Running')")
                .IsUnique();
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_WorkloadRuns_Mode", "\"Mode\" IN ('install','update','uninstall','cancel')");
                t.HasCheckConstraint("CK_WorkloadRuns_State", "\"State\" IN ('Queued','Running','Completed','Failed','Cancelled')");
            });
        });

        modelBuilder.Entity<NodeWorkloadStateEntity>(entity =>
        {
            entity.HasKey(x => x.NodeWorkloadStateId);
            entity.Property(x => x.PackageStatesJson).HasMaxLength(8192);
            entity.HasOne(x => x.Node)
                .WithMany(x => x.NodeWorkloadStates)
                .HasForeignKey(x => x.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Workload)
                .WithMany(x => x.NodeStates)
                .HasForeignKey(x => x.WorkloadId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CurrentRevision)
                .WithMany()
                .HasForeignKey(x => x.CurrentRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.NodeId, x.WorkloadId }).IsUnique();
        });

        modelBuilder.Entity<WorkloadRunTimelineEntity>(entity =>
        {
            entity.HasKey(x => x.TimelineId);
            entity.HasIndex(x => x.RunId);
            entity.HasIndex(x => new { x.RunId, x.NodeId });
            entity.Property(x => x.MessageType).HasMaxLength(64);
            entity.Property(x => x.PackageId).HasMaxLength(128);
            entity.Property(x => x.StepName).HasMaxLength(128);
            entity.Property(x => x.Status).HasMaxLength(64);
            entity.Property(x => x.Detail).HasMaxLength(2048);
        });
    }

    private void EnforceWorkloadRevisionImmutability()
    {
        var revisionEntries = ChangeTracker.Entries<WorkloadRevisionEntity>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in revisionEntries)
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Workload revisions are immutable and cannot be deleted.");
            }

            var forbiddenPropertiesChanged = entry.Properties
                .Where(p => p.Metadata.Name is not nameof(WorkloadRevisionEntity.IsPublished)
                         and not nameof(WorkloadRevisionEntity.PreWorkloadStepsJson)
                         and not nameof(WorkloadRevisionEntity.PostWorkloadStepsJson)
                         and not nameof(WorkloadRevisionEntity.PreUninstallStepsJson)
                         and not nameof(WorkloadRevisionEntity.PostUninstallStepsJson)
                         and not nameof(WorkloadRevisionEntity.DefaultShell))
                .Any(p => p.IsModified);
            if (forbiddenPropertiesChanged)
            {
                throw new InvalidOperationException("Workload revisions are immutable after creation.");
            }
        }

        var packageEntries = ChangeTracker.Entries<WorkloadPackageEntity>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (packageEntries.Count > 0)
        {
            if (packageEntries.Any(e => e.State == EntityState.Deleted))
            {
                throw new InvalidOperationException("Workload revision packages are immutable and cannot be deleted.");
            }
            var allowedProperties = new HashSet<string>
            {
                nameof(WorkloadPackageEntity.PreInitStepsJson),
                nameof(WorkloadPackageEntity.PostInitStepsJson)
            };
            var hasForbiddenChanges = packageEntries.Any(entry =>
                entry.Properties.Any(p => p.IsModified && !allowedProperties.Contains(p.Metadata.Name)));
            if (hasForbiddenChanges)
            {
                throw new InvalidOperationException("Workload revision packages are immutable after creation.");
            }
        }
    }
}
