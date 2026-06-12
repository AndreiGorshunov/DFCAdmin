using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.CheckIn;

/// <summary>
/// Чек-ин на площадке (политика CanCheckIn: Admin + Steward). Стюард сканирует QR
/// (или вводит номер регистрации) -> видит выбранные сессии/точки этой регистрации
/// и отмечает чек-ин поштучно. Балк-загрузки нет: чек-ин по своей природе индивидуален.
/// </summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public IndexModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }   // QR-код или номер регистрации

    public bool Searched => !string.IsNullOrWhiteSpace(Q);
    public bool Found { get; private set; }

    public long RegistrationId { get; private set; }
    public string RegistrantName { get; private set; } = "";
    public string EventName { get; private set; } = "";
    public string? QrCode { get; private set; }
    public int PartySize { get; private set; }   // участники регистрации (родитель + дети)
    public IReadOnlyList<SessionRow> Sessions { get; private set; } = Array.Empty<SessionRow>();

    public Guid EventId { get; private set; }
    public IReadOnlyList<StartPointOptionRow> StartPointOptions { get; private set; } = Array.Empty<StartPointOptionRow>();

    public record StartPointOptionRow(int StartPointId, string Label);

    [TempData] public string? Notice { get; set; }

    public record SessionRow(long RegistrationSessionId, string SessionName, string StartPointName,
        TimeOnly? StartTime, TimeOnly? EndTime, bool CheckedIn, DateTime? CheckInTime);

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (Searched) await ResolveAsync(Q!.Trim(), ct);
    }

    public async Task<IActionResult> OnPostSetCheckedInAsync(
        long registrationSessionId, bool checkedIn, string? q, CancellationToken ct)
    {
        await _write.SetSessionCheckedInAsync(registrationSessionId, checkedIn, ct);
        Notice = checkedIn ? "Checked in." : "Check-in undone.";
        return RedirectToPage(new { q });
    }

    /// <summary>Walk-up: стьюард назначает точку на месте и сразу отмечает чек-ин
    /// (человек уже пришёл). Емкость точки проверяется в AssignSessionAsync.</summary>
    public async Task<IActionResult> OnPostAssignAsync(
        long registrationId, int startPointId, string? q, CancellationToken ct)
    {
        var sessionId = await _write.SessionIdOfStartPointAsync(startPointId, ct);
        if (sessionId == 0) { Notice = "Start point not found."; return RedirectToPage(new { q }); }

        var (ok, err) = await _write.AssignSessionAsync(registrationId, sessionId, startPointId, ct, checkInNow: true);
        Notice = ok ? "Assigned and checked in." : err;
        return RedirectToPage(new { q });
    }

    private async Task ResolveAsync(string query, CancellationToken ct)
    {
        // 1) по QR-коду (точное совпадение), 2) фолбэк — по номеру регистрации.
        var reg = await _db.EventRegistrations.AsNoTracking()
            .Include(r => r.User).Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.QRCode == query, ct);

        if (reg is null && long.TryParse(query, out var rid))
        {
            reg = await _db.EventRegistrations.AsNoTracking()
                .Include(r => r.User).Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.RegistrationId == rid, ct);
        }

        if (reg is null) { Found = false; return; }

        Found = true;
        RegistrationId = reg.RegistrationId;
        RegistrantName = $"{reg.User.FirstName} {reg.User.LastName}".Trim();
        EventName = reg.Event.Name;
        QrCode = reg.QRCode;
        PartySize = await _db.RegistrationParticipants.CountAsync(p => p.RegistrationId == reg.RegistrationId, ct);

        Sessions = await _db.RegistrationSessions.AsNoTracking()
            .Where(rs => rs.RegistrationId == reg.RegistrationId)
            .OrderBy(rs => rs.Session.Name).ThenBy(rs => rs.StartPoint.DisplayOrder)
            .Select(rs => new SessionRow(
                rs.RegistrationSessionId, rs.Session.Name, rs.StartPoint.Name,
                rs.StartPoint.StartTime, rs.StartPoint.EndTime, rs.CheckedIn, rs.CheckInTime))
            .ToListAsync(ct);

        // Точки старта события для walk-up — исключаем сессии, уже выбранные этой регистрацией
        // (одна точка на сессию). Точка задаёт свою сессию.
        EventId = reg.EventId;
        StartPointOptions = await _db.EventStartPoints.AsNoTracking()
            .Where(sp => sp.Session.EventId == reg.EventId
                && !_db.RegistrationSessions.Any(rs => rs.RegistrationId == reg.RegistrationId && rs.SessionId == sp.SessionId))
            .OrderBy(sp => sp.Session.Name).ThenBy(sp => sp.DisplayOrder)
            .Select(sp => new StartPointOptionRow(sp.StartPointId, sp.Session.Name + " — " + sp.Name))
            .ToListAsync(ct);
    }
}
