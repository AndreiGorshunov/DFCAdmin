/* =============================================================================
   Dubai Fitness Challenge 2026 — BULK LOAD HARNESS (~1M регистраций)
   Target DB: [dfc.EventRegistration]   |   СУБД: Microsoft SQL Server (T-SQL)
   -----------------------------------------------------------------------------
   Отличия от обычного seed-скрипта:
     * Заливка БАТЧАМИ в цикле (лог не раздувается, блокировки короткие).
     * НЕТ ORDER BY NEWID() в горячем пути — детерминированный хэш-разброс
       (Knuth multiplicative + CHECKSUM), воспроизводимо и без сортировок tempdb.
     * Управление индексами: лишние NC-индексы DISABLE на время загрузки,
       REBUILD после. IX_FamilyMembers_UserId оставляем — он нужен джойну
       при генерации детей-участников.
     * Опциональное переключение recovery model в BULK_LOGGED (по умолчанию OFF
       — трогать recovery в проде это сознательное решение).
     * TRY/CATCH: при ошибке индексы перестраиваются и recovery возвращается.
   -----------------------------------------------------------------------------
   Объёмы (при дефолтах): ~350k users x avg 3 рег = ~1.05M EventRegistrations,
   ~0.5M FamilyMembers, ~1.5–2M RegistrationParticipants.
   -----------------------------------------------------------------------------
   ВАЖНО про minimal logging: реальная выгода от TABLOCK будет только при
   recovery model SIMPLE или BULK_LOGGED. На FULL лог всё равно вырастет —
   тогда либо @SwitchRecovery = 1, либо заранее увеличь файл лога.
   ============================================================================= */

USE [dfc.EventRegistration];
SET NOCOUNT ON;
SET XACT_ABORT ON;

-------------------------------------------------------------------------------
-- ПАРАМЕТРЫ
-------------------------------------------------------------------------------
DECLARE @UserCount      int  = 350000;   -- ~1.05M регистраций при avg 3/user
DECLARE @BatchSize      int  = 50000;    -- пользователей за один батч/транзакцию
DECLARE @ResetData      bit  = 1;        -- очистить таблицы перед загрузкой
DECLARE @ManageIndexes  bit  = 1;        -- DISABLE/REBUILD лишних NC-индексов
DECLARE @SwitchRecovery bit  = 0;        -- 1 = временно BULK_LOGGED (нужны права!)

-------------------------------------------------------------------------------
-- Хэш-паттерн (детерминированный, без NEWID):
--   ((CHECKSUM(CONVERT(bigint, <seed>) * 2654435761 + <salt>)) & 0x7FFFFFFF)
-- * умножение на большое нечётное (Knuth) разносит соседние seed далеко
-- * & 0x7FFFFFFF снимает знак без риска ABS(MIN_INT)
-- Разные <salt> = независимые "потоки" псевдослучайности для разных полей.
-------------------------------------------------------------------------------

DECLARE @t0 datetime2 = SYSDATETIME();
DECLARE @msg nvarchar(200);
DECLARE @OrigRecovery sysname =
    (SELECT recovery_model_desc FROM sys.databases WHERE database_id = DB_ID());

-------------------------------------------------------------------------------
-- Справочники имён (temp tables — живут весь скрипт)
-------------------------------------------------------------------------------
DROP TABLE IF EXISTS #First, #Last, #Child, #Events, #Idx;

CREATE TABLE #First (i int PRIMARY KEY, nm nvarchar(50));
INSERT INTO #First VALUES
 (0,N'Ahmed'),(1,N'Mohammed'),(2,N'Omar'),(3,N'Khalid'),(4,N'Yusuf'),(5,N'Saeed'),
 (6,N'Fatima'),(7,N'Aisha'),(8,N'Layla'),(9,N'Noura'),(10,N'Mariam'),(11,N'Hessa'),
 (12,N'John'),(13,N'Sarah'),(14,N'Raj'),(15,N'Priya');

CREATE TABLE #Last (i int PRIMARY KEY, nm nvarchar(50));
INSERT INTO #Last VALUES
 (0,N'AlMaktoum'),(1,N'AlNahyan'),(2,N'AlSuwaidi'),(3,N'Khan'),(4,N'Rahman'),(5,N'Hassan'),
 (6,N'Smith'),(7,N'Johnson'),(8,N'Patel'),(9,N'Sharma'),(10,N'Chen'),(11,N'Nguyen'),
 (12,N'Ibrahim'),(13,N'Saleh'),(14,N'Mansoori'),(15,N'Qassimi');

CREATE TABLE #Child (i int PRIMARY KEY, nm nvarchar(50));
INSERT INTO #Child VALUES   -- часть арабской вязью: Unicode/RTL тест-данные
 (0,N'Sara'),(1,N'Yousef'),(2,N'Lina'),(3,N'Adam'),(4,N'Maya'),(5,N'Zayd'),
 (6,N'علي'),(7,N'مريم'),(8,N'نور'),(9,N'حمزة'),(10,N'سلمى'),(11,N'يوسف');

DECLARE @FirstN int = (SELECT COUNT(*) FROM #First);
DECLARE @LastN  int = (SELECT COUNT(*) FROM #Last);
DECLARE @ChildN int = (SELECT COUNT(*) FROM #Child);

-------------------------------------------------------------------------------
-- Список индексов под DISABLE/REBUILD (явный — предсказуемо).
-- IX_FamilyMembers_UserId НЕ отключаем: его использует джойн генерации детей.
-------------------------------------------------------------------------------
CREATE TABLE #Idx (TableName sysname, IndexName sysname);
INSERT INTO #Idx VALUES
 (N'Users',                     N'UX_Users_Email'),
 (N'EventRegistrations',        N'IX_EventRegistrations_EventId'),
 (N'EventRegistrations',        N'IX_EventRegistrations_UserId'),
 (N'EventRegistrations',        N'UX_EventRegistrations_QRCode'),
 (N'RegistrationParticipants',  N'IX_RegistrationParticipants_RegistrationId'),
 (N'RegistrationParticipants',  N'IX_RegistrationParticipants_FamilyMemberId'),
 (N'RegistrationParticipants',  N'IX_RegistrationParticipants_EventId');

DECLARE @sql nvarchar(max);

BEGIN TRY
    ---------------------------------------------------------------------------
    -- 0. Recovery model (опционально)
    ---------------------------------------------------------------------------
    IF @SwitchRecovery = 1 AND @OrigRecovery NOT IN (N'SIMPLE', N'BULK_LOGGED')
    BEGIN
        ALTER DATABASE CURRENT SET RECOVERY BULK_LOGGED;
        RAISERROR(N'Recovery model -> BULK_LOGGED (было %s)', 10, 1, @OrigRecovery) WITH NOWAIT;
    END

    ---------------------------------------------------------------------------
    -- 1. Reset
    ---------------------------------------------------------------------------
    IF @ResetData = 1
    BEGIN
        TRUNCATE TABLE dbo.RegistrationParticipants;
        DELETE FROM dbo.EventRegistrations;
        DELETE FROM dbo.FamilyMembers;
        DELETE FROM dbo.Events;
        DELETE FROM dbo.Users;
        DBCC CHECKIDENT (N'dbo.Users',                    RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT (N'dbo.FamilyMembers',            RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT (N'dbo.EventRegistrations',       RESEED, 0) WITH NO_INFOMSGS;
        DBCC CHECKIDENT (N'dbo.RegistrationParticipants', RESEED, 0) WITH NO_INFOMSGS;
        RAISERROR(N'Tables reset.', 10, 1) WITH NOWAIT;
    END

    ---------------------------------------------------------------------------
    -- 2. Events (5 шт., GUID из "внешней системы")
    ---------------------------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM dbo.Events)
        INSERT INTO dbo.Events (Id, Name, Description, StartDate, EndDate, RegistrationOpeningDate)
        VALUES
         ('A1000000-0000-4000-8000-000000000001', N'DFC Dubai Marathon 2026',   N'Full 42.195 km marathon.', '2026-11-14T06:00:00','2026-11-14T14:00:00','2026-08-15T00:00:00'),
         ('A1000000-0000-4000-8000-000000000002', N'DFC Dubai 10K Run 2026',     N'10 km road race.',         '2026-11-08T07:00:00','2026-11-08T11:00:00','2026-08-15T00:00:00'),
         ('A1000000-0000-4000-8000-000000000003', N'DFC Dubai Cycle Tour 2026',  N'60 km cycling.',           '2026-11-15T06:30:00','2026-11-15T12:00:00','2026-08-15T00:00:00'),
         ('A1000000-0000-4000-8000-000000000004', N'DFC Open Water Swim 2026',   N'2 km open-water swim.',    '2026-11-09T07:30:00','2026-11-09T11:30:00','2026-08-15T00:00:00'),
         ('A1000000-0000-4000-8000-000000000005', N'DFC Sprint Triathlon 2026',  N'750m/20km/5km.',           '2026-11-21T06:00:00','2026-11-21T13:00:00','2026-08-15T00:00:00');

    -- ординалы событий для детерминированного выбора (вместо ORDER BY NEWID)
    SELECT Ordinal = ROW_NUMBER() OVER (ORDER BY StartDate, Id),
           Id, RegistrationOpeningDate AS RegOpen
    INTO #Events
    FROM dbo.Events;

    ---------------------------------------------------------------------------
    -- 3. DISABLE индексов
    ---------------------------------------------------------------------------
    IF @ManageIndexes = 1
    BEGIN
        SET @sql = N'';
        SELECT @sql = @sql + N'ALTER INDEX ' + QUOTENAME(IndexName)
                           + N' ON dbo.' + QUOTENAME(TableName) + N' DISABLE;' + CHAR(10)
        FROM #Idx x
        WHERE EXISTS (SELECT 1 FROM sys.indexes i
                      WHERE i.object_id = OBJECT_ID(N'dbo.' + x.TableName)
                        AND i.name = x.IndexName);
        IF @sql <> N'' EXEC sys.sp_executesql @sql;
        RAISERROR(N'Indexes disabled.', 10, 1) WITH NOWAIT;
    END

    ---------------------------------------------------------------------------
    -- 4. ОСНОВНОЙ ЦИКЛ
    ---------------------------------------------------------------------------
    DECLARE @Done int = 0, @ThisBatch int, @Batch int = 0;
    DECLARE @MaxUser int, @MaxReg bigint;

    WHILE @Done < @UserCount
    BEGIN
        SET @ThisBatch = CASE WHEN @UserCount - @Done < @BatchSize
                              THEN @UserCount - @Done ELSE @BatchSize END;
        SET @Batch += 1;

        BEGIN TRANSACTION;

        SET @MaxUser = ISNULL((SELECT MAX(UserId) FROM dbo.Users), 0);

        ---- 4a. USERS (генерация от глобального ординала n) -------------------
        ;WITH Nums AS
        (
            SELECT TOP (@ThisBatch)
                   (@Done + ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) AS n
            FROM sys.all_objects a CROSS JOIN sys.all_objects b
        ),
        G AS
        (
            SELECT n,
                   ((CHECKSUM(CONVERT(bigint,n)*2654435761 + 11) & 0x7FFFFFFF)) AS h1,
                   ((CHECKSUM(CONVERT(bigint,n)*2654435761 + 23) & 0x7FFFFFFF)) AS h2
            FROM Nums
        )
        INSERT INTO dbo.Users WITH (TABLOCK) (FirstName, LastName, Email, Phone, DateOfBirth)
        SELECT f.nm, l.nm,
               LOWER(CONCAT(f.nm, N'.', l.nm, g.n, N'@example.ae')),               -- n -> уникальность
               CONCAT(N'+97150', RIGHT(N'0000000' + CAST(g.h1 % 10000000 AS nvarchar(7)), 7)),
               DATEADD(DAY, g.h2 % 10585, '1975-01-01')                            -- взрослый 1975..2003
        FROM G g
        JOIN #First f ON f.i = (g.h1 / 7)  % @FirstN
        JOIN #Last  l ON l.i = (g.h1 / 131) % @LastN;

        ---- 4b. FAMILY MEMBERS (0..3, фамилия от родителя) --------------------
        ;WITH NewU AS (SELECT UserId, LastName FROM dbo.Users WHERE UserId > @MaxUser),
        Cnt AS
        (
            SELECT UserId, LastName,
                   ((CHECKSUM(CONVERT(bigint,UserId)*2654435761 + 31) & 0x7FFFFFFF)) % 4 AS fmCount
            FROM NewU
        ),
        Seq AS (SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3),
        Gen AS
        (
            SELECT c.UserId, c.LastName, s.n AS childNo,
                   ((CHECKSUM(CONVERT(bigint,c.UserId)*2654435761 + s.n*101 + 7)  & 0x7FFFFFFF)) AS hf,
                   ((CHECKSUM(CONVERT(bigint,c.UserId)*2654435761 + s.n*211 + 13) & 0x7FFFFFFF)) AS hd
            FROM Cnt c JOIN Seq s ON s.n <= c.fmCount
        )
        INSERT INTO dbo.FamilyMembers WITH (TABLOCK) (UserId, FirstName, LastName, DateOfBirth)
        SELECT g.UserId, ch.nm, g.LastName,
               DATEADD(DAY, g.hd % 4017, '2013-01-01')                             -- ребёнок 2013..2023
        FROM Gen g
        JOIN #Child ch ON ch.i = g.hf % @ChildN;

        ---- 4c. EVENT REGISTRATIONS (1..5 РАЗНЫХ событий) ---------------------
        SET @MaxReg = ISNULL((SELECT MAX(RegistrationId) FROM dbo.EventRegistrations), 0);

        ;WITH NewU AS (SELECT UserId FROM dbo.Users WHERE UserId > @MaxUser),
        Keep AS
        (
            SELECT UserId,
                   (((CHECKSUM(CONVERT(bigint,UserId)*2654435761 + 91) & 0x7FFFFFFF)) % 5) + 1 AS KeepCount
            FROM NewU
        ),
        Ranked AS
        (
            SELECT u.UserId, e.Id AS EventId, e.RegOpen,
                   ROW_NUMBER() OVER (
                       PARTITION BY u.UserId
                       ORDER BY ((CHECKSUM(CONVERT(bigint,u.UserId)*2654435761 + e.Ordinal*40503) & 0x7FFFFFFF))
                   ) AS rnk
            FROM NewU u CROSS JOIN #Events e
        ),
        Sel AS
        (
            SELECT r.UserId, r.EventId, r.RegOpen,
                   ((CHECKSUM(CONVERT(bigint,r.UserId)*2654435761 + CHECKSUM(r.EventId)) & 0x7FFFFFFF)) AS h
            FROM Ranked r
            JOIN Keep k ON k.UserId = r.UserId
            WHERE r.rnk <= k.KeepCount
        )
        INSERT INTO dbo.EventRegistrations WITH (TABLOCK)
            (EventId, UserId, GroupCode,
             EmergencyContactFirstName, EmergencyContactLastName, EmergencyContactPhone,
             RegistrationDate, Status, QRCode)
        SELECT s.EventId, s.UserId,
               CASE WHEN (s.h % 10) < 4 THEN CONCAT(N'GRP-', FORMAT(s.UserId, '0000000')) END,
               ef.nm, el.nm,
               CONCAT(N'+97155', RIGHT(N'0000000' + CAST(s.h % 10000000 AS nvarchar(7)), 7)),
               DATEADD(DAY, s.h % 80, s.RegOpen),                                  -- open + 0..80 (< StartDate)
               s.h % 4,                                                            -- Status 0..3
               NULL
        FROM Sel s
        JOIN #First ef ON ef.i = (s.h / 7)  % @FirstN
        JOIN #Last  el ON el.i = (s.h / 13) % @LastN;

        ---- 4d. QR только для Confirmed/CheckedIn (уникален) -----------------
        UPDATE dbo.EventRegistrations
        SET    QRCode = CONCAT(N'DFC26-', FORMAT(RegistrationId, '0000000000'))
        WHERE  RegistrationId > @MaxReg AND Status IN (1, 2);

        ---- 4e. PARTICIPANTS: родитель (FamilyMemberId = NULL) ----------------
        INSERT INTO dbo.RegistrationParticipants WITH (TABLOCK)
            (RegistrationId, EventId, FamilyMemberId, TshirtSize)
        SELECT r.RegistrationId, r.EventId, NULL,
               CHOOSE(((CHECKSUM(CONVERT(bigint,r.RegistrationId)*2654435761 + 1) & 0x7FFFFFFF) % 6) + 1,
                      N'XS', N'S', N'M', N'L', N'XL', N'XXL')
        FROM dbo.EventRegistrations r
        WHERE r.RegistrationId > @MaxReg;

        ---- 4f. PARTICIPANTS: дети (~50% family members на регистрацию) -------
        INSERT INTO dbo.RegistrationParticipants WITH (TABLOCK)
            (RegistrationId, EventId, FamilyMemberId, TshirtSize)
        SELECT r.RegistrationId, r.EventId, fm.FamilyMemberId,
               CHOOSE(((CHECKSUM(CONVERT(bigint,r.RegistrationId)*2654435761 + fm.FamilyMemberId*7) & 0x7FFFFFFF) % 6) + 1,
                      N'XS', N'S', N'M', N'L', N'XL', N'XXL')
        FROM dbo.EventRegistrations r
        JOIN dbo.FamilyMembers fm ON fm.UserId = r.UserId          -- использует IX_FamilyMembers_UserId
        WHERE r.RegistrationId > @MaxReg
          AND ((CHECKSUM(CONVERT(bigint,r.RegistrationId)*2654435761 + fm.FamilyMemberId) & 0x7FFFFFFF) % 2) = 0;

        COMMIT TRANSACTION;

        SET @Done += @ThisBatch;
        SET @msg = CONCAT(N'Batch ', @Batch, N': users ', @Done, N'/', @UserCount,
                          N'  (', DATEDIFF(SECOND, @t0, SYSDATETIME()), N's)');
        RAISERROR(@msg, 10, 1) WITH NOWAIT;
    END

    ---------------------------------------------------------------------------
    -- 5. REBUILD индексов (на уникальном QR проверится уникальность)
    ---------------------------------------------------------------------------
    IF @ManageIndexes = 1
    BEGIN
        SET @sql = N'';
        SELECT @sql = @sql + N'ALTER INDEX ' + QUOTENAME(IndexName)
                           + N' ON dbo.' + QUOTENAME(TableName) + N' REBUILD;' + CHAR(10)
        FROM #Idx x
        WHERE EXISTS (SELECT 1 FROM sys.indexes i
                      WHERE i.object_id = OBJECT_ID(N'dbo.' + x.TableName)
                        AND i.name = x.IndexName);
        IF @sql <> N'' EXEC sys.sp_executesql @sql;
        RAISERROR(N'Indexes rebuilt.', 10, 1) WITH NOWAIT;
    END

    ---------------------------------------------------------------------------
    -- 6. Восстановить recovery model
    ---------------------------------------------------------------------------
    IF @SwitchRecovery = 1 AND @OrigRecovery NOT IN (N'SIMPLE', N'BULK_LOGGED')
    BEGIN
        SET @sql = N'ALTER DATABASE CURRENT SET RECOVERY ' + @OrigRecovery + N';';
        EXEC sys.sp_executesql @sql;
        RAISERROR(N'Recovery model restored -> %s', 10, 1, @OrigRecovery) WITH NOWAIT;
    END

END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;

    -- не оставляем БД в "грязном" состоянии: чиним индексы и recovery
    BEGIN TRY
        IF @ManageIndexes = 1
        BEGIN
            SET @sql = N'';
            SELECT @sql = @sql + N'ALTER INDEX ' + QUOTENAME(IndexName)
                               + N' ON dbo.' + QUOTENAME(TableName) + N' REBUILD;' + CHAR(10)
            FROM #Idx x
            WHERE EXISTS (SELECT 1 FROM sys.indexes i
                          WHERE i.object_id = OBJECT_ID(N'dbo.' + x.TableName)
                            AND i.name = x.IndexName AND i.is_disabled = 1);
            IF @sql <> N'' EXEC sys.sp_executesql @sql;
        END
        IF @SwitchRecovery = 1 AND @OrigRecovery NOT IN (N'SIMPLE', N'BULK_LOGGED')
        BEGIN
            SET @sql = N'ALTER DATABASE CURRENT SET RECOVERY ' + @OrigRecovery + N';';
            EXEC sys.sp_executesql @sql;
        END
    END TRY
    BEGIN CATCH /* swallow cleanup errors, отдаём исходную ошибку */ END CATCH;

    THROW;
END CATCH;

-------------------------------------------------------------------------------
-- 7. Сводка и санити-чеки
-------------------------------------------------------------------------------
RAISERROR(N'--- DONE in %d sec ---', 10, 1, 0) WITH NOWAIT;

SELECT 'Users' AS [Table], COUNT(*) AS [Rows] FROM dbo.Users
UNION ALL SELECT 'Events',                   COUNT(*) FROM dbo.Events
UNION ALL SELECT 'FamilyMembers',            COUNT(*) FROM dbo.FamilyMembers
UNION ALL SELECT 'EventRegistrations',       COUNT(*) FROM dbo.EventRegistrations
UNION ALL SELECT 'RegistrationParticipants', COUNT(*) FROM dbo.RegistrationParticipants;

-- Распределение регистраций на пользователя (ожидаем 1..5)
SELECT RegsPerUser, Users = COUNT(*)
FROM (
    SELECT UserId, COUNT(*) AS RegsPerUser
    FROM dbo.EventRegistrations GROUP BY UserId
) q
GROUP BY RegsPerUser ORDER BY RegsPerUser;

-- Санити: ни у кого не больше 5 и нет дублей событий
SELECT MaxRegsPerUser = MAX(c), DupEventPerUser =
       (SELECT COUNT(*) FROM (SELECT UserId, EventId FROM dbo.EventRegistrations
                              GROUP BY UserId, EventId HAVING COUNT(*) > 1) d)
FROM (SELECT UserId, COUNT(*) c FROM dbo.EventRegistrations GROUP BY UserId) x;

-- Участники: должно быть >= числа регистраций (минимум по 1 родителю)
SELECT Registrations = (SELECT COUNT(*) FROM dbo.EventRegistrations),
       Participants   = (SELECT COUNT(*) FROM dbo.RegistrationParticipants),
       ParentRows     = (SELECT COUNT(*) FROM dbo.RegistrationParticipants WHERE FamilyMemberId IS NULL),
       ChildRows      = (SELECT COUNT(*) FROM dbo.RegistrationParticipants WHERE FamilyMemberId IS NOT NULL);
