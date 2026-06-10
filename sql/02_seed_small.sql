/* =============================================================================
   Dubai Fitness Challenge 2026 — Seed / Dummy Data
   Target DB: [dfc.EventRegistration]   (имя содержит точку -> только в скобках)
   СУБД: Microsoft SQL Server (T-SQL)
   -----------------------------------------------------------------------------
   Что генерирует:
     * 5 событий (marathon / 10K run / cycling / open-water swim / triathlon)
     * @UserCount пользователей (по умолчанию 10)
     * 0..3 family members на пользователя
     * 1..5 РАЗНЫХ событий на пользователя (регистрации)
     * Participants: 1 родитель (FamilyMemberId = NULL) + случайные дети
   -----------------------------------------------------------------------------
   Параметры (правятся здесь):
     @UserCount  — сколько пользователей сгенерировать
     @ResetData  — 1 = очистить эти 5 таблиц перед заливкой (это seed-скрипт!)

   Семантика Status (заглушка, поправь под реальный enum):
     0 = Pending, 1 = Confirmed, 2 = CheckedIn, 3 = Cancelled
     QRCode выдаётся только для Confirmed/CheckedIn, иначе NULL.
   ============================================================================= */

USE [dfc.EventRegistration];
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @UserCount int = 10;     -- <-- меняй для нагрузочных данных (см. блок про 1M внизу)
DECLARE @ResetData bit = 1;      -- <-- 1 = wipe перед заливкой

/* -----------------------------------------------------------------------------
   0. Reset (порядок учитывает FK)
   -------------------------------------------------------------------------- */
IF @ResetData = 1
BEGIN
    DELETE FROM dbo.RegistrationParticipants;
    DELETE FROM dbo.EventRegistrations;
    DELETE FROM dbo.FamilyMembers;
    DELETE FROM dbo.Events;
    DELETE FROM dbo.Users;

    DBCC CHECKIDENT (N'dbo.Users',                     RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT (N'dbo.FamilyMembers',             RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT (N'dbo.EventRegistrations',        RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT (N'dbo.RegistrationParticipants',  RESEED, 0) WITH NO_INFOMSGS;
END

BEGIN TRY
BEGIN TRANSACTION;

/* =============================================================================
   1. EVENTS  (Id — фиксированные GUID, как будто из внешней системы)
   ============================================================================= */
DECLARE
    @EvMarathon  uniqueidentifier = 'A1000000-0000-4000-8000-000000000001',
    @Ev10K       uniqueidentifier = 'A1000000-0000-4000-8000-000000000002',
    @EvCycling   uniqueidentifier = 'A1000000-0000-4000-8000-000000000003',
    @EvSwim      uniqueidentifier = 'A1000000-0000-4000-8000-000000000004',
    @EvTriathlon uniqueidentifier = 'A1000000-0000-4000-8000-000000000005';

INSERT INTO dbo.Events (Id, Name, Description, StartDate, EndDate, RegistrationOpeningDate)
VALUES
 (@EvMarathon,  N'DFC Dubai Marathon 2026',      N'Full 42.195 km marathon along Sheikh Zayed Road.', '2026-11-14T06:00:00', '2026-11-14T14:00:00', '2026-08-15T00:00:00'),
 (@Ev10K,       N'DFC Dubai 10K Run 2026',       N'Community 10 km road race at Dubai Marina.',        '2026-11-08T07:00:00', '2026-11-08T11:00:00', '2026-08-15T00:00:00'),
 (@EvCycling,   N'DFC Dubai Cycle Tour 2026',    N'60 km open-road cycling event.',                    '2026-11-15T06:30:00', '2026-11-15T12:00:00', '2026-08-15T00:00:00'),
 (@EvSwim,      N'DFC Open Water Swim 2026',     N'2 km open-water swim at Kite Beach.',               '2026-11-09T07:30:00', '2026-11-09T11:30:00', '2026-08-15T00:00:00'),
 (@EvTriathlon, N'DFC Sprint Triathlon 2026',    N'Sprint: 750 m swim / 20 km bike / 5 km run.',       '2026-11-21T06:00:00', '2026-11-21T13:00:00', '2026-08-15T00:00:00');

/* =============================================================================
   2. Справочники имён (table variables -> легко расширять)
   ============================================================================= */
DECLARE @First TABLE (i int PRIMARY KEY, nm nvarchar(50));
INSERT INTO @First VALUES
 (0,N'Ahmed'),(1,N'Mohammed'),(2,N'Omar'),(3,N'Khalid'),(4,N'Yusuf'),(5,N'Saeed'),
 (6,N'Fatima'),(7,N'Aisha'),(8,N'Layla'),(9,N'Noura'),(10,N'Mariam'),(11,N'Hessa'),
 (12,N'John'),(13,N'Sarah'),(14,N'Raj'),(15,N'Priya');

DECLARE @Last TABLE (i int PRIMARY KEY, nm nvarchar(50));
INSERT INTO @Last VALUES
 (0,N'Al Maktoum'),(1,N'Al Nahyan'),(2,N'Al Suwaidi'),(3,N'Khan'),(4,N'Rahman'),(5,N'Hassan'),
 (6,N'Smith'),(7,N'Johnson'),(8,N'Patel'),(9,N'Sharma'),(10,N'Chen'),(11,N'Nguyen'),
 (12,N'Ibrahim'),(13,N'Saleh'),(14,N'Mansoori'),(15,N'Qassimi');

-- Детские имена: часть латиницей, часть арабской вязью (Unicode/RTL test data)
DECLARE @Child TABLE (i int PRIMARY KEY, nm nvarchar(50));
INSERT INTO @Child VALUES
 (0,N'Sara'),(1,N'Yousef'),(2,N'Lina'),(3,N'Adam'),(4,N'Maya'),(5,N'Zayd'),
 (6,N'علي'),(7,N'مريم'),(8,N'نور'),(9,N'حمزة'),(10,N'سلمى'),(11,N'يوسف');

DECLARE @FirstN int = (SELECT COUNT(*) FROM @First);
DECLARE @LastN  int = (SELECT COUNT(*) FROM @Last);
DECLARE @ChildN int = (SELECT COUNT(*) FROM @Child);

/* =============================================================================
   3. USERS
   ============================================================================= */
;WITH Nums AS
(
    SELECT TOP (@UserCount)
           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS rn   -- 0-based
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.Users (FirstName, LastName, Email, Phone, DateOfBirth)
SELECT
    f.nm,
    l.nm,
    LOWER(CONCAT(f.nm, N'.', REPLACE(l.nm, N' ', N''), n.rn + 1, N'@example.ae')),
    CONCAT(N'+97150', RIGHT(N'0000000' + CAST(ABS(CHECKSUM(NEWID())) % 10000000 AS nvarchar(7)), 7)),
    DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 10585, '1975-01-01')   -- adult: 1975..2003
FROM Nums n
JOIN @First f ON f.i = n.rn % @FirstN
JOIN @Last  l ON l.i = (n.rn * 7 + 3) % @LastN;                  -- сдвиг -> другие пары

/* =============================================================================
   4. FAMILY MEMBERS  (0..3 на пользователя, фамилия наследуется от родителя)
   ============================================================================= */
;WITH FmCount AS
(
    SELECT UserId, LastName,
           ABS(CHECKSUM(NEWID())) % 4 AS fmCount     -- 0..3
    FROM dbo.Users
),
Seq AS (SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3)
INSERT INTO dbo.FamilyMembers (UserId, FirstName, LastName, DateOfBirth)
SELECT
    c.UserId,
    (SELECT nm FROM @Child WHERE i = ABS(CHECKSUM(NEWID())) % @ChildN),
    c.LastName,
    DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 4017, '2013-01-01')    -- ребёнок: 2013..2023
FROM FmCount c
JOIN Seq s ON s.n <= c.fmCount;

/* =============================================================================
   5. EVENT REGISTRATIONS  (1..5 РАЗНЫХ событий на пользователя)
   ============================================================================= */
;WITH UserKeep AS
(
    SELECT UserId, (ABS(CHECKSUM(NEWID())) % 5) + 1 AS KeepCount  -- 1..5
    FROM dbo.Users
),
Ranked AS
(
    SELECT u.UserId, e.Id AS EventId, e.RegistrationOpeningDate,
           ROW_NUMBER() OVER (PARTITION BY u.UserId ORDER BY NEWID()) AS rnk
    FROM dbo.Users u
    CROSS JOIN dbo.Events e
)
INSERT INTO dbo.EventRegistrations
    (EventId, UserId, GroupCode,
     EmergencyContactFirstName, EmergencyContactLastName, EmergencyContactPhone,
     RegistrationDate, Status, QRCode)
SELECT
    r.EventId,
    r.UserId,
    CASE WHEN ABS(CHECKSUM(NEWID())) % 10 < 4
         THEN CONCAT(N'GRP-', FORMAT(r.UserId, '000')) END,                       -- ~40% в группе
    (SELECT nm FROM @First WHERE i = ABS(CHECKSUM(NEWID())) % @FirstN),
    (SELECT nm FROM @Last  WHERE i = ABS(CHECKSUM(NEWID())) % @LastN),
    CONCAT(N'+97155', RIGHT(N'0000000' + CAST(ABS(CHECKSUM(NEWID())) % 10000000 AS nvarchar(7)), 7)),
    DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 80, r.RegistrationOpeningDate),         -- открытие + 0..80 дней (< StartDate)
    ABS(CHECKSUM(NEWID())) % 4,                                                   -- Status 0..3
    NULL                                                                         -- QR выставим ниже
FROM Ranked r
JOIN UserKeep k ON k.UserId = r.UserId
WHERE r.rnk <= k.KeepCount;

-- QR-код только для подтверждённых/чек-инов; уникален (фильтрованный UNIQUE index)
UPDATE dbo.EventRegistrations
SET    QRCode = CONCAT(N'DFC26-', FORMAT(RegistrationId, '0000000'))
WHERE  Status IN (1, 2);

/* =============================================================================
   6. REGISTRATION PARTICIPANTS
      6a. Родитель — по 1 строке на каждую регистрацию (FamilyMemberId = NULL)
   ============================================================================= */
INSERT INTO dbo.RegistrationParticipants (RegistrationId, EventId, FamilyMemberId, TshirtSize)
SELECT
    r.RegistrationId,
    r.EventId,                         -- денормализованный EventId
    NULL,                              -- Parent
    CHOOSE((ABS(CHECKSUM(NEWID())) % 6) + 1, N'XS', N'S', N'M', N'L', N'XL', N'XXL')
FROM dbo.EventRegistrations r;

/* 6b. Дети — случайное подмножество family members того же пользователя (~50%) */
INSERT INTO dbo.RegistrationParticipants (RegistrationId, EventId, FamilyMemberId, TshirtSize)
SELECT
    r.RegistrationId,
    r.EventId,
    fm.FamilyMemberId,                 -- Child
    CHOOSE((ABS(CHECKSUM(NEWID())) % 6) + 1, N'XS', N'S', N'M', N'L', N'XL', N'XXL')
FROM dbo.EventRegistrations r
JOIN dbo.FamilyMembers fm ON fm.UserId = r.UserId
WHERE ABS(CHECKSUM(NEWID())) % 2 = 0;

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

/* =============================================================================
   7. Сводка
   ============================================================================= */
SELECT 'Users' AS [Table], COUNT(*) AS [Rows] FROM dbo.Users
UNION ALL SELECT 'Events',                    COUNT(*) FROM dbo.Events
UNION ALL SELECT 'FamilyMembers',             COUNT(*) FROM dbo.FamilyMembers
UNION ALL SELECT 'EventRegistrations',        COUNT(*) FROM dbo.EventRegistrations
UNION ALL SELECT 'RegistrationParticipants',  COUNT(*) FROM dbo.RegistrationParticipants;

-- Контроль: регистраций на пользователя (ожидаем 1..5)
SELECT u.UserId, u.FirstName, u.LastName,
       COUNT(r.RegistrationId)                                   AS Registrations,
       (SELECT COUNT(*) FROM dbo.FamilyMembers fm WHERE fm.UserId = u.UserId) AS FamilyMembers
FROM dbo.Users u
LEFT JOIN dbo.EventRegistrations r ON r.UserId = u.UserId
GROUP BY u.UserId, u.FirstName, u.LastName
ORDER BY u.UserId;
