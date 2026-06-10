using DfcEventRegistration.Web.Models;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DfcEventRegistration.Web.Pages.Reports;

public class TshirtsModel : PageModel
{
    private readonly TshirtReportService _svc;

    public TshirtsModel(TshirtReportService svc) => _svc = svc;

    [BindProperty(SupportsGet = true)] public Guid? EventId { get; set; }

    public IReadOnlyList<EventOption> Events { get; private set; } = Array.Empty<EventOption>();
    public IReadOnlyList<TshirtReportRow> Rows { get; private set; } = Array.Empty<TshirtReportRow>();

    public int TotalRequested => Rows.Sum(r => r.Requested);
    public int TotalCollected => Rows.Sum(r => r.Collected);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Events = await _svc.EventsAsync(ct);
        Rows = await _svc.BuildAsync(EventId, ct);
    }

    public async Task<IActionResult> OnGetExportAsync([FromServices] ExcelExportService excel, CancellationToken ct)
    {
        var rows = await _svc.BuildAsync(EventId, ct);
        var bytes = excel.BuildTshirtReport(rows);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"tshirt_report_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }
}
