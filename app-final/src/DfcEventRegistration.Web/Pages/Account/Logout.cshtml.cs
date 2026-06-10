using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfcEventRegistration.Web.Pages.Account;

/// <summary>Выход. POST (CSRF-safe). AllowAnonymous — чтобы не залочиться, если роль не назначена.</summary>
[AllowAnonymous]
public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Account/Login");
    }

    public IActionResult OnGet() => RedirectToPage("/Account/Login");
}
