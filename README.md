# DFC 2026 — Admin

ASP.NET Core 8 (Razor Pages) поверх существующей БД `[dfc.EventRegistration]`:
управление регистрациями (поиск/фильтр/сортировка/пейджинг, правка, удаление),
пользователями, детьми, событиями и членами семьи; отчёт по футболкам и выгрузки в
Excel/CSV. Доступ под аутентификацией с ролями **Admin** / **Partner**, журнал аудита
и управление доступом из UI.

## Быстрый старт

```bash
# 1) База (SSMS, по порядку)
#    Свежая БД:    sql/01_schema.sql  ->  03_bulk_load_1m.sql (или 02_seed_small.sql)  ->  04_keyset_indexes.sql
#    Аутентификация (таблицы доступа + аудит + сид): sql/05_auth.sql
#    На уже существующей БД достаточно прогнать только 05_auth.sql.

# 2) Приложение
cd app-final/src/DfcEventRegistration.Web
#    проверь ConnectionStrings:Default в appsettings.json
dotnet restore
dotnet run
```

Открой адрес из консоли (например `https://localhost:7095`). Без входа — редирект на
`/Account/Login`. В Development это dev-заглушка: выбираешь засиженного пользователя
(`admin@dfc.local` — Admin, `partner@dfc.local` — Partner), роль берётся из таблицы
`AdminUsers`. Корень редиректит на `/Registrants`.

> .NET: проект на `net8.0` (LTS). Если стоит .NET 10 — поменяй `TargetFramework`
> в `.csproj` на `net10.0` и версии пакетов на `10.x`.

## Что реализовано

**Registrants** (`/Registrants`)
- Поиск по email / имени / фамилии / телефону (умная маршрутизация — см. «Поиск»).
- Фильтр по событию и статусу; сортировка по клику (стабильный tiebreaker по `RegistrationId`).
- Серверный пейджинг; экспорт в Excel/CSV с учётом фильтров (кап `Export:MaxRows`).
- «Total kids below/above 13» из `RegistrationParticipants` → `FamilyMembers.DateOfBirth`
  (возраст на дату начала события).
- Правка персоны + регистрации, управление участниками и ростером семьи, danger-zone
  (удаление регистрации / персоны каскадом) — `/Registrants/Edit` (Admin).
- Read-only просмотр — `/Registrants/Details` (доступен и партнёрам).

**Users / Children / Events / FamilyMembers**
- Users (`/Users`): список + создание/правка (Admin).
- Children (`/Children`): поиск участников-детей (по имени ребёнка и родителя).
- Events (`/Events`): список + создание/правка/каскадное удаление (Admin).
- FamilyMembers (`/FamilyMembers/Edit`): правка члена семьи (Admin).

**T-shirt report** (`/Reports/Tshirts`)
- По размерам: Requested vs Collected (checked-in) vs Stock (конфиг) vs остатки; фильтр по
  событию, экспорт.

**Доступ и аудит**
- Аутентификация (cookie), роли Admin/Partner — см. раздел ниже.
- Управление доступом (`/Admins`, Admin) и журнал аудита (`/Audit`, Admin).

## Аутентификация и доступ

### Как устроено сейчас
- **Сессия — cookie** (`AddCookie`, `Program.cs`). Это локальный слой приложения,
  не зависящий от провайдера. Реальный IdP подключится отдельной внешней схемой, cookie
  останется сессией (см. «Заметки на будущее»).
- **Две роли:** `Admin` (полный доступ) и `Partner` (только просмотр/поиск + отчёты,
  без правок). Константы — `Auth/AuthConstants.cs`.
- **Политики по действиям:** `CanView` (Admin + Partner) и `CanManage` (Admin). Страницы
  ссылаются на политику, а маппинг «политика → роли» живёт в одном месте.
- **Гейтинг — в одном месте** (Razor-конвенции в `Program.cs`), а не атрибутами по классам:
  `AuthorizeFolder("/", CanView)` секьюрит всё по умолчанию; `CanManage` точечно вешается
  на страницы/папки правок (`/Registrants/Edit`, `/Users/Create|Edit`, `/Events`,
  `/FamilyMembers/Edit`, `/Admins`, `/Audit`). `FallbackPolicy` требует аутентификации для
  любого не покрытого эндпоинта. Вход/отказ/выход — `AllowAnonymous`.
- **Важно:** все мутации физически расположены на `CanManage`-страницах, поэтому партнёр
  не вызовет их даже сырым POST — это серверный гейт, а не только скрытие в UI.
- **Источник ролей — таблица `dbo.AdminUsers`** (не конфиг). Роль резолвится по email
  **при входе** через `IRoleResolver` (`Auth/RoleResolver.cs`), учитывая `IsActive` и
  `ExpiresAtUtc`, и кладётся в claim cookie — без похода в БД на каждый запрос.
- **Dev-вход** (`Pages/Account/Login`) работает только в Development: роль не выбирается
  руками, а резолвится из `AdminUsers` по выбранному email (прогоняется реальный путь).
- **Аудит:** `AuditService` берёт актёра из claims и стейджит запись в тот же `DbContext`,
  поэтому «кто/что сделал» сохраняется в одной транзакции с изменением. Вписан во все
  мутации `AdminWriteService`. Просмотр — `/Audit`.
- **Управление доступом:** `/Admins` — выдача/обновление (`GrantAccessAsync`), отзыв/возврат
  (`SetAdminActiveAsync`), удаление. Это и есть «сотрудники DFC выдают доступ партнёрам».

### Карта доступа

| Область | Admin | Partner |
|---|---|---|
| Registrants/Users/Children (поиск), T-shirt report, экспорт, `/Registrants/Details` | ✅ | ✅ (read-only) |
| Registrants/Edit, Users/Create+Edit, Events/*, FamilyMembers/Edit | ✅ | ⛔ |
| /Admins, /Audit | ✅ | ⛔ |

### Заметки на будущее (что и где менять)

1. **Подключить реальный IdP (OIDC / Entra ID / UAE Pass).** Весь черновик —
   `Auth/OidcAuthentication.cs` (закомментирован, чтобы сборка не требовала пакета).
   Шаги:
   - `dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect`;
   - заполнить `Auth:Oidc` в конфиге и выставить `Enabled = true`;
   - в `Program.cs` заменить блок `AddAuthentication(...).AddCookie(...)` на
     `services.AddDfcOidc(config)` (раскомментировать метод);
   - в `Pages/Account/Login.cshtml.cs` в проде делать `Challenge("oidc")` вместо dev-формы.
   Роль подтянет `OnTokenValidated` через ту же `IRoleResolver` — провайдеро-независимая
   часть уже готова и переиспользуется.
2. **`ClientSecret` — НЕ в `appsettings.json`.** В проде только user-secrets / переменные
   окружения. Плейсхолдеры в `Auth:Oidc` оставлены пустыми.
3. **Мгновенный отзыв доступа (kill-switch).** Сейчас роль резолвится при входе и живёт в
   cookie (8ч sliding), поэтому деактивация/истечение срока/смена роли вступают в силу при
   следующем входе. Если нужно отзывать сразу (особенно для партнёров) — добавить
   `CookieAuthenticationEvents.OnValidatePrincipal` в `AddCookie` (`Program.cs`): на каждый
   запрос перепроверять `AdminUsers` через `IRoleResolver` и при отсутствии активной записи
   звать `RejectPrincipal()` + `SignOutAsync()`. Таблица крошечная; можно закэшировать на
   несколько секунд, чтобы не бить БД на каждый хит.
4. **Сид-пользователи.** `05_auth.sql` сидит `admin@dfc.local` / `partner@dfc.local` —
   заменить на реальные адреса сотрудников (или раздать доступ через `/Admins` после первого
   входа админом). Сравнение email регистронезависимо (CI-коллация SQL Server).
5. **Cookie/secure.** В Development `SecurePolicy = SameAsRequest` (иначе secure-cookie не
   ходит по http и вход зацикливается); в проде — `Always` + HSTS уже включён. `SameSite=Lax`
   совместим с redirect-флоу OIDC.
6. **Выход при OIDC.** В `OidcAuthentication.cs` задан `SignedOutCallbackPath` — при включении
   IdP сделать выход федеративным (SignOut из cookie + из IdP), а форму logout в `_Layout`
   оставить (она уже POST + antiforgery).
7. **Рост `AuditLog`.** Таблица растёт безгранично; для прода добавить ретеншн/архивацию
   (партиционирование по `WhenUtc` или ночной перенос в холодное хранилище). Индекс —
   `IX_AuditLog_WhenUtc (WhenUtc DESC)`.
8. **Больше ролей/гранулярности.** Если появятся новые роли — расширять только маппинг
   «политика → роли» в `Program.cs` и при необходимости дробить `CanManage` на более узкие
   политики; разметку страниц трогать не нужно.

## Поиск и производительность (~1M+ регистраций)

- **Маршрутизация по форме ввода** (`Services/UserSearchService.cs`):
  - `@` в строке → **email-префикс** (`StartsWith`) — seek по `UX_Users_Email`;
  - только цифры → **телефон «оканчивается на N цифр»**: ищем по `PhoneDigitsRev`
    (PERSISTED-колонка с цифрами в обратном порядке) как префикс — seek по
    `IX_Users_PhoneDigitsRev`, а не подстрочный `LIKE '%...%'`;
  - иначе → **имя/фамилия**: full-text `CONTAINS` (токены AND) при `Search:UseFullText=true`,
    иначе токенизированный `LIKE`. Поиск детей (`/Children`) дополнительно ищет по именам в
    `FamilyMembers`.
  - Трейд-оффы: цифры из СЕРЕДИНЫ номера не покрываются reversed-seek; FTS — префикс/словоформы,
    без fuzzy. Если на сервере FTS недоступен — переключить `Search:UseFullText=false`.
- **Сортировка/keyset:** листинг сикается по денормализованной `RegistrantLastName`; под это
  есть `IX_ER_Keyset (RegistrantLastName, RegistrationId)` (см. `04_keyset_indexes.sql`).
  `RegistrantLastName` синхронизируется во всех регистрациях персоны при правке (Вариант B).
- **Подсчёт детей** — коррелированные подзапросы на странице (25–200 строк), это ок.
- **Денормализация:** правка имени/фамилии/email персоны отражается на всех её регистрациях
  (внутри транзакции, set-based `ExecuteUpdate`).

## Маппинг спеки на схему (честные разрывы)

| Поле спеки | Источник | Примечание |
|---|---|---|
| Email / First / Last / Mobile | `Users` | прямое |
| Group code | `EventRegistrations.GroupCode` | прямое |
| Event | `Events.Name` | фильтр по событиям из БД |
| Collected bibs / Checked in at venue | `EventRegistrations.Status == CheckedIn` | **один сигнал на оба** — в схеме нет `EventType` |
| Total kids below/above 13 | `RegistrationParticipants` + `FamilyMembers.DateOfBirth` | возраст на `Event.StartDate` |
| **Slot/Route/Session** | — | **нет в схеме** |
| T-shirt **stock** | `appsettings:TshirtStock` | **нет в БД** — placeholder из конфига |

Чтобы закрыть разрывы: `Events.EventType` (lookup), `EventRegistrations.SlotDetails`,
отдельная таблица остатков футболок (либо признак фактической выдачи на участнике).

## Известные ограничения (для прода)

- **Конкурентность** — last-write-wins (нет `rowversion`).
- **Каскады** — FK с `NO ACTION` (multiple cascade paths), удаление каскадом сделано вручную
  в транзакции; альтернатива на проде — soft-delete (PDPL/GDPR-дружественнее).
- **Отзыв доступа** — отложенный (вступает в силу при следующем входе); см. «Заметки» п.3.
- **Разрывы спека↔схема** — `EventType`, `Slot/Route/Session`, t-shirt `stock`.

## Структура

```
Program.cs                      // hosting, DI, cookie-auth, политики, RBAC-конвенции
appsettings.json                // ConnectionStrings, TshirtStock, Export, Search, Auth:Oidc
Auth/
  AuthConstants.cs              // Roles (Admin/Partner) + Policies (CanView/CanManage)
  RoleResolver.cs               // IRoleResolver: email -> роль из AdminUsers (при входе)
  OidcAuthentication.cs         // СКАФФОЛД реального IdP (закомментирован)
Data/
  Entities.cs                   // ...Registration/Participant + AdminUser + AuditEntry + enum
  AppDbContext.cs               // Fluent-маппинг на dbo.* (вкл. AdminUsers, AuditLog)
Models/ViewModels.cs
Services/
  UserSearchService.cs          // маршрутизация поиска (email/phone/name, FTS|LIKE)
  RegistrantQueryService.cs / UserQueryService.cs / ChildQueryService.cs
  TshirtReportService.cs
  ExcelExportService.cs / StreamingExportService.cs
  AdminWriteService.cs          // ВСЕ мутации + аудит + управление доступом
  AuditService.cs               // запись «кто/что» в текущий DbContext
Pages/
  Account/  Login · Logout · Denied
  Registrants/  Index · Details (read-only) · Edit
  Users/  Index · Create · Edit
  Children/Index · Events/  Index · Edit · FamilyMembers/Edit
  Reports/Tshirts
  Admins/Index (доступ) · Audit/Index (журнал)
  Shared/_Layout
```

## SQL-скрипты

```
sql/01_schema.sql          // таблицы (вкл. AdminUsers, AuditLog), идемпотентно
sql/02_seed_small.sql      // небольшой сид данных
sql/03_bulk_load_1m.sql    // нагрузочный объём (~1M регистраций)
sql/04_keyset_indexes.sql  // индексы под поиск/сортировку (PhoneDigitsRev, IX_ER_Keyset, FTS)
sql/05_auth.sql            // AdminUsers + AuditLog + сид доступа (для существующей БД — только он)
```
