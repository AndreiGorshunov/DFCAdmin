using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Sessions;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public EditModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }            // SessionId
    [BindProperty(SupportsGet = true)] public Guid EventId { get; set; }       // нужен для нового + редиректа
    [BindProperty] public InputModel Input { get; set; } = new();

    public bool IsNew => Id is null || Id == 0;
    public string? EventName { get; private set; }
    public string? Error { get; set; }

    public class InputModel
    {
        public int? Id { get; set; }
        [Required, StringLength(256)] public string Name { get; set; } = "";
        public string? Description { get; set; }
        [Range(0, int.MaxValue)] public int? MaxParticipants { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!IsNew)
        {
            var s = await _db.EventSessions.AsNoTracking().FirstOrDefaultAsync(x => x.SessionId == Id, ct);
            if (s is null) return NotFound();
            EventId = s.EventId;   // источник истины — сама сессия
            Input = new InputModel { Id = s.SessionId, Name = s.Name, Description = s.Description, MaxParticipants = s.MaxParticipants };
        }

        if (EventId == Guid.Empty) return RedirectToPage("/Events/Index");
        EventName = await _db.Events.AsNoTracking().Where(e => e.Id == EventId).Select(e => e.Name).FirstOrDefaultAsync(ct);
        if (EventName is null) return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadEventNameAsync(ct);
            return Page();
        }

        var (ok, err, _) = await _write.UpsertSessionAsync(new(
            IsNew ? null : Id, EventId, Input.Name.Trim(), Input.Description, Input.MaxParticipants), ct);

        if (!ok)
        {
            Error = err;
            await LoadEventNameAsync(ct);
            return Page();
        }

        return RedirectToPage("/Sessions/Index", new { eventId = EventId });
    }

    private async Task LoadEventNameAsync(CancellationToken ct)
        => EventName = await _db.Events.AsNoTracking().Where(e => e.Id == EventId).Select(e => e.Name).FirstOrDefaultAsync(ct);
}
