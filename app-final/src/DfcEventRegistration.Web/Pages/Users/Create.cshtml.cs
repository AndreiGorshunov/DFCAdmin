using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfcEventRegistration.Web.Pages.Users;

public class CreateModel : PageModel
{
    private readonly AdminWriteService _write;
    public CreateModel(AdminWriteService write) => _write = write;

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? Error { get; set; }

    public class InputModel
    {
        [Required, StringLength(100)] public string FirstName { get; set; } = "";
        [Required, StringLength(100)] public string LastName { get; set; } = "";
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
        [StringLength(32)] public string? Phone { get; set; }
        public DateTime? DateOfBirth { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();

        var (ok, err, id) = await _write.CreateUserAsync(
            Input.FirstName.Trim(), Input.LastName.Trim(), Input.Email.Trim(),
            Input.Phone, Input.DateOfBirth, ct);

        if (!ok) { Error = err; return Page(); }

        // На Edit, где доступен ростер семьи.
        return RedirectToPage("/Users/Edit", new { Id = id });
    }
}
