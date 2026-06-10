/* =============================================================================
   DFC 2026 — Вариант B: денормализация фамилии регистранта + индексы листинга
   Target DB: [dfc.EventRegistration]
   -----------------------------------------------------------------------------
   Листинг регистраций сортируется по фамилии регистранта, затем RegistrationId.
   Чтобы keyset ложился ОДНИМ seek'ом на таблицу регистраций (без джойна к Users
   на сортировке), держим фамилию денормализованно в EventRegistrations.RegistrantLastName.

   Колонка объявлена в 01_schema.sql и заполняется в 03_bulk_load. Этот скрипт:
     1) добивает колонку, если её нет (для БД, созданных старой схемой);
     2) бэкфилл NULL-ов из Users;
     3) снимает зависимый keyset-индекс, если он есть (иначе ALTER COLUMN падает);
     4) делает колонку NOT NULL (после бэкфилла — иначе EF упадёт на NULL);
     5) (пере)создаёт keyset-индекс (RegistrantLastName, RegistrationId);
     6) индекс IX_Users_Phone для поиска по телефону;
     7) (опционально) full-text индексы для поиска по именам (Users, FamilyMembers).

   Порядок запуска: 01 (схема) -> 03 (bulk) -> 04 (этот файл).

   Поддержание актуальности при правке имени делается в приложении
   (AdminWriteService.UpdateUser/UpdateRegistrant — set-based ExecuteUpdate
   проставляет RegistrantLastName во всех регистрациях персоны в одной транзакции).
   ============================================================================= */

USE [dfc.EventRegistration];
GO

/* 1. Колонка — идемпотентно (на случай БД без неё). ------------------------- */
IF COL_LENGTH(N'dbo.EventRegistrations', N'RegistrantLastName') IS NULL
    ALTER TABLE dbo.EventRegistrations ADD RegistrantLastName nvarchar(100) NULL;
GO

/* 2. Бэкфилл из Users (строки, добавленные до появления колонки). ----------- */
UPDATE er
SET    er.RegistrantLastName = u.LastName
FROM   dbo.EventRegistrations er
JOIN   dbo.Users u ON u.UserId = er.UserId
WHERE  er.RegistrantLastName IS NULL;
GO

/* 3. Снимаем keyset-индекс, если он есть: он зависит от RegistrantLastName,
      и без этого ALTER COLUMN падает (Msg 5074/4922). На свежей БД — no-op. ----- */
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = N'IX_ER_Keyset'
             AND object_id = OBJECT_ID(N'dbo.EventRegistrations'))
    DROP INDEX IX_ER_Keyset ON dbo.EventRegistrations;
GO

/* 4. NOT NULL — строго после бэкфилла и снятия зависимого индекса. --------- */
ALTER TABLE dbo.EventRegistrations ALTER COLUMN RegistrantLastName nvarchar(100) NOT NULL;
GO

/* 5. Keyset-индекс (пере)создаём: один seek на листинг (порядок = ORDER BY кода). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_ER_Keyset'
                 AND object_id = OBJECT_ID(N'dbo.EventRegistrations'))
    CREATE INDEX IX_ER_Keyset
        ON dbo.EventRegistrations (RegistrantLastName, RegistrationId)
        INCLUDE (EventId, UserId, GroupCode, Status);
GO

/* 6. Индекс для поиска по телефону (Phone LIKE '%x%' -> узкий index scan вместо
      кластерного). Идемпотентно — для БД, созданных старой схемой без него. ------ */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_Users_Phone'
                 AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE INDEX IX_Users_Phone ON dbo.Users (Phone) WHERE Phone IS NOT NULL;
GO

/* =============================================================================
   ОПЦИОНАЛЬНО — full-text поиск по именам (быстрее, чем LIKE %term% на больших
   объёмах). Email в FTS не кладём: токенизация по '@'/'.' ведёт себя странно —
   поэтому email/телефон ищутся обычным LIKE по Users.

   Поиск в *QueryService уже использует full-text: EF.Functions.Contains
   (и EF.Functions.FreeText) транслируются в SQL-предикат CONTAINS/FREETEXT, а НЕ
   в LIKE (LIKE — это string.Contains). FromSql не нужен. Без full-text индексов
   ниже EF.Functions.Contains кинет SQL-ошибку — поэтому FTS-поиск держим на
   отдельной ветке (fallback на токенизированный LIKE — в основной ветке).

   Индексы: Users(FirstName, LastName) — имена регистрантов/персон (вкладки
   Registrants/Users), FamilyMembers(FirstName, LastName) — имена детей (вкладка Children).
   ============================================================================= */
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'dfc_ft')
    CREATE FULLTEXT CATALOG dfc_ft AS DEFAULT;
GO

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes
               WHERE object_id = OBJECT_ID(N'dbo.Users'))
    CREATE FULLTEXT INDEX ON dbo.Users (FirstName, LastName)
        KEY INDEX PK_Users               -- уникальный single-column индекс на UserId
        WITH CHANGE_TRACKING = AUTO;
GO

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes
               WHERE object_id = OBJECT_ID(N'dbo.FamilyMembers'))
    CREATE FULLTEXT INDEX ON dbo.FamilyMembers (FirstName, LastName)
        KEY INDEX PK_FamilyMembers       -- уникальный single-column индекс на FamilyMemberId
        WITH CHANGE_TRACKING = AUTO;
GO
