using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Services;

public class RegistrantQueryService
{
    private readonly AppDbContext _db;
    public RegistrantQueryService(AppDbContext db) => _db = db;

    /// <summary>Базовый отфильтрованный набор регистраций (без проекции).</summary>
    private IQueryable<EventRegistration> Base(RegistrantFilter f)
    {
        var q = _db.EventRegistrations.AsNoTracking();

        if (f.EventId is Guid ev)
            q = q.Where(r => r.EventId == ev);

        if (f.Status is RegistrationStatus st)
            q = q.Where(r => r.Status == st);

        // Тип участника (exclusive-семантика, грейн остаётся = регистрация):
        // Adults  -> регистрации БЕЗ детей (нет участника с FamilyMemberId != null).
        // Children-> регистрации С детьми (есть участник с FamilyMemberId != null).
        // Для просмотра самих ДЕТЕЙ (грейн = участник) есть отдельная вкладка /Children.
        if (f.ParticipantType == ParticipantKind.Adults)
            q = q.Where(r => !r.Participants.Any(p => p.FamilyMemberId != null));
        else if (f.ParticipantType == ParticipantKind.Children)
            q = q.Where(r => r.Participants.Any(p => p.FamilyMemberId != null));

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = f.Q.Trim();
            // LIKE %term% — для админ-инструмента ок; на больших объёмах
            // ведущий wildcard не sargable (см. keyset_indexes.sql про full-text).
            q = q.Where(r =>
                r.User.Email.Contains(term) ||
                r.User.FirstName.Contains(term) ||
                r.User.LastName.Contains(term) ||
                //r.RegistrantLastName.Contains(term) // если включаешь денормализацию
                (r.User.Phone != null && r.User.Phone.Contains(term)));
        }

        return q;
    }

    public Task<int> CountAsync(RegistrantFilter f, CancellationToken ct = default)
        => Base(f).CountAsync(ct);

    public IQueryable<RegistrantRow> Query(RegistrantFilter f)
    {
        return Base(f).Select(r => new RegistrantRow
        {
            RegistrationId = r.RegistrationId,
            Email = r.User.Email,
            FirstName = r.User.FirstName,
            LastName = r.User.LastName,
            Mobile = r.User.Phone,
            GroupCode = r.GroupCode,
            EventName = r.Event.Name,
            Status = r.Status,
            CheckedIn = r.Status == RegistrationStatus.CheckedIn,

            KidsBelow13 = r.Participants.Count(p =>
                p.FamilyMemberId != null &&
                EF.Functions.DateDiffYear(p.FamilyMember!.DateOfBirth, r.Event.StartDate) < 13),

            KidsAbove13 = r.Participants.Count(p =>
                p.FamilyMemberId != null &&
                EF.Functions.DateDiffYear(p.FamilyMember!.DateOfBirth, r.Event.StartDate) >= 13),
        });
    }

    /// <summary>Сортировка по белому списку колонок + стабильный tiebreaker по RegistrationId.</summary>
    public IQueryable<RegistrantRow> ApplySort(IQueryable<RegistrantRow> q, string? sort, string? dir)
    {
        bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<RegistrantRow> ordered = (sort?.ToLowerInvariant()) switch
        {
            "email"     => desc ? q.OrderByDescending(r => r.Email)     : q.OrderBy(r => r.Email),
            "firstname" => desc ? q.OrderByDescending(r => r.FirstName) : q.OrderBy(r => r.FirstName),
            "event"     => desc ? q.OrderByDescending(r => r.EventName) : q.OrderBy(r => r.EventName),
            "groupcode" => desc ? q.OrderByDescending(r => r.GroupCode) : q.OrderBy(r => r.GroupCode),
            "checkedin" => desc ? q.OrderByDescending(r => r.CheckedIn) : q.OrderBy(r => r.CheckedIn),
            _           => desc ? q.OrderByDescending(r => r.LastName)  : q.OrderBy(r => r.LastName),
        };

        return ordered.ThenBy(r => r.RegistrationId);
    }

    /// <summary>
    /// Keyset-сидинг. Зеркалит дефолтный ORDER BY (LastName ASC, RegistrationId ASC):
    /// возвращает строки СТРОГО ПОСЛЕ курсора (afterLastName, afterId).
    /// string.Compare(...) > 0 EF переводит в SQL '>' по коллации колонки —
    /// то есть тот же порядок, что и в ORDER BY. Для первой страницы курсора нет.
    /// </summary>
    public IQueryable<RegistrantRow> SeekAfter(IQueryable<RegistrantRow> ordered, string? afterLastName, long? afterId)
    {
        if (afterLastName is null || afterId is null)
            return ordered;

        var ln = afterLastName;
        var id = afterId.Value;

        return ordered.Where(r =>
            string.Compare(r.LastName, ln) > 0 ||
            (r.LastName == ln && r.RegistrationId > id));
    }
}
