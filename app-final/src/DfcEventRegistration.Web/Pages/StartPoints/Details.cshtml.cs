using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.StartPoints;

/// <summary>
/// Регистранты конкретной точки старта + поиск (Q: email/имя/фамилия/телефон, как на /Registrants)
/// и фильтр по статусу. Грейн = регистрация (строка RegistrationSessions этой точки). Колонка
/// «Checked in here» = чек-ин ИМЕННО на этой точке. Пагинация offset с теми же размерами страниц,
/// что и на /Registrants (точка ограничена ёмкостью, объёмы небольшие). Под /StartPoints -> Admin.
/// </summary>
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserSearchService _search;

    public DetailsModel(AppDbContext db, UserSearchService search)
    {
        _db = db;
        _search = search;
    }

    [BindProperty(SupportsGet = true)] public int StartPointId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public RegistrationStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

    public string StartPointName { get; private set; } = "";
    public string SessionName { get; private set; } = "";
    public string EventName { get; private set; } = "";
    public int SessionId { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public int? Capacity { get; private set; }
    public int Occupied { get; private set; }
    public int CheckedInHereCount { get; private set; }

    public bool IsFull => Capacity is int c && Occupied >= c;
    public int? Free => Capacity is int c ? Math.Max(0, c - Occupied) : null;

    public record Row(long RegistrationId, string Email, string FirstName, string LastName,
        RegistrationStatus Status, bool CheckedInHere, DateTime? CheckInTime);

    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();
    public int Total { get; private set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        PageSize = Math.Clamp(PageSize, 10, 200);
        if (PageNo < 1) PageNo = 1;

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
        CheckedInHereCount = await _db.RegistrationSessions.CountAsync(rs => rs.StartPointId == StartPointId && rs.CheckedIn, ct);

        var q = _db.RegistrationSessions.AsNoTracking().Where(rs => rs.StartPointId == StartPointId);

        if (Status is RegistrationStatus st)
            q = q.Where(rs => rs.Registration.Status == st);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var ids = _search.MatchUserIds(Q.Trim());   // та же маршрутизация email/имя/телефон, что и на /Registrants
            q = q.Where(rs => ids.Contains(rs.Registration.UserId));
        }

        Total = await q.CountAsync(ct);

        Rows = await q
            .OrderBy(rs => rs.Registration.RegistrantLastName).ThenBy(rs => rs.RegistrationId)
            .Skip((PageNo - 1) * PageSize).Take(PageSize)
            .Select(rs => new Row(
                rs.RegistrationId,
                rs.Registration.User.Email,
                rs.Registration.User.FirstName,
                rs.Registration.RegistrantLastName,
                rs.Registration.Status,
                rs.CheckedIn,
                rs.CheckInTime))
            .ToListAsync(ct);

        return Page();
    }
}
