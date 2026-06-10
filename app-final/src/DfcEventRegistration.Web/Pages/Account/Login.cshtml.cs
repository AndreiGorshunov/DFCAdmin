using System.Security.Claims;
using DfcEventRegistration.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfcEventRegistration.Web.Pages.Account;

/// <summary>
/// DEV-ЗАГЛУШКА входа. Позволяет гонять приложение с включённой аутентификацией БЕЗ
/// реального IdP: выбираешь роль -> выдаётся cookie с соответствующими claims.
///
/// TODO(auth): когда подключим реальный провайдер (OIDC/SAML/UAE Pass), здесь вместо
/// dev-формы будет Challenge() внешней схемы, а роль подтянет AllowlistRoleTransformation
/// (email -> роль). Dev-форма работает ТОЛЬКО в Development.
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly IWebHostEnvironment _env;
    public LoginModel(IWebHostEnvironment env) => _env = env;

    public bool DevMode => _env.IsDevelopment();
    public static string[] AllRoles => Roles.All;

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public string Email { get; set; } = "admin@dfc.local";
    [BindProperty] public string Role { get; set; } = Roles.Admin;

    public IActionResult OnGet()
    {
        // Dev: показываем форму. Прод без IdP: разметка покажет "Sign-in unavailable".
        // Реальная защита от выпуска cookie в проде — в OnPost (ниже).
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!DevMode) return Forbid();

        var role = Roles.Normalize(Role) ?? Roles.Partner;
        var email = string.IsNullOrWhiteSpace(Email) ? $"{role.ToLowerInvariant()}@dfc.local" : Email.Trim();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Email, email),
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
}
