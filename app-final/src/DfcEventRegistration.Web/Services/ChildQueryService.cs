using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Services;

/// <summary>
/// Грейн = участник-ребёнок (RegistrationParticipants с FamilyMemberId != null).
/// В отличие от листинга регистрантов, здесь строка = конкретный ребёнок на конкретном
/// событии (одна регистрация может дать несколько строк — по числу детей).
/// </summary>
public class ChildQueryService
{
    private readonly AppDbContext _db;
    public ChildQueryService(AppDbContext db) => _db = db;

    private IQueryable<RegistrationParticipant> Base(ChildFilter f)
    {
        var q = _db.RegistrationParticipants.AsNoTracking()
            .Where(p => p.FamilyMemberId != null);

        if (f.EventId is Guid ev)
            q = q.Where(p => p.EventId == ev);            // денормализованный EventId на участнике

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var t = f.Q.Trim();
            q = q.Where(p =>
                p.FamilyMember!.FirstName.Contains(t) ||
                p.FamilyMember!.LastName.Contains(t) ||
                p.Registration.User.FirstName.Contains(t) ||
                p.Registration.User.LastName.Contains(t) ||
                p.Registration.User.Email.Contains(t));
        }

        if (f.Age == AgeBand.Under13)
            q = q.Where(p => EF.Functions.DateDiffYear(p.FamilyMember!.DateOfBirth, p.Registration.Event.StartDate) < 13);
        else if (f.Age == AgeBand.From13)
            q = q.Where(p => EF.Functions.DateDiffYear(p.FamilyMember!.DateOfBirth, p.Registration.Event.StartDate) >= 13);

        return q;
    }

    public Task<int> CountAsync(ChildFilter f, CancellationToken ct = default)
        => Base(f).CountAsync(ct);

    public IQueryable<ChildRow> Query(ChildFilter f)
        => Base(f).Select(p => new ChildRow
        {
            ParticipantId = p.ParticipantId,
            RegistrationId = p.RegistrationId,
            ChildFirstName = p.FamilyMember!.FirstName,
            ChildLastName = p.FamilyMember!.LastName,
            Age = EF.Functions.DateDiffYear(p.FamilyMember!.DateOfBirth, p.Registration.Event.StartDate),
            EventName = p.Registration.Event.Name,
            TshirtSize = p.TshirtSize,
            ParentFirstName = p.Registration.User.FirstName,
            ParentLastName = p.Registration.User.LastName,
            ParentEmail = p.Registration.User.Email,
            GroupCode = p.Registration.GroupCode
        });

    public IQueryable<ChildRow> ApplySort(IQueryable<ChildRow> q, string? sort, string? dir)
    {
        bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<ChildRow> o = (sort?.ToLowerInvariant()) switch
        {
            "child"  => desc ? q.OrderByDescending(x => x.ChildLastName)  : q.OrderBy(x => x.ChildLastName),
            "age"    => desc ? q.OrderByDescending(x => x.Age)            : q.OrderBy(x => x.Age),
            "event"  => desc ? q.OrderByDescending(x => x.EventName)      : q.OrderBy(x => x.EventName),
            "parent" => desc ? q.OrderByDescending(x => x.ParentLastName) : q.OrderBy(x => x.ParentLastName),
            "tshirt" => desc ? q.OrderByDescending(x => x.TshirtSize)     : q.OrderBy(x => x.TshirtSize),
            _        => desc ? q.OrderByDescending(x => x.ChildLastName)  : q.OrderBy(x => x.ChildLastName),
        };

        return o.ThenBy(x => x.ParticipantId);  // стабильный tiebreaker
    }

    public async Task<IReadOnlyList<EventOption>> EventsAsync(CancellationToken ct = default)
        => await _db.Events.AsNoTracking()
            .OrderBy(e => e.StartDate)
            .Select(e => new EventOption(e.Id, e.Name))
            .ToListAsync(ct);
}
