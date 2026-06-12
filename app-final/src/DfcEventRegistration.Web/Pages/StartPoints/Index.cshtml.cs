using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.StartPoints;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public IndexModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public int SessionId { get; set; }

    public record Row(int StartPointId, string Name, TimeOnly? StartTime, TimeOnly? EndTime,
        int? Capacity, int DisplayOrder, int Registrations);

    public string? SessionName { get; private set; }
    public Guid EventId { get; private set; }                 // для ссылки «назад к сессиям»
    public IReadOnlyList<Row> StartPoints { get; private set; } = Array.Empty<Row>();
    public string? Error { get; set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        var session = await _db.EventSessions.AsNoTracking()
            .Where(s => s.SessionId == SessionId)
            .Select(s => new { s.Name, s.EventId })
            .FirstOrDefaultAsync(ct);
        if (session is null) return NotFound();
        SessionName = session.Name;
        EventId = session.EventId;

        StartPoints = await _db.EventStartPoints.AsNoTracking()
            .Where(p => p.SessionId == SessionId)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .Select(p => new Row(
                p.StartPointId, p.Name, p.StartTime, p.EndTime, p.Capacity, p.DisplayOrder,
                _db.RegistrationSessions.Count(rs => rs.StartPointId == p.StartPointId)))
            .ToListAsync(ct);

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int startPointId, bool force, CancellationToken ct)
    {
        int regs = await _write.StartPointRegistrationCountAsync(startPointId, ct);

        // Каскад с предпросмотром: если на точку есть выборы регистраций — требуем подтверждения.
        if (regs > 0 && !force)
        {
            Error = $"This start point has {regs:N0} registration selection(s). Tick “cascade” to delete them too.";
            return await LoadAsync(ct);
        }

        await _write.DeleteStartPointCascadeAsync(startPointId, ct);
        Notice = regs > 0
            ? $"Start point deleted with {regs:N0} selection(s)."
            : "Start point deleted.";
        return RedirectToPage(new { sessionId = SessionId });
    }
}
