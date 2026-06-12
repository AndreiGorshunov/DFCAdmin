/* =============================================================================
   23_backfill_checkedin_sessions.sql — бэкафилл выборов сессий для CheckedIn
   -----------------------------------------------------------------------------
   Находит регистрации со статусом CheckedIn (Status = 2) и добавляет им выборы
   сессий: СЛУЧАЙНОЕ число сессий на регистрацию (от 1 до числа сессий события),
   каждой выбранной сессии — случайная точка старта.

   Чек-ин НЕ ставим: CheckedIn = 0, CheckInTime = NULL — участник отмечается
   непосредственно на точке сбора (через /CheckIn).

   «Случайность» детерминирована (хеш CHECKSUM от RegistrationId/SessionId/...),
   поэтому распределение выглядит случайным, но повторный прогон стабилен.

   ИДЕМПОТЕНТНО: вставляет только тем CheckedIn-регистрациям, у которых ЕЩЁ НЕТ
   ни одного выбора сессии (NOT EXISTS) — повторный прогон ничего не дублирует.

   Запускать ПОСЛЕ 20 (таблицы v2) и ПОСЛЕ наполнения сессий/точек (24 и/или 22).
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [dfc.EventRegistration];
GO

DECLARE @CheckedInStatus TINYINT = 2;   -- RegistrationStatus.CheckedIn

/* (CHECKSUM(...) % k + k) % k -> 0..k-1 без ABS (CHECKSUM может вернуть int.MinValue). */
;WITH
SessionCount AS
(
    SELECT EventId, COUNT(*) AS Cnt
    FROM dbo.EventSessions
    GROUP BY EventId
),
Targets AS
(
    -- CheckedIn-регистрации без выборов + сколько сессий им назначить (1..Cnt, псевдослучайно).
    SELECT  r.RegistrationId,
            r.EventId,
            1 + ((CHECKSUM(r.RegistrationId) % sc.Cnt + sc.Cnt) % sc.Cnt) AS TargetCount
    FROM dbo.EventRegistrations r
    JOIN SessionCount sc ON sc.EventId = r.EventId       -- события без сессий отсеиваются
    WHERE r.Status = @CheckedInStatus
      AND NOT EXISTS (SELECT 1 FROM dbo.RegistrationSessions rs
                      WHERE rs.RegistrationId = r.RegistrationId)
),
RankedSessions AS
(
    -- Сессии события в псевдослучайном порядке для каждой регистрации.
    SELECT  t.RegistrationId, t.TargetCount, s.SessionId,
            ROW_NUMBER() OVER (PARTITION BY t.RegistrationId
                               ORDER BY CHECKSUM(t.RegistrationId, s.SessionId), s.SessionId) AS srank
    FROM Targets t
    JOIN dbo.EventSessions s ON s.EventId = t.EventId
),
ChosenSession AS
(
    -- Берём первые TargetCount сессий -> 1..все, по-разному у разных регистраций.
    SELECT RegistrationId, SessionId
    FROM RankedSessions
    WHERE srank <= TargetCount
),
ChosenStartPoint AS
(
    -- Для каждой выбранной сессии — псевдослучайная точка старта (rn = 1).
    SELECT  cs.RegistrationId, cs.SessionId, sp.StartPointId,
            ROW_NUMBER() OVER (PARTITION BY cs.RegistrationId, cs.SessionId
                               ORDER BY CHECKSUM(cs.RegistrationId, sp.StartPointId), sp.StartPointId) AS rn
    FROM ChosenSession cs
    JOIN dbo.EventStartPoints sp ON sp.SessionId = cs.SessionId
)
INSERT INTO dbo.RegistrationSessions (RegistrationId, SessionId, StartPointId, CheckedIn, CheckInTime)
SELECT RegistrationId, SessionId, StartPointId, 0, NULL
FROM ChosenStartPoint
WHERE rn = 1;

PRINT CONCAT(N'Добавлено выборов сессий: ', @@ROWCOUNT);
GO

/* --- Сводки --------------------------------------------------------------- */

-- Распределение: сколько регистраций получили N сессий.
SELECT q.SessionsPerReg, COUNT(*) AS Registrations
FROM (
    SELECT rs.RegistrationId, COUNT(*) AS SessionsPerReg
    FROM dbo.RegistrationSessions rs
    JOIN dbo.EventRegistrations r ON r.RegistrationId = rs.RegistrationId AND r.Status = 2
    GROUP BY rs.RegistrationId
) q
GROUP BY q.SessionsPerReg
ORDER BY q.SessionsPerReg;

-- Пропущенные: CheckedIn без выборов, чьё событие не имеет сессий (назначать нечего).
SELECT COUNT(*) AS CheckedIn_Skipped_NoSessions
FROM dbo.EventRegistrations r
WHERE r.Status = 2
  AND NOT EXISTS (SELECT 1 FROM dbo.RegistrationSessions rs WHERE rs.RegistrationId = r.RegistrationId)
  AND NOT EXISTS (SELECT 1 FROM dbo.EventSessions es WHERE es.EventId = r.EventId);
GO
