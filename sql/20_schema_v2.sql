/* =============================================================================
   20_schema_v2.sql — Sessions / StartPoints / RegistrationSessions (DFC v2)
   -----------------------------------------------------------------------------
   Аддитивно к 01_schema.sql (существующие таблицы не меняются). Идемпотентно.
   Запускать ПОСЛЕ 01 — нужны dbo.Events и dbo.EventRegistrations.

   Модель (из ER-диаграммы v2):
     Events (1) ──contains──> EventSessions (1) ──offers──> EventStartPoints
     EventRegistrations (1) ──selects──> RegistrationSessions
       └ RegistrationSessions ссылается на выбранную Session и StartPoint + чек-ин.

   Все FK — NO ACTION (как и в 01): RegistrationSessions имеет три FK, любой
   ON DELETE CASCADE упрётся в "multiple cascade paths". Каскад — вручную в коде.
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [dfc.EventRegistration];
GO

/* =============================================================================
   1. EventSessions — сессии внутри события (5K Run, Yoga, ...).
   ============================================================================= */
IF OBJECT_ID(N'dbo.EventSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventSessions
    (
        SessionId       INT              IDENTITY(1,1) NOT NULL,
        EventId         UNIQUEIDENTIFIER NOT NULL,
        Name            NVARCHAR(256)    NOT NULL,
        Description     NVARCHAR(MAX)    NULL,
        MaxParticipants INT              NULL,

        CONSTRAINT PK_EventSessions PRIMARY KEY CLUSTERED (SessionId),
        CONSTRAINT FK_EventSessions_Events
            FOREIGN KEY (EventId) REFERENCES dbo.Events (Id)
    );

    CREATE INDEX IX_EventSessions_EventId ON dbo.EventSessions (EventId);
END
GO

/* =============================================================================
   2. EventStartPoints — точки/слоты старта внутри сессии (волны, площадки).
   StartTime/EndTime — TIME (время суток, без даты). DisplayOrder — порядок в UI.
   ============================================================================= */
IF OBJECT_ID(N'dbo.EventStartPoints', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventStartPoints
    (
        StartPointId INT           IDENTITY(1,1) NOT NULL,
        SessionId    INT           NOT NULL,
        Name         NVARCHAR(256) NOT NULL,
        StartTime    TIME(0)       NULL,
        EndTime      TIME(0)       NULL,
        Capacity     INT           NULL,
        DisplayOrder INT           NOT NULL
            CONSTRAINT DF_EventStartPoints_DisplayOrder DEFAULT (0),

        CONSTRAINT PK_EventStartPoints PRIMARY KEY CLUSTERED (StartPointId),
        CONSTRAINT FK_EventStartPoints_EventSessions
            FOREIGN KEY (SessionId) REFERENCES dbo.EventSessions (SessionId)
    );

    CREATE INDEX IX_EventStartPoints_SessionId ON dbo.EventStartPoints (SessionId);
END
GO

/* =============================================================================
   3. RegistrationSessions — какую сессию и точку старта выбрала регистрация,
   плюс факт чек-ина (CheckedIn / CheckInTime).
   ============================================================================= */
IF OBJECT_ID(N'dbo.RegistrationSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RegistrationSessions
    (
        RegistrationSessionId BIGINT       IDENTITY(1,1) NOT NULL,
        RegistrationId        BIGINT       NOT NULL,
        SessionId             INT          NOT NULL,
        StartPointId          INT          NOT NULL,
        CheckedIn             BIT          NOT NULL
            CONSTRAINT DF_RegistrationSessions_CheckedIn DEFAULT (0),
        CheckInTime           DATETIME2(0) NULL,

        CONSTRAINT PK_RegistrationSessions PRIMARY KEY CLUSTERED (RegistrationSessionId),
        CONSTRAINT FK_RegistrationSessions_EventRegistrations
            FOREIGN KEY (RegistrationId) REFERENCES dbo.EventRegistrations (RegistrationId),
        CONSTRAINT FK_RegistrationSessions_EventSessions
            FOREIGN KEY (SessionId) REFERENCES dbo.EventSessions (SessionId),
        CONSTRAINT FK_RegistrationSessions_EventStartPoints
            FOREIGN KEY (StartPointId) REFERENCES dbo.EventStartPoints (StartPointId)
    );

    -- Индексы на FK (доп. составные/уникальные — в 21_indexes_v2.sql).
    CREATE INDEX IX_RegistrationSessions_RegistrationId ON dbo.RegistrationSessions (RegistrationId);
    CREATE INDEX IX_RegistrationSessions_SessionId      ON dbo.RegistrationSessions (SessionId);
    CREATE INDEX IX_RegistrationSessions_StartPointId   ON dbo.RegistrationSessions (StartPointId);
END
GO
