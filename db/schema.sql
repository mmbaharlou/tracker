/*
  Tracker schema for SQL Server 2016+ (columnstore + OPENJSON).
  Run once on a dedicated database, e.g.:
    CREATE DATABASE Tracker;  GO  USE Tracker;  GO  -- then this script
  The script is idempotent (safe to run again).
*/
SET NOCOUNT ON;
GO
IF SCHEMA_ID('tracker') IS NULL EXEC('CREATE SCHEMA tracker');
GO

/* ---------- Events: every page view, click, API call, custom event ---------- */
IF OBJECT_ID('tracker.Events') IS NULL
BEGIN
    CREATE TABLE tracker.Events
    (
        Id           UNIQUEIDENTIFIER NOT NULL,
        ProjectKey   VARCHAR(64)      NOT NULL,
        Source       VARCHAR(8)       NOT NULL,   -- web | server
        Type         VARCHAR(32)      NOT NULL,
        Name         NVARCHAR(200)    NULL,
        Ts           DATETIME2(3)     NOT NULL,   -- client time (UTC)
        ReceivedAt   DATETIME2(3)     NOT NULL,
        UserId       NVARCHAR(128)    NULL,
        AnonymousId  VARCHAR(64)      NULL,
        SessionId    VARCHAR(64)      NULL,
        Url          NVARCHAR(1024)   NULL,
        Path         NVARCHAR(512)    NULL,
        Referrer     NVARCHAR(1024)   NULL,
        Title        NVARCHAR(256)    NULL,
        Route        NVARCHAR(256)    NULL,
        Method       VARCHAR(10)      NULL,
        StatusCode   SMALLINT         NULL,
        Ip           VARCHAR(45)      NULL,
        Browser      VARCHAR(32)      NULL,
        Os           VARCHAR(32)      NULL,
        Device       VARCHAR(16)      NULL,
        Screen       VARCHAR(16)      NULL,
        Language     VARCHAR(16)      NULL,
        Properties   NVARCHAR(MAX)    NULL
    );
    -- Columnstore: ~10x compression and fast aggregations for reports.
    CREATE CLUSTERED COLUMNSTORE INDEX CCI_Events ON tracker.Events;
    -- Rowstore indexes for timelines and lookups.
    CREATE NONCLUSTERED INDEX IX_Events_User ON tracker.Events(ProjectKey, UserId, Ts DESC) WHERE UserId IS NOT NULL;
    CREATE NONCLUSTERED INDEX IX_Events_Anon ON tracker.Events(ProjectKey, AnonymousId, Ts DESC) WHERE AnonymousId IS NOT NULL;
    CREATE NONCLUSTERED INDEX IX_Events_Session ON tracker.Events(ProjectKey, SessionId, Ts) WHERE SessionId IS NOT NULL;
    CREATE NONCLUSTERED INDEX IX_Events_Time ON tracker.Events(ProjectKey, ReceivedAt DESC) INCLUDE (Type);
END
GO

/* ---------- Users: one profile per identified user ---------- */
IF OBJECT_ID('tracker.Users') IS NULL
BEGIN
    CREATE TABLE tracker.Users
    (
        ProjectKey   VARCHAR(64)    NOT NULL,
        UserId       NVARCHAR(128)  NOT NULL,
        Traits       NVARCHAR(MAX)  NULL,
        FirstSeen    DATETIME2(3)   NOT NULL,
        LastSeen     DATETIME2(3)   NOT NULL,
        EventCount   BIGINT         NOT NULL CONSTRAINT DF_Users_EventCount DEFAULT 0,
        LastBrowser  VARCHAR(32)    NULL,
        LastOs       VARCHAR(32)    NULL,
        LastDevice   VARCHAR(16)    NULL,
        LastIp       VARCHAR(45)    NULL,
        CONSTRAINT PK_Users PRIMARY KEY (ProjectKey, UserId)
    );
    CREATE NONCLUSTERED INDEX IX_Users_LastSeen ON tracker.Users(ProjectKey, LastSeen DESC);
END
GO

/* ---------- Anonymous -> user links (merge visitor history after login) ---------- */
IF OBJECT_ID('tracker.Aliases') IS NULL
BEGIN
    CREATE TABLE tracker.Aliases
    (
        ProjectKey  VARCHAR(64)   NOT NULL,
        AnonymousId VARCHAR(64)   NOT NULL,
        UserId      NVARCHAR(128) NOT NULL,
        LinkedAt    DATETIME2(3)  NOT NULL,
        CONSTRAINT PK_Aliases PRIMARY KEY (ProjectKey, AnonymousId)
    );
    CREATE NONCLUSTERED INDEX IX_Aliases_User ON tracker.Aliases(ProjectKey, UserId);
END
GO

/* ---------- Upsert users + aliases from a batch (called by the Collector with JSON) ---------- */
CREATE OR ALTER PROCEDURE tracker.UpsertUsers @json NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    /* @json = [{ projectKey, userId, anonId, traits, ts, count, browser, os, device, ip }] (one row per user per batch) */
    MERGE tracker.Users WITH (HOLDLOCK) AS t
    USING (
        SELECT ProjectKey, UserId, Traits, Ts, Cnt, Browser, Os, Device, Ip
        FROM OPENJSON(@json) WITH (
            ProjectKey VARCHAR(64) '$.projectKey', UserId NVARCHAR(128) '$.userId', Traits NVARCHAR(MAX) '$.traits',
            Ts DATETIME2(3) '$.ts', Cnt BIGINT '$.count', Browser VARCHAR(32) '$.browser', Os VARCHAR(32) '$.os',
            Device VARCHAR(16) '$.device', Ip VARCHAR(45) '$.ip')
        WHERE UserId IS NOT NULL
    ) AS s ON t.ProjectKey = s.ProjectKey AND t.UserId = s.UserId
    WHEN MATCHED THEN UPDATE SET
        LastSeen = CASE WHEN s.Ts > t.LastSeen THEN s.Ts ELSE t.LastSeen END,
        FirstSeen = CASE WHEN s.Ts < t.FirstSeen THEN s.Ts ELSE t.FirstSeen END,
        EventCount = t.EventCount + s.Cnt,
        Traits = COALESCE(s.Traits, t.Traits),
        LastBrowser = COALESCE(s.Browser, t.LastBrowser), LastOs = COALESCE(s.Os, t.LastOs),
        LastDevice = COALESCE(s.Device, t.LastDevice), LastIp = COALESCE(s.Ip, t.LastIp)
    WHEN NOT MATCHED THEN INSERT (ProjectKey, UserId, Traits, FirstSeen, LastSeen, EventCount, LastBrowser, LastOs, LastDevice, LastIp)
        VALUES (s.ProjectKey, s.UserId, s.Traits, s.Ts, s.Ts, s.Cnt, s.Browser, s.Os, s.Device, s.Ip);

    MERGE tracker.Aliases WITH (HOLDLOCK) AS t
    USING (
        SELECT DISTINCT ProjectKey, AnonId, UserId
        FROM OPENJSON(@json) WITH (ProjectKey VARCHAR(64) '$.projectKey', AnonId VARCHAR(64) '$.anonId', UserId NVARCHAR(128) '$.userId')
        WHERE AnonId IS NOT NULL AND UserId IS NOT NULL
    ) AS s ON t.ProjectKey = s.ProjectKey AND t.AnonymousId = s.AnonId
    WHEN NOT MATCHED THEN INSERT (ProjectKey, AnonymousId, UserId, LinkedAt) VALUES (s.ProjectKey, s.AnonId, s.UserId, SYSUTCDATETIME());
END
GO

/* ---------- Retention: delete old events in small chunks ---------- */
CREATE OR ALTER PROCEDURE tracker.PurgeOldEvents @days INT, @chunk INT = 50000
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoff DATETIME2(3) = DATEADD(DAY, -@days, SYSUTCDATETIME()), @n INT = 1, @total INT = 0;
    WHILE @n > 0
    BEGIN
        DELETE TOP (@chunk) FROM tracker.Events WHERE ReceivedAt < @cutoff;
        SET @n = @@ROWCOUNT; SET @total += @n;
    END
    SELECT @total AS Deleted;
END
GO

/* ---------- Right to be forgotten ---------- */
CREATE OR ALTER PROCEDURE tracker.ForgetUser @projectKey VARCHAR(64), @userId NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE e FROM tracker.Events e
    WHERE e.ProjectKey = @projectKey AND (e.UserId = @userId
       OR e.AnonymousId IN (SELECT AnonymousId FROM tracker.Aliases WHERE ProjectKey = @projectKey AND UserId = @userId));
    DELETE FROM tracker.Aliases WHERE ProjectKey = @projectKey AND UserId = @userId;
    DELETE FROM tracker.Users WHERE ProjectKey = @projectKey AND UserId = @userId;
END
GO

/* ---------- Least-privilege roles ----------
   Collector writer login: member of tracker_writer.  Dashboard reader: member of tracker_reader.
   Example:
     CREATE LOGIN tracker_collector WITH PASSWORD = '<strong password>';
     CREATE USER tracker_collector FOR LOGIN tracker_collector;
     ALTER ROLE tracker_writer ADD MEMBER tracker_collector;
     CREATE LOGIN tracker_dashboard WITH PASSWORD = '<another strong password>';
     CREATE USER tracker_dashboard FOR LOGIN tracker_dashboard;
     ALTER ROLE tracker_reader ADD MEMBER tracker_dashboard;
*/
IF DATABASE_PRINCIPAL_ID('tracker_writer') IS NULL CREATE ROLE tracker_writer;
IF DATABASE_PRINCIPAL_ID('tracker_reader') IS NULL CREATE ROLE tracker_reader;
GO
GRANT INSERT ON tracker.Events TO tracker_writer;
GRANT EXECUTE ON tracker.UpsertUsers TO tracker_writer;
GRANT EXECUTE ON tracker.PurgeOldEvents TO tracker_writer;
GRANT SELECT ON SCHEMA::tracker TO tracker_reader;
GRANT EXECUTE ON tracker.ForgetUser TO tracker_reader;
GO

/* =====================  Central management (v1.1)  ===================== */

/* Projects managed from the dashboard (projects in appsettings still work and show as read-only). */
IF OBJECT_ID('tracker.Projects') IS NULL
BEGIN
    CREATE TABLE tracker.Projects
    (
        [Key]                 VARCHAR(64)    NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
        Name                  NVARCHAR(100)  NOT NULL,
        ServerSecretProtected NVARCHAR(MAX)  NULL,   -- encrypted with ASP.NET Data Protection
        ReadKeyHash           CHAR(64)       NULL,   -- SHA-256 of the read-only key; the key itself is never stored
        AllowedOrigins        NVARCHAR(MAX)  NULL,   -- JSON array
        Settings              NVARCHAR(MAX)  NULL,   -- JSON ProjectSettings pushed to every install
        IsEnabled             BIT            NOT NULL CONSTRAINT DF_Projects_IsEnabled DEFAULT 1,
        CreatedAt             DATETIME2(0)   NOT NULL CONSTRAINT DF_Projects_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt             DATETIME2(0)   NOT NULL CONSTRAINT DF_Projects_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('tracker.DashboardUsers') IS NULL
BEGIN
    CREATE TABLE tracker.DashboardUsers
    (
        Username     NVARCHAR(64)   NOT NULL CONSTRAINT PK_DashboardUsers PRIMARY KEY,
        PasswordHash VARCHAR(200)   NOT NULL,
        Projects     NVARCHAR(MAX)  NOT NULL,   -- JSON array of project keys, ["*"] = all
        IsAdmin      BIT            NOT NULL CONSTRAINT DF_DashboardUsers_IsAdmin DEFAULT 0,
        CreatedAt    DATETIME2(0)   NOT NULL CONSTRAINT DF_DashboardUsers_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

/* Only the Collector's writer login manages these tables. */
GRANT SELECT, INSERT, UPDATE, DELETE ON tracker.Projects TO tracker_writer;
GRANT SELECT, INSERT, UPDATE, DELETE ON tracker.DashboardUsers TO tracker_writer;
DENY SELECT ON tracker.Projects TO tracker_reader;
DENY SELECT ON tracker.DashboardUsers TO tracker_reader;
GO
