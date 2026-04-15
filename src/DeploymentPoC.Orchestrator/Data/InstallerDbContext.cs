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
    public DbSet<AssignmentLeaseEntity> AssignmentLeases => Set<AssignmentLeaseEntity>();
    public DbSet<ConfigSnapshotEntity> ConfigSnapshots => Set<ConfigSnapshotEntity>();

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
            entity.Property(x => x.AgentVersion).HasMaxLength(64);
            entity.Property(x => x.Status).HasMaxLength(64);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Nodes_Status", "\"Status\" IN ('Offline','Online')");
            });
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
    }
}
