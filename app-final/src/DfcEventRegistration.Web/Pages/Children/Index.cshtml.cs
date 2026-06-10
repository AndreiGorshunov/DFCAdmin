using DfcEventRegistration.Web.Models;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Children;

public class IndexModel : PageModel
{
    private readonly ChildQueryService _svc;
    public IndexModel(ChildQueryService svc) => _svc = svc;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? EventId { get; set; }
    [BindProperty(SupportsGet = true)] public AgeBand? Age { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dir { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;

    public IReadOnlyList<ChildRow> Rows { get; private set; } = Array.Empty<ChildRow>();
    public IReadOnlyList<EventOption> Events { get; private set; } = Array.Empty<EventOption>();
    public bool HasNext { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (PageNo < 1) PageNo = 1;
        PageSize = Math.Clamp(PageSize, 10, 200);

        Events = await _svc.EventsAsync(ct);

        var f = Filter();
        var query = _svc.ApplySort(_svc.Query(f), Sort, Dir);

        // PageSize + 1 -> знаем, есть ли следующая страница, без COUNT по миллионам.
        var rows = await query
            .Skip((PageNo - 1) * PageSize)
            .Take(PageSize + 1)
            .ToListAsync(ct);

        HasNext = rows.Count > PageSize;
        Rows = HasNext ? rows.Take(PageSize).ToList() : rows;
    }

    /// <summary>Счёт детей по требованию (кнопка Count), учитывает фильтры Q/EventId/Age.</summary>
    public async Task<IActionResult> OnGetCountAsync(CancellationToken ct)
    {
        var count = await _svc.CountAsync(Filter(), ct);
        return new JsonResult(count);
    }

    private ChildFilter Filter() => new() { Q = Q, EventId = EventId, Age = Age };

    public string NextDir(string column)
        => (string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase))
            ? "desc" : "asc";

    public string SortIndicator(string column)
    {
        if (string.IsNullOrEmpty(Sort) && string.Equals(column, "child", StringComparison.OrdinalIgnoreCase))
            return " ▲";
        if (!string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)) return "";
        return string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? " ▼" : " ▲";
    }
}
