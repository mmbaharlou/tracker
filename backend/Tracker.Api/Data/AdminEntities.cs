namespace Tracker.Api.Data;

/// <summary>Viewer: reads the projects assigned to them. Admin: all projects, manages projects and viewers. Owner: everything, including admins.</summary>
public enum AdminRole { Viewer = 0, Admin = 1, Owner = 2 }

public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public AdminRole Role { get; set; }
    /// <summary>Viewers only: true = every project (also future ones).</summary>
    public bool AllProjects { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Set for new accounts and after an admin resets the password; every API except changing it answers 403.</summary>
    public bool MustChangePassword { get; set; }
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime PasswordChangedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<AdminUserProject> Projects { get; set; } = [];

    public bool SeesAllProjects => Role >= AdminRole.Admin || AllProjects;
}

public class AdminUserProject
{
    public int UserId { get; set; }
    public string ProjectKey { get; set; } = "";
}

/// <summary>A server-side login session. The cookie holds a random token; only its SHA-256 is stored, so a database leak cannot be replayed.</summary>
public class AdminSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public AdminUser? User { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    /// <summary>Absolute end of the session, however active it is.</summary>
    public DateTime ExpiresAt { get; set; }
    public bool Persistent { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public int? UserId { get; set; }
    public string? Username { get; set; }
    /// <summary>Machine-readable, e.g. login.ok, login.failed, user.create, project.rotate.</summary>
    public string Action { get; set; } = "";
    public string? Target { get; set; }
    public string? Detail { get; set; }
    public string? Ip { get; set; }
}
