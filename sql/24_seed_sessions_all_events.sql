/* =============================================================================
   24_seed_sessions_all_events.sql — сид сессий и точек старта для ВСЕХ событий
   -----------------------------------------------------------------------------
   Для каждого события добавляет 2–4 сессии, каждой сессии — 1–5 точек старта.
   MaxParticipants сессии = сумма Capacity её точек старта (шаг 3 — согласованно).
   Количество детерминировано (от CHECKSUM id), поэтому повторный прогон стабилен.
   Идемпотентно: вставка по натуральным ключам (EventId+Name, SessionId+Name) с
   NOT EXISTS — дубликатов не будет.

   Считаем без ABS (CHECKSUM может вернуть int.MinValue -> ABS переполнится):
     (CHECKSUM(x) % k + k) % k  -> гарантированно 0..k-1.

   Запускать ПОСЛЕ 20 (таблицы v2) и при наличии событий (01 -> 02/03).
   Это «широкий» сид (Session N / Wave M по всем событиям); 22_seed_v2 — отдельный
   мелкий ручной сид первого события (5K Run / Yoga), они не конфликтуют по именам.
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [dfc.EventRegistration];
GO

/* --- Шаг 1: 2..4 сессии на каждое событие --------------------------------- */
;WITH Nums AS (SELECT n FROM (VALUES (1),(2),(3),(4),(5)) AS t(n))
INSERT INTO dbo.EventSessions (EventId, Name, Description, MaxParticipants)
SELECT  e.Id,
        CONCAT(N'Session ', n.n),
        CONCAT(N'Auto-seeded session ', n.n),
        NULL                                   -- MaxParticipants пересчитаем в шаге 3 = Σ Capacity точек
FROM dbo.Events e
JOIN Nums n
      ON n.n <= 2 + ((CHECKSUM(e.Id) % 3 + 3) % 3)          -- 2..4 сессии
WHERE NOT EXISTS (SELECT 1 FROM dbo.EventSessions s
                  WHERE s.EventId = e.Id AND s.Name = CONCAT(N'Session ', n.n));

PRINT CONCAT(N'Добавлено сессий: ', @@ROWCOUNT);
GO

/* --- Шаг 2: 1..5 точек старта на каждую сессию ----------------------------- */
;WITH Nums AS (SELECT n FROM (VALUES (1),(2),(3),(4),(5)) AS t(n))
INSERT INTO dbo.EventStartPoints (SessionId, Name, StartTime, EndTime, Capacity, DisplayOrder)
SELECT  s.SessionId,
        CONCAT(N'Wave ', n.n),
        DATEADD(MINUTE, (n.n - 1) * 30,      CAST('07:00' AS TIME(0))),   -- 07:00, 07:30, ...
        DATEADD(MINUTE, (n.n - 1) * 30 + 30, CAST('07:00' AS TIME(0))),   -- +30 мин
        100 + 50 * n.n,                                                   -- ёмкость 150..350
        n.n                                                               -- DisplayOrder
FROM dbo.EventSessions s
JOIN Nums n
      ON n.n <= 1 + ((CHECKSUM(s.SessionId) % 5 + 5) % 5)  -- 1..5 точек
WHERE NOT EXISTS (SELECT 1 FROM dbo.EventStartPoints sp
                  WHERE sp.SessionId = s.SessionId AND sp.Name = CONCAT(N'Wave ', n.n));

PRINT CONCAT(N'Добавлено точек старта: ', @@ROWCOUNT);
GO

/* --- Шаг 3: MaxParticipants сессии = сумма Capacity её точек старта ---------
   Точки старта разбивают сессию -> лимит сессии = сумма лимитов точек.
   Фильтр по 'Session %' трогает только авто-сид этого скрипта; убери его,
   чтобы пересчитать MaxParticipants у ВСЕХ сессий, у которых есть точки. */
UPDATE es
SET es.MaxParticipants = sp.TotalCapacity
FROM dbo.EventSessions es
JOIN (
    SELECT SessionId, SUM(Capacity) AS TotalCapacity
    FROM dbo.EventStartPoints
    GROUP BY SessionId
) sp ON sp.SessionId = es.SessionId
WHERE es.Name LIKE N'Session %';

PRINT CONCAT(N'Пересчитан Max у сессий: ', @@ROWCOUNT);
GO

/* --- Сводка --------------------------------------------------------------- */
SELECT  e.Id AS EventId,
        e.Name AS EventName,
        COUNT(DISTINCT s.SessionId)  AS Sessions,
        COUNT(sp.StartPointId)       AS StartPoints
FROM dbo.Events e
LEFT JOIN dbo.EventSessions    s  ON s.EventId  = e.Id
LEFT JOIN dbo.EventStartPoints sp ON sp.SessionId = s.SessionId
GROUP BY e.Id, e.Name
ORDER BY e.Name;
GO
