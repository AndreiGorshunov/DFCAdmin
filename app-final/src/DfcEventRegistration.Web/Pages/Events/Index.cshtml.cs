using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Events;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public IndexModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    public record Row(Guid Id, string Name, DateTime StartDate, DateTime EndDate,
        DateTime? RegistrationOpeningDate, int Registrations);

    public IReadOnlyList<Row> Events { get; private set; } = Array.Empty<Row>();
    public string? Error { get; set; }
    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        // COUNT по событию опирается на индекс EventRegistrations(EventId) — для нескольких
        // событий это дёшево. Если событий станет много, вынеси счётчик в отдельную колонку.
        Events = await _db.Events.AsNoTracking()
            .OrderBy(e => e.StartDate)
            .Select(e => new Row(
                e.Id, e.Name, e.StartDate, e.EndDate, e.RegistrationOpeningDate,
                _db.EventRegistrations.Count(r => r.EventId == e.Id)))
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, bool force, CancellationToken ct)
    {
        var (regs, parts) = await _write.EventImpactAsync(id, ct);

        // Защита: если у события есть регистрации — требуем явного подтверждения каскада.
        if (regs > 0 && !force)
        {
            Error = $"This event has {regs:N0} registration(s) and {parts:N0} participant(s). " +
                    "Tick “cascade” to delete them too.";
            await LoadAsync(ct);
            return Page();
        }

        await _write.DeleteEventCascadeAsync(id, ct);
        Notice = regs > 0
            ? $"Event deleted with {regs:N0} registration(s) and {parts:N0} participant(s)."
            : "Event deleted.";
        return RedirectToPage();
    }
}
