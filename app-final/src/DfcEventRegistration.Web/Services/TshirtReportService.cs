using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Services;

public class TshirtReportService
{
    private static readonly string[] SizeOrder = { "XS", "S", "M", "L", "XL", "XXL" };

    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public TshirtReportService(AppDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    public async Task<List<TshirtReportRow>> BuildAsync(Guid? eventId, CancellationToken ct = default)
    {
        var q = _db.RegistrationParticipants.AsNoTracking()
            .Where(p => p.TshirtSize != null);

        if (eventId is Guid ev)
            q = q.Where(p => p.EventId == ev);

        // Requested = все участники с размером.
        // Collected  = регистрация уже CheckedIn (прокси "получил" — отдельного
        //              признака выдачи в схеме нет, см. README).
        var grouped = await q
            .GroupBy(p => p.TshirtSize!)
            .Select(g => new
            {
                Size = g.Key,
                Requested = g.Count(),
                Collected = g.Count(p => p.Registration.Status == RegistrationStatus.CheckedIn)
            })
            .ToListAsync(ct);

        var stock = _cfg.GetSection("TshirtStock").Get<Dictionary<string, int>>()
                    ?? new Dictionary<string, int>();

        var rows = grouped.Select(x => new TshirtReportRow
        {
            Size = x.Size,
            Requested = x.Requested,
            Collected = x.Collected,
            Stock = stock.TryGetValue(x.Size, out var s) ? s : (int?)null
        });

        // Упорядочиваем по каноническому размеру; неизвестные размеры — в конец.
        return rows
            .OrderBy(r =>
            {
                var i = Array.IndexOf(SizeOrder, r.Size);
                return i < 0 ? int.MaxValue : i;
            })
            .ThenBy(r => r.Size)
            .ToList();
    }

    public async Task<List<EventOption>> EventsAsync(CancellationToken ct = default)
        => await _db.Events.AsNoTracking()
            .OrderBy(e => e.StartDate)
            .Select(e => new EventOption(e.Id, e.Name))
            .ToListAsync(ct);
}
