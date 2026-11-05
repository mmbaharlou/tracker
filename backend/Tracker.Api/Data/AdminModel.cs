using Microsoft.EntityFrameworkCore;

namespace Tracker.Api.Data;

public static class AdminModel
{
    /// <summary>SQL Server does not store the kind; every DateTime here is UTC, so mark it as such when reading (JSON then ends in "Z").</summary>
    public static void UseUtcDateTimes(this ModelBuilder b)
    {
        var conv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nconv = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        foreach (var e in b.Model.GetEntityTypes())
        foreach (var p in e.GetProperties())
        {
            if (p.ClrType == typeof(DateTime)) p.SetValueConverter(conv);
            else if (p.ClrType == typeof(DateTime?)) p.SetValueConverter(nconv);
        }
    }

    /// <summary>Tables for panel users, sessions and the audit log.</summary>
    public static void ConfigureAdmin(this ModelBuilder b, string schema)
    {
        b.Entity<AdminUser>(e =>
        {
            e.ToTable("AdminUsers", schema);
            e.Property(x => x.Username).HasMaxLength(64);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsUnicode(false);
            e.Property(x => x.LastLoginIp).HasMaxLength(45).IsUnicode(false);
            e.HasMany(x => x.Projects).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AdminUserProject>(e =>
        {
            e.ToTable("AdminUserProjects", schema);
            e.HasKey(x => new { x.UserId, x.ProjectKey });
            e.Property(x => x.ProjectKey).HasMaxLength(64).IsUnicode(false);
        });
        b.Entity<AdminSession>(e =>
        {
            e.ToTable("AdminSessions", schema);
            e.Property(x => x.TokenHash).HasMaxLength(32);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.Property(x => x.Ip).HasMaxLength(45).IsUnicode(false);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditLog", schema);
            e.HasIndex(x => x.At);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Target).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.Ip).HasMaxLength(45).IsUnicode(false);
        });
    }
}
