/* =============================================================================
   25_unique_reg_session.sql — одна точка старта на сессию для регистрации
   -----------------------------------------------------------------------------
   Правило: регистрация участвует в 1..N РАЗНЫХ сессиях, но в каждой сессии — ровно
   ОДНА точка старта. Дублей (та же сессия дважды, в т.ч. на ту же точку) быть не должно.

   Шаг 1: дедуп существующих строк — оставляем по одной на (RegistrationId, SessionId),
          предпочитая уже отмеченную чек-ином (CheckedIn=1), затем наименьший Id.
   Шаг 2: уникальный индекс UX_RegistrationSessions_Reg_Session (RegistrationId, SessionId)
          — жёсткая гарантия на уровне БД.

   Идемпотентно: дедуп безопасен при повторе, индекс создаётся IF NOT EXISTS.
   Запускать ПОСЛЕ 20 (таблица RegistrationSessions). Если уже прогоняли 23 — тоже ок,
   он создаёт только различные (Reg+Session), нарушений не вносит.
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [dfc.EventRegistration];
GO

/* --- Шаг 1: убрать дубли (Reg+Session), оставив «лучшую» строку ------------ */
;WITH ranked AS
(
    SELECT  RegistrationSessionId,
            ROW_NUMBER() OVER (PARTITION BY RegistrationId, SessionId
                               ORDER BY CheckedIn DESC, RegistrationSessionId ASC) AS rn
    FROM dbo.RegistrationSessions
)
DELETE FROM dbo.RegistrationSessions
WHERE RegistrationSessionId IN (SELECT RegistrationSessionId FROM ranked WHERE rn > 1);

PRINT CONCAT(N'Удалено дублей (Reg+Session): ', @@ROWCOUNT);
GO

/* --- Шаг 2: уникальный индекс (одна точка старта на сессию) ---------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_RegistrationSessions_Reg_Session'
                 AND object_id = OBJECT_ID(N'dbo.RegistrationSessions'))
BEGIN
    CREATE UNIQUE INDEX UX_RegistrationSessions_Reg_Session
        ON dbo.RegistrationSessions (RegistrationId, SessionId);
    PRINT 'Создан UX_RegistrationSessions_Reg_Session.';
END
ELSE
    PRINT 'UX_RegistrationSessions_Reg_Session уже существует.';
GO
