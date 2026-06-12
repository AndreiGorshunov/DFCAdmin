/* =============================================================================
   22_seed_v2.sql — тестовый сид сессий и точек старта для ПЕРВОГО события.
   Идемпотентно (MERGE по натуральным ключам). Запускать ПОСЛЕ 20 и при наличии
   хотя бы одного события (01->02/03 уже прогнаны). Один батч — переменные живут.
   ============================================================================= */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [dfc.EventRegistration];
GO

DECLARE @EventId UNIQUEIDENTIFIER = (SELECT TOP (1) Id FROM dbo.Events ORDER BY StartDate);

IF @EventId IS NULL
BEGIN
    PRINT 'Нет событий: создай событие (UI или 02/03), затем запусти 22_seed_v2.sql.';
    RETURN;
END

/* --- Сессии --- */
MERGE dbo.EventSessions AS t
USING (VALUES
    (@EventId, N'5K Run', N'5 km run',     500),
    (@EventId, N'Yoga',   N'Morning yoga', 100)
) AS s(EventId, Name, Description, MaxParticipants)
ON t.EventId = s.EventId AND t.Name = s.Name
WHEN NOT MATCHED THEN
    INSERT (EventId, Name, Description, MaxParticipants)
    VALUES (s.EventId, s.Name, s.Description, s.MaxParticipants);

DECLARE @run  INT = (SELECT SessionId FROM dbo.EventSessions WHERE EventId = @EventId AND Name = N'5K Run');
DECLARE @yoga INT = (SELECT SessionId FROM dbo.EventSessions WHERE EventId = @EventId AND Name = N'Yoga');

/* --- Точки старта --- */
MERGE dbo.EventStartPoints AS t
USING (VALUES
    (@run,  N'Wave A', CAST('07:00' AS TIME(0)), CAST('07:30' AS TIME(0)), 250, 1),
    (@run,  N'Wave B', CAST('07:30' AS TIME(0)), CAST('08:00' AS TIME(0)), 250, 2),
    (@yoga, N'Lawn',   CAST('09:00' AS TIME(0)), CAST('10:00' AS TIME(0)), 100, 1)
) AS s(SessionId, Name, StartTime, EndTime, Capacity, DisplayOrder)
ON t.SessionId = s.SessionId AND t.Name = s.Name
WHEN NOT MATCHED THEN
    INSERT (SessionId, Name, StartTime, EndTime, Capacity, DisplayOrder)
    VALUES (s.SessionId, s.Name, s.StartTime, s.EndTime, s.Capacity, s.DisplayOrder);

PRINT 'Сид сессий/точек старта готов для события:';
SELECT @EventId AS EventId;
SELECT SessionId, Name, MaxParticipants FROM dbo.EventSessions WHERE EventId = @EventId;
SELECT sp.StartPointId, sp.SessionId, sp.Name, sp.StartTime, sp.EndTime, sp.Capacity, sp.DisplayOrder
FROM dbo.EventStartPoints sp
JOIN dbo.EventSessions es ON es.SessionId = sp.SessionId
WHERE es.EventId = @EventId
ORDER BY sp.SessionId, sp.DisplayOrder;
GO
