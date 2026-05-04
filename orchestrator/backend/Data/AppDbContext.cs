using Microsoft.EntityFrameworkCore;
using Orchestrator.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<Workload> Workloads => Set<Workload>();
    public DbSet<WorkloadPackage> WorkloadPackages => Set<WorkloadPackage>();
    public DbSet<AgentNode> AgentNodes => Set<AgentNode>();
    public DbSet<AgentPackage> AgentPackages => Set<AgentPackage>();
    public DbSet<WorkloadRun> WorkloadRuns => Set<WorkloadRun>();
    public DbSet<WorkloadRunStep> WorkloadRunSteps => Set<WorkloadRunStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkloadPackage>(entity =>
        {
            entity.HasKey(wp => new { wp.WorkloadId, wp.WorkloadVersion, wp.PackageId });
            entity.Property(wp => wp.PreInitSteps).HasColumnType("TEXT");
            entity.Property(wp => wp.PostInitSteps).HasColumnType("TEXT");
            entity.HasOne(wp => wp.Workload)
                  .WithMany(w => w.Packages)
                  .HasForeignKey(wp => new { wp.WorkloadId, wp.WorkloadVersion })
                  .HasPrincipalKey(w => new { w.WorkloadId, w.Version });
            entity.HasOne(wp => wp.Package)
                  .WithMany()
                  .HasForeignKey(wp => new { wp.PackageId, wp.PackageVersion })
                  .HasPrincipalKey(a => new { a.PackageId, a.Version });
        });

        modelBuilder.Entity<AgentPackage>(entity =>
        {
            entity.HasKey(ap => new { ap.AgentId, ap.PackageId });
            entity.HasOne(ap => ap.Agent)
                  .WithMany(a => a.InstalledPackages)
                  .HasForeignKey(ap => ap.AgentId)
                  .HasPrincipalKey(a => a.AgentId);
        });

        modelBuilder.Entity<Artifact>(entity =>
        {
            entity.HasIndex(a => new { a.PackageId, a.Version }).IsUnique();
        });

        modelBuilder.Entity<Workload>(entity =>
        {
            entity.HasIndex(w => new { w.WorkloadId, w.Version }).IsUnique();
        });

        modelBuilder.Entity<EnrollmentToken>(entity =>
        {
            entity.HasIndex(e => e.Token);
        });

        modelBuilder.Entity<AgentNode>(entity =>
        {
            entity.HasIndex(a => a.AgentId).IsUnique();
            entity.HasIndex(a => a.AgentSecret).IsUnique();
            entity.HasIndex(a => a.Status);
            entity.HasOne(a => a.Workload)
                  .WithMany()
                  .HasForeignKey(a => a.AssignedWorkloadId)
                  .HasPrincipalKey(w => w.WorkloadId);
            entity.HasMany(a => a.Runs)
                  .WithOne()
                  .HasForeignKey(r => r.AgentId)
                  .HasPrincipalKey(a => a.AgentId);
        });

        modelBuilder.Entity<WorkloadRun>(entity =>
        {
            entity.HasIndex(r => r.AgentId);
            entity.HasIndex(r => r.Status);
            entity.HasOne(r => r.Workload)
                  .WithMany()
                  .HasForeignKey(r => new { r.WorkloadId, r.WorkloadVersion })
                  .HasPrincipalKey(w => new { w.WorkloadId, w.Version });
        });

        modelBuilder.Entity<WorkloadRunStep>(entity =>
        {
            entity.HasOne(rs => rs.Run)
                  .WithMany(r => r.Steps)
                  .HasForeignKey(rs => rs.RunId);
        });

        // Enum conversions
        modelBuilder.Entity<AgentNode>().Property(a => a.Status).HasConversion<string>();
        modelBuilder.Entity<WorkloadRun>().Property(w => w.Mode).HasConversion<string>();
        modelBuilder.Entity<WorkloadRun>().Property(w => w.Status).HasConversion<string>();
        modelBuilder.Entity<WorkloadRunStep>().Property(w => w.Action).HasConversion<string>();
        modelBuilder.Entity<WorkloadRunStep>().Property(w => w.Status).HasConversion<string>();
        modelBuilder.Entity<AgentPackage>().Property(a => a.Status).HasConversion<string>();
    }
}
