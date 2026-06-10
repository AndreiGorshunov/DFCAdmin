/* =============================================================================
   DFC 2026 — индексы под листинг регистраций (keyset + фильтры) и опционально FTS
   Target DB: [dfc.EventRegistration]
   -----------------------------------------------------------------------------
   Контекст: листинг сортируется по Users.LastName, потом EventRegistrations.RegistrationId.
   Это порядок ЧЕРЕЗ ДВЕ ТАБЛИЦЫ — отсюда выбор вариантов ниже.
   ============================================================================= */

USE [dfc.EventRegistration];
GO

/* =============================================================================
   ВАРИАНТ A — без изменения схемы (запускается как есть)
   -----------------------------------------------------------------------------
   Покрывает фильтры по событию/статусу и ускоряет джойн+порядок по фамилии.
   Keyset корректен, но seek через JOIN оптимизатор не всегда свернёт в один
   проход (LastName в Users, RegistrationId в EventRegistrations).
   ============================================================================= */
/*
-- Фильтры EventId/Status + покрытие частых колонок (без обращения к куче).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_ER_Event_Status'
                 AND object_id = OBJECT_ID(N'dbo.EventRegistrations'))
CREATE INDEX IX_ER_Event_Status
    ON dbo.EventRegistrations (EventId, Status)
    INCLUDE (UserId, GroupCode, RegistrationDate, QRCode);
GO

-- Порядок/поиск по фамилии: ORDER BY LastName + обратный джойн к Users.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_Users_LastName'
                 AND object_id = OBJECT_ID(N'dbo.Users'))
CREATE INDEX IX_Users_LastName
    ON dbo.Users (LastName, UserId)
    INCLUDE (FirstName, Email, Phone);
GO
*/

/* =============================================================================
   ВАРИАНТ B — рекомендуется для read-heavy листинга (денормализация фамилии)
   -----------------------------------------------------------------------------
   Кладём LastName прямо в EventRegistrations -> keyset ложится ОДНИМ seek'ом
   на таблицу листинга: индекс (RegistrantLastName, RegistrationId).
   Цена: поле надо поддерживать в актуальном состоянии (см. ниже).

   ВНИМАНИЕ: чтобы EF реально это использовал, нужно переключить сортировку/seek
   на денормализованную колонку (см. блок "Изменения в коде" в конце файла).
   Поэтому Вариант B по умолчанию ЗАКОММЕНТИРОВАН — включай осознанно.
   ============================================================================= */


IF COL_LENGTH(N'dbo.EventRegistrations', N'RegistrantLastName') IS NULL
    ALTER TABLE dbo.EventRegistrations ADD RegistrantLastName nvarchar(100) NULL;
GO

-- Бэкфилл существующих строк
UPDATE er
SET    er.RegistrantLastName = u.LastName
FROM   dbo.EventRegistrations er
JOIN   dbo.Users u ON u.UserId = er.UserId
WHERE  er.RegistrantLastName IS NULL;
GO

-- Индекс под keyset одним seek'ом
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_ER_Keyset'
                 AND object_id = OBJECT_ID(N'dbo.EventRegistrations'))
CREATE INDEX IX_ER_Keyset
    ON dbo.EventRegistrations (RegistrantLastName, RegistrationId)
    INCLUDE (EventId, UserId, GroupCode, Status);
GO

-- Поддержание актуальности:
--   * проще всего — проставлять RegistrantLastName из Users на запись регистрации
--     (в приложении, в той же транзакции);
--   * если фамилии редактируются в Users и это должно отражаться в листинге —
--     триггер AFTER UPDATE на Users(LastName), синхронизирующий EventRegistrations.


/* =============================================================================
   ОПЦИОНАЛЬНО — full-text поиск по ИМЕНАМ (ускоряет поиск вместо LIKE %term%)
   -----------------------------------------------------------------------------
   Email в FTS не кладём: токенизация по '@' и '.' ведёт себя странно.
   Для email лучше сарджабельный префикс  Email LIKE @q + '%'  по IX_Users_Email.
   Использование CONTAINS требует отдельного патча поиска (EF.FromSql) — скажи,
   добавлю; EF.Functions.Contains это всё ещё LIKE, не CONTAINS.
   ============================================================================= */


IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'dfc_ft')
    CREATE FULLTEXT CATALOG dfc_ft AS DEFAULT;
GO

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes
               WHERE object_id = OBJECT_ID(N'dbo.Users'))
    CREATE FULLTEXT INDEX ON dbo.Users (FirstName, LastName)
        KEY INDEX PK_Users          -- уникальный single-column индекс на UserId
        WITH CHANGE_TRACKING = AUTO;
GO


/* =============================================================================
   ИЗМЕНЕНИЯ В КОДЕ ДЛЯ ВАРИАНТА B (если включаешь денормализацию)
   -----------------------------------------------------------------------------
   1) В сущность EventRegistration добавить:  public string RegistrantLastName { get; set; }
   2) В Query(...) проекцию LastName брать из r.RegistrantLastName (не r.User.LastName).
   3) В ApplySort(...) дефолтную ветку и SeekAfter(...) оставить как есть —
      они работают по RegistrantRow.LastName, который теперь маппится на
      денормализованную колонку, и seek уходит одним проходом по IX_ER_Keyset.
   Поиск (LIKE/FTS) по-прежнему по Users — это ок, фильтр и порядок развязаны.
   ============================================================================= */
