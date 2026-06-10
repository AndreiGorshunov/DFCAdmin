using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Events;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public EditModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public bool IsNew => Id is null || Id == Guid.Empty;
    public string? Error { get; set; }

    public class InputModel
    {
        public Guid? Id { get; set; }
        [Required, StringLength(256)] public string Name { get; set; } = "";
        public string? Description { get; set; }
        [Required] public DateTime StartDate { get; set; }
        [Required] public DateTime EndDate { get; set; }
        public DateTime? RegistrationOpeningDate { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!IsNew)
        {
            var e = await _db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Id, ct);
            if (e is null) return NotFound();

            Input = new InputModel
            {
                Id = e.Id,
                Name = e.Name,
                Description = e.Description,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                RegistrationOpeningDate = e.RegistrationOpeningDate
            };
        }
        else
        {
            // разумные дефолты для нового события
            var start = DateTime.Today.AddMonths(1).AddHours(7);
            Input = new InputModel
            {
                StartDate = start,
                EndDate = start.AddHours(5),
                RegistrationOpeningDate = DateTime.Today
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var (ok, err, _) = await _write.UpsertEventAsync(new(
            IsNew ? null : Id,
            Input.Name.Trim(), Input.Description,
            Input.StartDate, Input.EndDate, Input.RegistrationOpeningDate), ct);

        if (!ok)
        {
            Error = err;
            return Page();
        }

        return RedirectToPage("/Events/Index");
    }
}
