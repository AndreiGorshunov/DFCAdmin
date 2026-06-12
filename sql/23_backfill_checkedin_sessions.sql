/* =============================================================================
   23_backfill_checkedin_sessions.sql — бэкафилл выборов сессий для CheckedIn
   -----------------------------------------------------------------------------
   Находит регистрации со статусом CheckedIn (Status = 2) и добавляет им выборы
   сессий: СЛУЧАЙНОЕ число сессий на регистрацию (от 1 до числа сессий события),
   каждой выбранной сессии — точка старта С УЧЁТОМ ОСТАТКА ЁМКОСТИ (Capacity).

   Ёмкость точки = жёсткая квота (как в приложении, AssignSessionAsync): сюда не
   назначаем сверх Capacity. Остаток считается с учётом УЖЕ существующих выборов
   (включая сид). Capacity = NULL трактуется как безлимит. Если в сессии не хватило
   мест на всех — лишние выборы этой сессии просто не создаются (видно в сводке).

   Чек-ин НЕ ставим: CheckedIn = 0, CheckInTime = NULL — участник отмечается
   непосредственно на точке сбора (через /CheckIn).

   «Случайность» детерминирована (хеш CHECKSUM), поэтому распределение выглядит
   случайным, но повторный прогон стабилен.

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
RegSeq AS
(
    -- Псевдослучайный порядковый номер регистрации внутри сессии (кому раньше достанется место).
    SELECT  RegistrationId, SessionId,
            ROW_NUMBER() OVER (PARTITION BY SessionId
                               ORDER BY CHECKSUM(RegistrationId, SessionId), RegistrationId) AS regno
    FROM ChosenSession
),
StartPointRem AS
(
    -- Остаток ёмкости точки = Capacity - уже занятые места (по существующим выборам).
    -- NULL Capacity = безлимит -> очень большое число.
    SELECT  sp.SessionId, sp.StartPointId, sp.DisplayOrder,
            CASE WHEN sp.Capacity IS NULL THEN 1000000000
                 ELSE sp.Capacity - ISNULL(ex.cnt, 0) END AS Remaining
    FROM dbo.EventStartPoints sp
    LEFT JOIN (SELECT StartPointId, COUNT(*) AS cnt
               FROM dbo.RegistrationSessions GROUP BY StartPointId) ex
           ON ex.StartPointId = sp.StartPointId
),
StartPointRanges AS
(
    -- Каждой точке отдаём непрерывный диапазон номеров [CumEnd-Remaining+1 .. CumEnd]
    -- внутри сессии (по DisplayOrder). regno из этого диапазона -> эта точка.
    SELECT  SessionId, StartPointId, Remaining,
            SUM(CAST(Remaining AS BIGINT)) OVER (PARTITION BY SessionId
                ORDER BY DisplayOrder, StartPointId ROWS UNBOUNDED PRECEDING) AS CumEnd
    FROM StartPointRem
    WHERE Remaining > 0
)
INSERT INTO dbo.RegistrationSessions (RegistrationId, SessionId, StartPointId, CheckedIn, CheckInTime)
SELECT rq.RegistrationId, rq.SessionId, spr.StartPointId, 0, NULL
FROM RegSeq rq
JOIN StartPointRanges spr
      ON spr.SessionId = rq.SessionId
     AND rq.regno BETWEEN (spr.CumEnd - spr.Remaining + 1) AND spr.CumEnd;   -- regno > суммы остатков -> сессия полна, выбор не создаётся

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

-- Пропущенные #1: CheckedIn без выборов, чьё событие НЕ имеет сессий (назначать нечего).
SELECT COUNT(*) AS CheckedIn_Skipped_NoSessions
FROM dbo.EventRegistrations r
WHERE r.Status = 2
  AND NOT EXISTS (SELECT 1 FROM dbo.RegistrationSessions rs WHERE rs.RegistrationId = r.RegistrationId)
  AND NOT EXISTS (SELECT 1 FROM dbo.EventSessions es WHERE es.EventId = r.EventId);

-- Пропущенные #2: CheckedIn без выборов, хотя у события ЕСТЬ сессии
-- (не хватило мест по квотам — точки заполнены).
SELECT COUNT(*) AS CheckedIn_Skipped_NoCapacity
FROM dbo.EventRegistrations r
WHERE r.Status = 2
  AND NOT EXISTS (SELECT 1 FROM dbo.RegistrationSessions rs WHERE rs.RegistrationId = r.RegistrationId)
  AND EXISTS (SELECT 1 FROM dbo.EventSessions es WHERE es.EventId = r.EventId);
GO
