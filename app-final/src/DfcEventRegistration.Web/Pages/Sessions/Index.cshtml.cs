using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Sessions;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public IndexModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public Guid EventId { get; set; }

    public record Row(int SessionId, string Name, string? Description, int? MaxParticipants,
        int StartPoints, int Registrations);

    public string? EventName { get; private set; }
    public IReadOnlyList<Row> Sessions { get; private set; } = Array.Empty<Row>();
    public string? Error { get; set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (EventId == Guid.Empty) return RedirectToPage("/Events/Index");
        return await LoadAsync(ct);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        EventName = await _db.Events.AsNoTracking()
            .Where(e => e.Id == EventId).Select(e => e.Name).FirstOrDefaultAsync(ct);
        if (EventName is null) return NotFound();

        Sessions = await _db.EventSessions.AsNoTracking()
            .Where(s => s.EventId == EventId)
            .OrderBy(s => s.Name)
            .Select(s => new Row(
                s.SessionId, s.Name, s.Description, s.MaxParticipants,
                _db.EventStartPoints.Count(p => p.SessionId == s.SessionId),
                _db.RegistrationSessions.Count(rs => rs.SessionId == s.SessionId)))
            .ToListAsync(ct);

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int sessionId, bool force, CancellationToken ct)
    {
        var (startPoints, regs) = await _write.SessionImpactAsync(sessionId, ct);

        // Каскад с предпросмотром: если есть точки старта или выборы регистраций — требуем подтверждения.
        if ((startPoints > 0 || regs > 0) && !force)
        {
            Error = $"This session has {startPoints:N0} start point(s) and {regs:N0} registration selection(s). " +
                    "Tick “cascade” to delete them too.";
            return await LoadAsync(ct);
        }

        await _write.DeleteSessionCascadeAsync(sessionId, ct);
        Notice = (startPoints > 0 || regs > 0)
            ? $"Session deleted with {startPoints:N0} start point(s) and {regs:N0} selection(s)."
            : "Session deleted.";
        return RedirectToPage(new { eventId = EventId });
    }
}
