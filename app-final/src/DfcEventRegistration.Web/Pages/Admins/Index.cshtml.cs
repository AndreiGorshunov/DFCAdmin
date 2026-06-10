using System.ComponentModel.DataAnnotations;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuthRoles = DfcEventRegistration.Web.Auth.Roles;

namespace DfcEventRegistration.Web.Pages.Admins;

/// <summary>
/// Управление доступом (Admin-only — папка /Admins под политикой CanManage).
/// Сотрудник DFC выдаёт/отзывает доступ, в т.ч. партнёрам. Все действия пишутся в аудит.
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

    public IReadOnlyList<AdminUser> Users { get; private set; } = Array.Empty<AdminUser>();
    public string[] AllRoles => AuthRoles.All;

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? Error { get; set; }
    [TempData] public string? Notice { get; set; }

    public class InputModel
    {
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
        [Required] public string Role { get; set; } = AuthRoles.Partner;
        [StringLength(200)] public string? DisplayName { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        Users = await _db.AdminUsers.AsNoTracking()
            .OrderByDescending(a => a.IsActive)
            .ThenBy(a => a.Role)
            .ThenBy(a => a.Email)
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostGrantAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }

        var (ok, err) = await _write.GrantAccessAsync(
            new(Input.Email.Trim(), Input.Role, Input.DisplayName, Input.ExpiresAtUtc), ct);

        if (!ok) { Error = err; await LoadAsync(ct); return Page(); }

        Notice = $"Access granted to {Input.Email.Trim()}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, bool active, CancellationToken ct)
    {
        await _write.SetAdminActiveAsync(id, active, ct);
        Notice = active ? "Access reactivated." : "Access revoked.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await _write.DeleteAdminUserAsync(id, ct);
        Notice = "Access record deleted.";
        return RedirectToPage();
    }
}
