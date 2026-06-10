using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfcEventRegistration.Web.Pages.Account;

[AllowAnonymous]
public class DeniedModel : PageModel
{
    public void OnGet() { }
}
