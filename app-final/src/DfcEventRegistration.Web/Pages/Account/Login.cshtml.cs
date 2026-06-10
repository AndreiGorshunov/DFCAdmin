using System.Security.Claims;
using DfcEventRegistration.Web.Auth;
using DfcEventRegistration.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Account;

/// <summary>
/// DEV-ЗАГЛУШКА входа. Роль НЕ выбирается вручную — она резолвится из таблицы
/// AdminUsers по email (тот же путь, что будет у реального IdP). Так dev-вход
/// прогоняет настоящую логику авторизации.
///
/// TODO(auth): в проде вместо dev-формы — Challenge("oidc") (см. OidcAuthentication.cs);
/// роль подтянет OnTokenValidated через тот же IRoleResolver.
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly IWebHostEnvironment _env;
    private readonly IRoleResolver _roles;
    private readonly AppDbContext _db;

    public LoginModel(IWebHostEnvironment env, IRoleResolver roles, AppDbContext db)
    {
        _env = env;
        _roles = roles;
        _db = db;
    }

    public bool DevMode => _env.IsDevelopment();

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public string Email { get; set; } = "";

    public List<KnownUser> KnownUsers { get; private set; } = new();
    public record KnownUser(string Email, string Role, string? DisplayName);

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (DevMode) await LoadKnownAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!DevMode) return Forbid();

        var email = Email?.Trim();
        var role = await _roles.ResolveRoleAsync(email, ct);
        if (role is null)
        {
            ModelState.AddModelError(string.Empty,
                "This email has no active access. Add it on the Admins page (as an existing admin) or seed it via sql/05_auth.sql.");
            await LoadKnownAsync(ct);
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email!),
            new(ClaimTypes.Email, email!),
            new(ClaimTypes.Role, role),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        // LocalRedirect защищает от open-redirect (внешние URL отвергаются).
        return LocalRedirect(string.IsNullOrEmpty(ReturnUrl) ? "/" : ReturnUrl);
    }

    private async Task LoadKnownAsync(CancellationToken ct)
    {
        try
        {
            KnownUsers = await _db.AdminUsers.AsNoTracking()
                .Where(a => a.IsActive)
                .OrderBy(a => a.Role).ThenBy(a => a.Email)
                .Select(a => new KnownUser(a.Email, a.Role, a.DisplayName))
                .ToListAsync(ct);
        }
        catch
        {
            // Таблица AdminUsers ещё не создана (не прогнан 05_auth.sql) — не валим логин-страницу,
            // оставляем список пустым; разметка покажет подсказку прогнать скрипт.
            KnownUsers = new();
        }

        if (string.IsNullOrEmpty(Email) && KnownUsers.Count > 0)
            Email = KnownUsers[0].Email;
    }
}
