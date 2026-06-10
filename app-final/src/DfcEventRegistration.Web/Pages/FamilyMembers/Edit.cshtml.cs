using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.FamilyMembers;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public EditModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }        // FamilyMemberId
    [BindProperty(SupportsGet = true)] public long? RegId { get; set; }   // куда вернуться

    [BindProperty] public InputModel Input { get; set; } = new();
    public string OwnerName { get; private set; } = "";
    public string? Error { get; set; }

    public class InputModel
    {
        public int FamilyMemberId { get; set; }
        [Required, StringLength(100)] public string FirstName { get; set; } = "";
        [Required, StringLength(100)] public string LastName { get; set; } = "";
        public DateTime? DateOfBirth { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var fm = await _db.FamilyMembers.AsNoTracking().FirstOrDefaultAsync(f => f.FamilyMemberId == Id, ct);
        if (fm is null) return NotFound();

        OwnerName = await _db.Users.Where(u => u.UserId == fm.UserId)
            .Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? "";

        Input = new InputModel
        {
            FamilyMemberId = fm.FamilyMemberId,
            FirstName = fm.FirstName,
            LastName = fm.LastName,
            DateOfBirth = fm.DateOfBirth
        };
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var (ok, err) = await _write.UpdateFamilyMemberAsync(
            new(Input.FamilyMemberId, Input.FirstName.Trim(), Input.LastName.Trim(), Input.DateOfBirth), ct);

        if (!ok) { Error = err; return Page(); }

        return Back();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        await _write.DeleteFamilyMemberAsync(Id, ct);
        return Back();
    }

    private IActionResult Back()
        => RegId is long rid
            ? RedirectToPage("/Registrants/Edit", new { Id = rid })
            : RedirectToPage("/Registrants/Index");
}
