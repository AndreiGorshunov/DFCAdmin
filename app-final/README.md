# DFC 2026 — Admin (Registrants & T-shirt report)

Минимальное ASP.NET Core 8 (Razor Pages) приложение поверх существующей БД
`[dfc.EventRegistration]`: листинг регистраций с поиском/фильтром/сортировкой/пейджингом,
экспорт в Excel, и отчёт по футболкам.

## Быстрый старт

```bash
# 1) База
#    выполни в SSMS по порядку: sql/01_schema.sql, затем 02_seed_small.sql
#    (или 03_bulk_load_1m.sql для нагрузочного объёма), затем 04_keyset_indexes.sql

# 2) Приложение
cd app-final/src/DfcEventRegistration.Web
#    проверь ConnectionStrings:Default в appsettings.json
dotnet restore
dotnet run
```

Открой адрес из консоли (например `https://localhost:7095`). Корень редиректит на
`/Registrants`.

> .NET: проект на `net8.0` (LTS). Если стоит .NET 10 — поменяй `TargetFramework`
> в `.csproj` на `net10.0` и версии пакетов на `10.x`.

## Что реализовано

**Registrants** (`/Registrants`)
- Поиск по email / имени / фамилии / телефону (`LIKE %term%`).
- Фильтр по событию и по статусу.
- Сортировка по email / имени / фамилии / группе / событию / checked-in
  (клик по заголовку; стабильный tiebreaker по `RegistrationId`).
- Серверный пейджинг (`Skip/Take`), размер страницы 25–200.
- Экспорт в Excel (ClosedXML) с учётом текущих фильтров; кап на строки —
  `Export:MaxRows` в конфиге (по умолчанию 100k).
- «Total kids below/above 13» — считается из `RegistrationParticipants` →
  `FamilyMembers.DateOfBirth`, возраст на дату начала события.

**T-shirt report** (`/Reports/Tshirts`)
- По размерам: Requested (участники с размером) vs Collected (регистрация checked-in)
  vs Stock (из конфига) vs остатки.
- Фильтр по событию, экспорт в Excel.

## Маппинг спеки на схему (и честные разрывы)

| Поле спеки | Источник | Примечание |
|---|---|---|
| Email / First / Last / Mobile | `Users` | прямое |
| Group code | `EventRegistrations.GroupCode` | прямое |
| Event | `Events.Name` | фильтр по событиям из БД |
| Collected bibs / Checked in at venue | `EventRegistrations.Status == CheckedIn` | **один сигнал на оба** — в схеме нет `EventType`, разделяющего Run/Ride/Speed Laps (bibs) и SUP/Yoga (venue check-in) |
| Total kids below/above 13 | `RegistrationParticipants` + `FamilyMembers.DateOfBirth` | возраст на `Event.StartDate` |
| **Slot/Route/Session** | — | **нет в схеме**, в Excel выгружается пустой колонкой |
| T-shirt **stock** | `appsettings: TshirtStock` | **нет в БД** — placeholder из конфига |

Чтобы закрыть разрывы по-настоящему, минимально нужно: добавить `Events.EventType`
(tinyint/lookup), `EventRegistrations.SlotDetails` (nvarchar), и отдельную таблицу
остатков футболок (`TshirtStock`), либо признак фактической выдачи на участнике.

## Замечания по производительности (~1M+ регистраций)

- **Поиск** через `LIKE %term%` имеет ведущий wildcard и не sargable. На проде под
  объём — full-text index по `Users(Email, FirstName, LastName)` или префиксный поиск
  (`term%`), который ложится на обычный индекс.
- **Пейджинг** сейчас offset (`Skip/Take`) — для глубоких страниц деградирует.
  Для «бесконечной» прокрутки лучше keyset: `WHERE (LastName, RegistrationId) > (@ln, @id)`.
- **Подсчёт детей** делается коррелированными подзапросами на странице (25–200 строк) —
  это ок. Если EF не переведёт `DateDiffYear` на твоём провайдере, замени проекцию
  `KidsBelow13/Above13` на отдельный сгруппированный запрос по `RegistrationId`
  текущей страницы.
- Для фильтров пригодятся индексы `EventRegistrations(EventId, Status)` и покрытие под
  сортировку; их состав зависит от реальных паттернов запросов.

## Известные ограничения (для прода)

- **Конкурентность** — last-write-wins (нет `rowversion`).
- **Auth** — экраны открыты; нужен `[Authorize]` + аутентификация (UAE Pass/OAuth).
- **Разрывы спека↔схема** — нет `EventType` (Run/Ride/SUP/Yoga разделение bibs vs
  venue check-in), `Slot/Route/Session`, t-shirt `stock` (берётся из конфига).
  Подробности — в `app-final/README.md` и `CONVERSATION.md`.
  
## Структура

```
Program.cs                      // hosting, DI, routing
appsettings.json                // connection string, TshirtStock, Export:MaxRows
Data/
  Entities.cs                   // User/Event/FamilyMember/EventRegistration/Participant + enum
  AppDbContext.cs               // Fluent-маппинг на dbo.*
Models/ViewModels.cs            // RegistrantRow / RegistrantFilter / TshirtReportRow
Services/
  RegistrantQueryService.cs     // фильтр + проекция + сортировка + count
  TshirtReportService.cs        // группировка по размерам + stock из конфига
  ExcelExportService.cs         // ClosedXML
Pages/
  Registrants/Index             // листинг + экспорт
  Reports/Tshirts               // отчёт + экспорт
```
