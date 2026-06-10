using DfcEventRegistration.Web.Models;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Users;

public class IndexModel : PageModel
{
    private readonly UserQueryService _svc;
    public IndexModel(UserQueryService svc) => _svc = svc;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dir { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;

    public IReadOnlyList<UserRow> Rows { get; private set; } = Array.Empty<UserRow>();
    public bool HasNext { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (PageNo < 1) PageNo = 1;
        PageSize = Math.Clamp(PageSize, 10, 200);

        var query = _svc.ApplySort(_svc.Query(Filter()), Sort, Dir);

        var rows = await query
            .Skip((PageNo - 1) * PageSize)
            .Take(PageSize + 1)               // +1 -> есть ли следующая, без COUNT
            .ToListAsync(ct);

        HasNext = rows.Count > PageSize;
        Rows = HasNext ? rows.Take(PageSize).ToList() : rows;
    }

    public async Task<IActionResult> OnGetCountAsync(CancellationToken ct)
        => new JsonResult(await _svc.CountAsync(Filter(), ct));

    private UserFilter Filter() => new() { Q = Q };

    public string NextDir(string column)
        => (string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase))
            ? "desc" : "asc";

    public string SortIndicator(string column)
    {
        if (string.IsNullOrEmpty(Sort) && string.Equals(column, "last", StringComparison.OrdinalIgnoreCase))
            return " ▲";
        if (!string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)) return "";
        return string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? " ▼" : " ▲";
    }
}
