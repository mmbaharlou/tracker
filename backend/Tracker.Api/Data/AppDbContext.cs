using Microsoft.EntityFrameworkCore;

namespace Tracker.Api.Data;

/// <summary>
/// Management data (panel users, sessions, audit log, projects) through EF Core.
/// The high-volume event tables (tracker.Events, Users, Aliases) are created by the Initial migration from
/// Data/Sql/events.sql and written with SqlBulkCopy/Dapper for speed.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string Schema = "tracker";

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminUserProject> AdminUserProjects => Set<AdminUserProject>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();
    public DbSet<TrackerProject> Projects => Set<TrackerProject>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ConfigureAdmin(Schema);
        b.Entity<TrackerProject>(e =>
        {
            e.ToTable("Projects", Schema);
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.ReadKeyHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        });
        b.UseUtcDateTimes();
    }
}

/// <summary>A site that sends events. Its server secret is encrypted with ASP.NET Data Protection; of the read key only a hash is kept.</summary>
public class TrackerProject
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ServerSecretProtected { get; set; }
    public string? ReadKeyHash { get; set; }
    /// <summary>JSON array of origins allowed to send browser events.</summary>
    public string? AllowedOrigins { get; set; }
    /// <summary>JSON <see cref="Tracker.Core.ProjectSettings"/> pushed to every install.</summary>
    public string? Settings { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
