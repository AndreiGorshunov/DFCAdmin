/* =============================================================================
   Event Registration — Database Schema (Microsoft SQL Server / T-SQL)
   -----------------------------------------------------------------------------
   Источник: ER-диаграмма (Users / Events / FamilyMembers /
             EventRegistrations / RegistrationParticipants)

   Принятые решения:
     * NVARCHAR везде — поддержка EN/AR (Unicode обязателен для арабского).
     * DATETIME2(0) вместо legacy DATETIME.
     * Events.Id НЕ имеет DEFAULT — GUID приходит из внешней системы.
     * Все FK с NO ACTION (см. блок про каскады в конце файла).
     * QRCode nullable + unique → фильтрованный уникальный индекс.
     * Идемпотентность — каждый объект создаётся через IF NOT EXISTS,
       скрипт можно запускать повторно без ошибок.

   Порядок создания учитывает зависимости:
     Users, Events  ->  FamilyMembers, EventRegistrations  ->  RegistrationParticipants
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF NOT EXISTS (
    SELECT 1 
    FROM sys.databases 
    WHERE name = 'dfc.EventRegistration'
)
BEGIN
    CREATE DATABASE [dfc.EventRegistration];
END
GO

/* -----------------------------------------------------------------------------
   Схема (по желанию вынести в отдельную; здесь используем dbo)
   -------------------------------------------------------------------------- */
-- IF SCHEMA_ID(N'evt') IS NULL EXEC(N'CREATE SCHEMA evt AUTHORIZATION dbo;');
-- GO

/* =============================================================================
   1. Users
   ============================================================================= */
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        UserId       INT            IDENTITY(1,1) NOT NULL,
        FirstName    NVARCHAR(100)  NOT NULL,
        LastName     NVARCHAR(100)  NOT NULL,
        Email        NVARCHAR(256)  NOT NULL,
        Phone        NVARCHAR(32)   NULL,
        DateOfBirth  DATE           NULL,

        -- Цифры телефона в обратном порядке (PERSISTED). Поиск «оканчивается на N цифр»
        -- = префикс по этой колонке (seek), а не подстрочный скан Phone LIKE '%x%'.
        PhoneDigitsRev AS REVERSE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Phone,'+',''),' ',''),'-',''),'(',''),')','')) PERSISTED,

        CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (UserId)
    );

    -- Email как естественный уникальный ключ пользователя.
    -- Убери, если допускаются дубли e-mail.
    CREATE UNIQUE INDEX UX_Users_Email ON dbo.Users (Email);

    -- Поиск по телефону «по последним цифрам»: префикс по развёрнутым цифрам = seek.
    CREATE INDEX IX_Users_PhoneDigitsRev ON dbo.Users (PhoneDigitsRev) WHERE PhoneDigitsRev IS NOT NULL;
END
GO

/* =============================================================================
   2. Events
   ID приходит из внешней системы -> без DEFAULT NEWID().
   ============================================================================= */
IF OBJECT_ID(N'dbo.Events', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Events
    (
        Id                       UNIQUEIDENTIFIER NOT NULL,   -- from external system
        Name                     NVARCHAR(256)    NOT NULL,
        Description              NVARCHAR(MAX)    NULL,
        StartDate                DATETIME2(0)     NOT NULL,
        EndDate                  DATETIME2(0)     NOT NULL,
        RegistrationOpeningDate  DATETIME2(0)     NULL,

        CONSTRAINT PK_Events PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_Events_DateRange CHECK (EndDate >= StartDate)
    );
END
GO

/* =============================================================================
   3. FamilyMembers
   Дети/члены семьи, привязанные к пользователю (родителю).
   ============================================================================= */
IF OBJECT_ID(N'dbo.FamilyMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FamilyMembers
    (
        FamilyMemberId INT            IDENTITY(1,1) NOT NULL,
        UserId         INT            NOT NULL,
        FirstName      NVARCHAR(100)  NOT NULL,
        LastName       NVARCHAR(100)  NOT NULL,
        DateOfBirth    DATE           NULL,

        CONSTRAINT PK_FamilyMembers PRIMARY KEY CLUSTERED (FamilyMemberId),
        CONSTRAINT FK_FamilyMembers_Users
            FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId)
    );

    -- Индекс на FK (SQL Server не создаёт его автоматически).
    CREATE INDEX IX_FamilyMembers_UserId ON dbo.FamilyMembers (UserId);
END
GO

/* =============================================================================
   4. EventRegistrations
   Регистрация пользователя на событие.
   ============================================================================= */
IF OBJECT_ID(N'dbo.EventRegistrations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventRegistrations
    (
        RegistrationId            BIGINT           IDENTITY(1,1) NOT NULL,
        EventId                   UNIQUEIDENTIFIER NOT NULL,
        UserId                    INT              NOT NULL,
        GroupCode                 NVARCHAR(64)     NULL,
        EmergencyContactFirstName NVARCHAR(100)    NULL,
        EmergencyContactLastName  NVARCHAR(100)    NULL,
        EmergencyContactPhone     NVARCHAR(32)     NULL,
        RegistrationDate          DATETIME2(0)     NOT NULL
            CONSTRAINT DF_EventRegistrations_RegistrationDate DEFAULT (SYSUTCDATETIME()),
        Status                    TINYINT          NOT NULL
            CONSTRAINT DF_EventRegistrations_Status DEFAULT (0),
        QRCode                    NVARCHAR(256)    NULL,
        RegistrantLastName        NVARCHAR(100)    NULL,   -- денормализация фамилии регистранта (Вариант B; 04 делает NOT NULL + IX_ER_Keyset)

        CONSTRAINT PK_EventRegistrations PRIMARY KEY CLUSTERED (RegistrationId),
        CONSTRAINT FK_EventRegistrations_Events
            FOREIGN KEY (EventId) REFERENCES dbo.Events (Id),
        CONSTRAINT FK_EventRegistrations_Users
            FOREIGN KEY (UserId)  REFERENCES dbo.Users (UserId)
    );

    -- Индексы на FK.
    CREATE INDEX IX_EventRegistrations_EventId ON dbo.EventRegistrations (EventId);
    CREATE INDEX IX_EventRegistrations_UserId  ON dbo.EventRegistrations (UserId);

    -- QRCode: nullable + unique. Обычный UNIQUE трактует несколько NULL как
    -- дубли, поэтому используем фильтрованный индекс.
    CREATE UNIQUE INDEX UX_EventRegistrations_QRCode
        ON dbo.EventRegistrations (QRCode)
        WHERE QRCode IS NOT NULL;

    -- Опционально: одна регистрация на пользователя в рамках события.
    -- Раскомментируй, если это бизнес-правило:
    -- CREATE UNIQUE INDEX UX_EventRegistrations_Event_User
    --     ON dbo.EventRegistrations (EventId, UserId);

    -- Опционально: справочник статусов через CHECK (подставь реальные значения):
    -- ALTER TABLE dbo.EventRegistrations
    --     ADD CONSTRAINT CK_EventRegistrations_Status
    --     CHECK (Status IN (0, 1, 2, 3));
END
GO

/* =============================================================================
   5. RegistrationParticipants
   Участники в рамках конкретной регистрации.
   FamilyMemberId: NULL = сам родитель (Parent), NOT NULL = ребёнок (Child).
   EventId — денормализован (дублирует EventRegistrations.EventId).
   ============================================================================= */
IF OBJECT_ID(N'dbo.RegistrationParticipants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RegistrationParticipants
    (
        ParticipantId  BIGINT           IDENTITY(1,1) NOT NULL,
        RegistrationId BIGINT           NOT NULL,
        EventId        UNIQUEIDENTIFIER NOT NULL,   -- denormalised
        FamilyMemberId INT              NULL,       -- NULL = Parent, NOT NULL = Child
        TshirtSize     NVARCHAR(16)     NULL,

        CONSTRAINT PK_RegistrationParticipants PRIMARY KEY CLUSTERED (ParticipantId),
        CONSTRAINT FK_RegistrationParticipants_EventRegistrations
            FOREIGN KEY (RegistrationId) REFERENCES dbo.EventRegistrations (RegistrationId),
        CONSTRAINT FK_RegistrationParticipants_FamilyMembers
            FOREIGN KEY (FamilyMemberId) REFERENCES dbo.FamilyMembers (FamilyMemberId),
        CONSTRAINT FK_RegistrationParticipants_Events
            FOREIGN KEY (EventId) REFERENCES dbo.Events (Id)
    );

    -- Индексы на FK.
    CREATE INDEX IX_RegistrationParticipants_RegistrationId
        ON dbo.RegistrationParticipants (RegistrationId);
    CREATE INDEX IX_RegistrationParticipants_FamilyMemberId
        ON dbo.RegistrationParticipants (FamilyMemberId)
        WHERE FamilyMemberId IS NOT NULL;
    CREATE INDEX IX_RegistrationParticipants_EventId
        ON dbo.RegistrationParticipants (EventId);

    -- Опционально: один член семьи не дублируется в одной регистрации.
    -- Фильтр нужен, т.к. NULL (родитель) может быть только один на регистрацию —
    -- при необходимости вынеси правило "ровно один Parent" в приложение/проц.
    -- CREATE UNIQUE INDEX UX_RegistrationParticipants_Reg_FamilyMember
    --     ON dbo.RegistrationParticipants (RegistrationId, FamilyMemberId)
    --     WHERE FamilyMemberId IS NOT NULL;
END
GO

/* =============================================================================
   6. AdminUsers  (доступ в админку + роль)
   Источник истины для ролей: IdP аутентифицирует личность, роль назначается здесь.
   ============================================================================= */
IF OBJECT_ID(N'dbo.AdminUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminUsers
    (
        AdminUserId  INT            IDENTITY(1,1) NOT NULL,
        Email        NVARCHAR(256)  NOT NULL,
        Role         NVARCHAR(32)   NOT NULL,        -- 'Admin' | 'Partner'
        DisplayName  NVARCHAR(200)  NULL,
        IsActive     BIT            NOT NULL CONSTRAINT DF_AdminUsers_IsActive DEFAULT (1),
        GrantedBy    NVARCHAR(256)  NULL,            -- кто выдал доступ
        GrantedAtUtc DATETIME2(0)   NOT NULL CONSTRAINT DF_AdminUsers_GrantedAt DEFAULT (SYSUTCDATETIME()),
        ExpiresAtUtc DATETIME2(0)   NULL,            -- null = бессрочно

        CONSTRAINT PK_AdminUsers PRIMARY KEY CLUSTERED (AdminUserId)
    );

    CREATE UNIQUE INDEX UX_AdminUsers_Email ON dbo.AdminUsers (Email);
END
GO

/* =============================================================================
   7. AuditLog  (кто/когда/что сделал)
   Пишется на каждую мутацию/удаление (в той же транзакции, что и изменение).
   ============================================================================= */
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

    -- Свежие записи сверху (вьюер сортирует по WhenUtc DESC).
    CREATE INDEX IX_AuditLog_WhenUtc ON dbo.AuditLog (WhenUtc DESC);
END
GO

/* =============================================================================
   ПРИМЕЧАНИЕ ПРО КАСКАДНОЕ УДАЛЕНИЕ
   -----------------------------------------------------------------------------
   Все FK созданы с NO ACTION (поведение по умолчанию). Причина:
   RegistrationParticipants ссылается одновременно на EventRegistrations,
   FamilyMembers и Events. Любая попытка задать ON DELETE CASCADE по нескольким
   из этих путей упрётся в ошибку SQL Server "multiple cascade paths".

   Рекомендуемые варианты для продакшена:
     1) Soft delete (IsDeleted/DeletedAtUtc) вместо физического удаления —
        предпочтительно для аудита и PDPL/GDPR-сценариев.
     2) Каскад через хранимую процедуру: удалять дочерние строки в правильном
        порядке внутри одной транзакции (Participants -> Registrations -> ...).
   ============================================================================= */
