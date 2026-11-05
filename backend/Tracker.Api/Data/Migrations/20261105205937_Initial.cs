using Tracker.Api.Data;
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tracker");

            migrationBuilder.CreateTable(
                name: "AdminUsers",
                schema: "tracker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PasswordHash = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    AllProjects = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    FailedLogins = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginIp = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    PasswordChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "tracker",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Action = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Ip = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            // Tracker 1.x created tracker.Projects with the same columns (db/schema.sql): keep it and its projects.
            migrationBuilder.Sql(@"IF OBJECT_ID('tracker.Projects') IS NULL
CREATE TABLE tracker.Projects (
    [Key] varchar(64) NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
    Name nvarchar(100) NOT NULL,
    ServerSecretProtected nvarchar(max) NULL,
    ReadKeyHash char(64) NULL,
    AllowedOrigins nvarchar(max) NULL,
    Settings nvarchar(max) NULL,
    IsEnabled bit NOT NULL,
    CreatedAt datetime2 NOT NULL,
    UpdatedAt datetime2 NOT NULL);");

            migrationBuilder.CreateTable(
                name: "AdminSessions",
                schema: "tracker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Persistent = table.Column<bool>(type: "bit", nullable: false),
                    Ip = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminSessions_AdminUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "tracker",
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminUserProjects",
                schema: "tracker",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ProjectKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUserProjects", x => new { x.UserId, x.ProjectKey });
                    table.ForeignKey(
                        name: "FK_AdminUserProjects_AdminUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "tracker",
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_TokenHash",
                schema: "tracker",
                table: "AdminSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_UserId_RevokedAt",
                schema: "tracker",
                table: "AdminSessions",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUsers_Username",
                schema: "tracker",
                table: "AdminUsers",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_At",
                schema: "tracker",
                table: "AuditLog",
                column: "At");

            // High-volume event tables, columnstore index and procedures (raw SQL, written with SqlBulkCopy/Dapper).
            foreach (var batch in SqlScripts.Batches("events.sql"))
                migrationBuilder.Sql(batch);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS tracker.ForgetUser; DROP PROCEDURE IF EXISTS tracker.PurgeOldEvents; DROP PROCEDURE IF EXISTS tracker.UpsertUsers;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS tracker.Aliases; DROP TABLE IF EXISTS tracker.Users; DROP TABLE IF EXISTS tracker.Events;");

            migrationBuilder.DropTable(
                name: "AdminSessions",
                schema: "tracker");

            migrationBuilder.DropTable(
                name: "AdminUserProjects",
                schema: "tracker");

            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "tracker");

            migrationBuilder.DropTable(
                name: "Projects",
                schema: "tracker");

            migrationBuilder.DropTable(
                name: "AdminUsers",
                schema: "tracker");
        }
    }
}
