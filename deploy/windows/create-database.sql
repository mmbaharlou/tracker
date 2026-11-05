-- دیتابیس و لاگین Tracker (یک بار روی همان SQL Server اجرا شود). جدول‌ها را خود سرویس می‌سازد.
IF DB_ID('Tracker') IS NULL CREATE DATABASE Tracker;
GO
ALTER DATABASE Tracker SET RECOVERY SIMPLE;
GO
IF SUSER_ID('tracker_owner') IS NULL CREATE LOGIN tracker_owner WITH PASSWORD = 'CHANGE_ME', CHECK_POLICY = OFF;
GO
GO
USE Tracker;
GO
IF DATABASE_PRINCIPAL_ID('tracker_owner') IS NULL CREATE USER tracker_owner FOR LOGIN tracker_owner;
ALTER ROLE db_owner ADD MEMBER tracker_owner;
GO
