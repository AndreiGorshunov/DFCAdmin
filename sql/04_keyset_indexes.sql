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
     3) делает колонку NOT NULL (после бэкфилла — иначе EF упадёт на NULL);
     4) строит keyset-индекс (RegistrantLastName, RegistrationId);
     5) (опционально) full-text индекс для поиска по именам.

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

/* 3. NOT NULL — строго после бэкфилла. ------------------------------------- */
ALTER TABLE dbo.EventRegistrations ALTER COLUMN RegistrantLastName nvarchar(100) NOT NULL;
GO

/* 4. Keyset-индекс: один seek на листинг (порядок = ORDER BY кода). --------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_ER_Keyset'
                 AND object_id = OBJECT_ID(N'dbo.EventRegistrations'))
    CREATE INDEX IX_ER_Keyset
        ON dbo.EventRegistrations (RegistrantLastName, RegistrationId)
        INCLUDE (EventId, UserId, GroupCode, Status);
GO

/* =============================================================================
   ОПЦИОНАЛЬНО — full-text поиск по именам (быстрее, чем LIKE %term% на больших
   объёмах). Email в FTS не кладём: токенизация по '@'/'.' ведёт себя странно —
   для email лучше сарджабельный префикс  Email LIKE @q + '%'  по UX_Users_Email.

   ВНИМАНИЕ: чтобы это реально использовалось, поиск в коде нужно перевести на
   CONTAINS через EF.FromSql (EF.Functions.Contains транслируется в LIKE, не в
   полнотекстовый CONTAINS). Сейчас поиск в RegistrantQueryService — токенизированный
   LIKE по Users; FTS-индекс ниже создаётся «про запас».
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
