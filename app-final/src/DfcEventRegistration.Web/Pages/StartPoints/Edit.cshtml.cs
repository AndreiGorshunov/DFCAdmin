using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.StartPoints;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public EditModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }            // StartPointId
    [BindProperty(SupportsGet = true)] public int SessionId { get; set; }      // нужен для нового + редиректа
    [BindProperty] public InputModel Input { get; set; } = new();

    public bool IsNew => Id is null || Id == 0;
    public string? SessionName { get; private set; }
    public Guid EventId { get; private set; }
    public string? Error { get; set; }

    public class InputModel
    {
        public int? Id { get; set; }
        [Required, StringLength(256)] public string Name { get; set; } = "";
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        [Range(0, int.MaxValue)] public int? Capacity { get; set; }
        [Range(0, int.MaxValue)] public int DisplayOrder { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!IsNew)
        {
            var p = await _db.EventStartPoints.AsNoTracking().FirstOrDefaultAsync(x => x.StartPointId == Id, ct);
            if (p is null) return NotFound();
            SessionId = p.SessionId;   // источник истины — сама точка
            Input = new InputModel
            {
                Id = p.StartPointId, Name = p.Name, StartTime = p.StartTime, EndTime = p.EndTime,
                Capacity = p.Capacity, DisplayOrder = p.DisplayOrder
            };
        }

        if (!await LoadSessionAsync(ct)) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadSessionAsync(ct);
            return Page();
        }

        var (ok, err, _) = await _write.UpsertStartPointAsync(new(
            IsNew ? null : Id, SessionId, Input.Name.Trim(),
            Input.StartTime, Input.EndTime, Input.Capacity, Input.DisplayOrder), ct);

        if (!ok)
        {
            Error = err;
            await LoadSessionAsync(ct);
            return Page();
        }

        return RedirectToPage("/StartPoints/Index", new { sessionId = SessionId });
    }

    private async Task<bool> LoadSessionAsync(CancellationToken ct)
    {
        var s = await _db.EventSessions.AsNoTracking()
            .Where(x => x.SessionId == SessionId)
            .Select(x => new { x.Name, x.EventId })
            .FirstOrDefaultAsync(ct);
        if (s is null) return false;
        SessionName = s.Name;
        EventId = s.EventId;
        return true;
    }
}
