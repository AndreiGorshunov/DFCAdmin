/* =============================================================================
   21_indexes_v2.sql — индексы под выборки v2. Идемпотентно. Запускать ПОСЛЕ 20.
   FK-индексы уже созданы в 20; здесь — составные/покрывающие и уникальный инвариант.
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [dfc.EventRegistration];
GO

/* Упорядоченный список точек старта внутри сессии: seek по SessionId + сортировка
   по DisplayOrder без отдельной сортировки. INCLUDE покрывает поля листинга. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_EventStartPoints_Session_Order' AND object_id = OBJECT_ID(N'dbo.EventStartPoints'))
    CREATE INDEX IX_EventStartPoints_Session_Order
        ON dbo.EventStartPoints (SessionId, DisplayOrder)
        INCLUDE (Name, StartTime, EndTime, Capacity);
GO

/* Подсчёт занятости/чек-инов по точке старта (Capacity vs выбрано/прошло чек-ин). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_RegistrationSessions_StartPoint_Checked' AND object_id = OBJECT_ID(N'dbo.RegistrationSessions'))
    CREATE INDEX IX_RegistrationSessions_StartPoint_Checked
        ON dbo.RegistrationSessions (StartPointId, CheckedIn);
GO

/* ПО РЕШЕНИЮ: регистрация МОЖЕТ выбирать одну сессию несколько раз (разные точки/слоты),
   поэтому уникального индекса на (RegistrationId, SessionId) НЕТ. Доступ по
   IX_RegistrationSessions_RegistrationId (из 20) для списка выборов одной регистрации. */
