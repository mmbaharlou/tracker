# Tracker — notes for Claude

Central user-tracking service for many sites (tracker.rhinox.ir). Same layout as BimeYar. See README.md for setup.

## Layout
- `backend/Tracker.Api` — ASP.NET Core 10.
  - `Controllers/` — panel API (`/api/auth`, `/api/data`, `/api/admin/*`). Ingestion stays minimal APIs in `Ingestion/` and `Management/ConfigAndReadEndpoints.cs` (`/v1/*`, hot path).
  - `Auth/` — panel login: PBKDF2, server-side sessions (`SessionStore`, cookie only holds a random token, DB holds its SHA-256), lockout, roles Viewer/Admin/Owner, audit log. Shared verbatim with Watchtower; change both.
  - `Data/AppDbContext.cs` — EF Core for management tables (schema `tracker`). Migrations: `dotnet ef migrations add <Name> -o Data/Migrations` from `backend/Tracker.Api`; they apply on startup.
  - `Data/Sql/events.sql` — high-volume event tables/procs, applied by the Initial migration. Written with SqlBulkCopy, read with Dapper (`Dashboard/DashboardQueries.cs`).
  - `Management/Registry.cs` — in-memory project snapshot used by ingestion (no DB I/O per request).
- `backend/Tracker.AspNetCore`, `backend/Tracker.Core` — NuGet SDK for sites (net8, keep compatible). `packages/tracker-next` — npm SDK.
- `frontend` — Next.js 15 App Router, Tailwind 4 tokens in `src/app/globals.css`. Client-rendered panel; `/api` and `/v1` are proxied to the API.

## Rules
- Every panel endpoint that takes a project checks `PanelUser.CanSee(project)`; admin endpoints use `Policies.Admin` / `Policies.Owner`.
- State-changing `/api` calls need `X-Requested-With: fetch` (CSRF guard). The frontend `api()` helper adds it.
- Never return secrets in listings: server secret and read key are shown once on create/rotate.
- Keep the 1.x upgrade path working (existing `tracker.Projects`, `DashboardUsers` import, `TrackerWriter` connection string).
- UI text is Persian; code and comments are English.
