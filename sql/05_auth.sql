/* =============================================================================
   05_auth.sql — Аутентификация/авторизация (ветка feature/auth)
   -----------------------------------------------------------------------------
   Идемпотентно создаёт таблицы AdminUsers и AuditLog на УЖЕ существующей БД
   (те же определения, что в 01_schema.sql — для свежей БД они уже созданы там).
   Плюс сидит стартовых пользователей доступа.

   Запуск: на существующей базе достаточно прогнать только этот скрипт.
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [dfc.EventRegistration];
GO

/* --- AdminUsers ----------------------------------------------------------- */
IF OBJECT_ID(N'dbo.AdminUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminUsers
    (
        AdminUserId  INT            IDENTITY(1,1) NOT NULL,
        Email        NVARCHAR(256)  NOT NULL,
        Role         NVARCHAR(32)   NOT NULL,        -- 'Admin' | 'Partner'
        DisplayName  NVARCHAR(200)  NULL,
        IsActive     BIT            NOT NULL CONSTRAINT DF_AdminUsers_IsActive DEFAULT (1),
        GrantedBy    NVARCHAR(256)  NULL,
        GrantedAtUtc DATETIME2(0)   NOT NULL CONSTRAINT DF_AdminUsers_GrantedAt DEFAULT (SYSUTCDATETIME()),
        ExpiresAtUtc DATETIME2(0)   NULL,

        CONSTRAINT PK_AdminUsers PRIMARY KEY CLUSTERED (AdminUserId)
    );
    CREATE UNIQUE INDEX UX_AdminUsers_Email ON dbo.AdminUsers (Email);
END
GO

/* --- AuditLog ------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        AuditId     BIGINT         IDENTITY(1,1) NOT NULL,
        WhenUtc     DATETIME2(0)   NOT NULL CONSTRAINT DF_AuditLog_WhenUtc DEFAULT (SYSUTCDATETIME()),
        ActorEmail  NVARCHAR(256)  NULL,
        Action      NVARCHAR(64)   NOT NULL,
        EntityType  NVARCHAR(64)   NULL,
        EntityId    NVARCHAR(64)   NULL,
        Details     NVARCHAR(1024) NULL,

        CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED (AuditId)
    );
    CREATE INDEX IX_AuditLog_WhenUtc ON dbo.AuditLog (WhenUtc DESC);
END
GO

/* --- Сид стартовых пользователей -----------------------------------------
   ВАЖНО: замени эти email на реальные адреса сотрудников (или раздай доступ
   через UI «Admins» после первого входа админом). Сид идемпотентен.
   Для dev-входа значения совпадают с подсказками на странице логина. -------- */
MERGE dbo.AdminUsers AS t
USING (VALUES
    (N'admin@dfc.local',   N'Admin',   N'Seed Admin'),
    (N'partner@dfc.local', N'Partner', N'Seed Partner'),
    (N'steward@dfc.local', N'Steward', N'Seed Steward')
) AS s(Email, Role, DisplayName)
ON t.Email = s.Email
WHEN NOT MATCHED THEN
    INSERT (Email, Role, DisplayName, GrantedBy)
    VALUES (s.Email, s.Role, s.DisplayName, N'seed');
GO

PRINT 'AdminUsers / AuditLog готовы. Текущие пользователи доступа:';
SELECT AdminUserId, Email, Role, IsActive, GrantedBy, GrantedAtUtc, ExpiresAtUtc FROM dbo.AdminUsers;
GO
