using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Models;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.StartPoints;

/// <summary>
/// Просмотр регистрантов конкретной точки старта + стандартный поиск (Q: email/имя/фамилия/телефон,
/// та же маршрутизация, что и на /Registrants) и фильтр по статусу. Грейн = регистрация
/// (одна строка на регистрацию). Под /StartPoints -> политика CanManage (Admin); партнёр/стюард
/// могут фильтровать по точке на /Registrants. Листинг ограничен ёмкостью точки, поэтому без пейджинга.
/// </summary>
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly RegistrantQueryService _svc;

    public DetailsModel(AppDbContext db, RegistrantQueryService svc)
    {
        _db = db;
        _svc = svc;
    }

    [BindProperty(SupportsGet = true)] public int StartPointId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public RegistrationStatus? Status { get; set; }

    private const int Cap = 1000;

    public string StartPointName { get; private set; } = "";
    public string SessionName { get; private set; } = "";
    public string EventName { get; private set; } = "";
    public int SessionId { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public int? Capacity { get; private set; }
    public int Occupied { get; private set; }
    public int? Remaining => Capacity is int c ? c - Occupied : null;

    public IReadOnlyList<RegistrantRow> Rows { get; private set; } = Array.Empty<RegistrantRow>();
    public bool Capped { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var sp = await _db.EventStartPoints.AsNoTracking()
            .Where(p => p.StartPointId == StartPointId)
            .Select(p => new
            {
                p.Name, p.StartTime, p.EndTime, p.Capacity, p.SessionId,
                SessionName = p.Session.Name, EventName = p.Session.Event.Name
            })
            .FirstOrDefaultAsync(ct);
        if (sp is null) return NotFound();

        StartPointName = sp.Name;
        SessionName = sp.SessionName;
        EventName = sp.EventName;
        SessionId = sp.SessionId;
        StartTime = sp.StartTime;
        EndTime = sp.EndTime;
        Capacity = sp.Capacity;
        Occupied = await _db.RegistrationSessions.CountAsync(rs => rs.StartPointId == StartPointId, ct);

        // Переиспользуем поиск регистрантов: фильтр зафиксирован на этой точке старта.
        var filter = new RegistrantFilter { StartPointId = StartPointId, Q = Q, Status = Status };
        var rows = await _svc.ApplySort(_svc.Query(filter), null, null).Take(Cap + 1).ToListAsync(ct);
        Capped = rows.Count > Cap;
        Rows = Capped ? rows.Take(Cap).ToList() : rows;

        return Page();
    }
}
