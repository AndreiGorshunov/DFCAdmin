using DfcEventRegistration.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Audit;

/// <summary>
/// Просмотр журнала аудита (Admin-only — папка /Audit под политикой CanManage).
/// Последние N записей (seek по IX_AuditLog_WhenUtc DESC), опц. фильтр по актёру/действию.
/// </summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public const int Take = 200;

    [BindProperty(SupportsGet = true)] public string? Actor { get; set; }
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }

    public IReadOnlyList<AuditEntry> Entries { get; private set; } = Array.Empty<AuditEntry>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var q = _db.AuditLog.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Actor))
            q = q.Where(a => a.ActorEmail != null && a.ActorEmail.Contains(Actor));
        if (!string.IsNullOrWhiteSpace(Action))
            q = q.Where(a => a.Action.Contains(Action));

        Entries = await q
            .OrderByDescending(a => a.WhenUtc)
            .ThenByDescending(a => a.AuditId)
            .Take(Take)
            .ToListAsync(ct);
    }
}
