using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Users;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminWriteService _write;

    public EditModel(AppDbContext db, AdminWriteService write)
    {
        _db = db;
        _write = write;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }   // UserId
    [BindProperty] public InputModel Input { get; set; } = new();

    public IReadOnlyList<FamilyMember> Family { get; private set; } = Array.Empty<FamilyMember>();
    public int RegistrationCount { get; private set; }

    public int MaxFamily => AdminWriteService.MaxChildrenPerUser;
    public bool FamilyLimitReached => Family.Count >= MaxFamily;

    public string? Error { get; set; }
    [TempData] public string? Notice { get; set; }

    public class InputModel
    {
        public int UserId { get; set; }

        [Required, StringLength(100)] public string FirstName { get; set; } = "";
        [Required, StringLength(100)] public string LastName { get; set; } = "";
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
        [StringLength(32)] public string? Phone { get; set; }
        public DateTime? DateOfBirth { get; set; }

        [StringLength(100)] public string? NewFamilyFirstName { get; set; }
        [StringLength(100)] public string? NewFamilyLastName { get; set; }
        public DateTime? NewFamilyDateOfBirth { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        => await LoadAsync(ct, fillInput: true) ? Page() : NotFound();

    private async Task<bool> LoadAsync(CancellationToken ct, bool fillInput)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == Id, ct);
        if (user is null) return false;

        Family = await _db.FamilyMembers.AsNoTracking()
            .Where(f => f.UserId == Id)
            .OrderBy(f => f.FamilyMemberId)
            .ToListAsync(ct);

        RegistrationCount = await _db.EventRegistrations.CountAsync(r => r.UserId == Id, ct);

        if (fillInput)
        {
            Input = new InputModel
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Phone = user.Phone,
                DateOfBirth = user.DateOfBirth,
            };
        }
        return true;
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(ct, fillInput: false); return Page(); }

        var (ok, err) = await _write.UpdateUserAsync(new(
            Id, Input.FirstName.Trim(), Input.LastName.Trim(), Input.Email.Trim(),
            Input.Phone, Input.DateOfBirth), ct);

        if (!ok) { Error = err; await LoadAsync(ct, fillInput: false); return Page(); }

        Notice = "Saved.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostAddFamilyAsync(CancellationToken ct)
    {
        // Эта форма НЕ редактирует персону. Чистим ModelState, иначе [Required] полей персоны
        // (их нет в этой форме) дадут ложные ошибки и заставят asp-for показать пустые поля.
        ModelState.Clear();

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Input.NewFamilyFirstName)) errors.Add("First name is required.");
        if (string.IsNullOrWhiteSpace(Input.NewFamilyLastName))  errors.Add("Last name is required.");
        if (Input.NewFamilyDateOfBirth is null)                  errors.Add("Date of birth is required.");

        if (errors.Count == 0)
        {
            var (ok, err) = await _write.AddFamilyMemberAsync(Id,
                Input.NewFamilyFirstName!.Trim(), Input.NewFamilyLastName!.Trim(), Input.NewFamilyDateOfBirth, ct);
            if (ok) { Notice = "Family member added."; return RedirectToPage(new { Id }); }
            errors.Add(err!);
        }

        Error = string.Join(" ", errors);
        var keep = (Input.NewFamilyFirstName, Input.NewFamilyLastName, Input.NewFamilyDateOfBirth);
        await LoadAsync(ct, fillInput: true);                 // вернуть поля персоны из БД
        (Input.NewFamilyFirstName, Input.NewFamilyLastName, Input.NewFamilyDateOfBirth) = keep;  // сохранить ввод
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteFamilyAsync(int familyMemberId, CancellationToken ct)
    {
        await _write.DeleteFamilyMemberAsync(familyMemberId, ct);
        Notice = "Family member removed from the roster and all registrations.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostDeleteUserAsync(CancellationToken ct)
    {
        await _write.DeleteUserCascadeAsync(Id, ct);
        Notice = "User and all related data deleted.";
        return RedirectToPage("/Users/Index");
    }
}
