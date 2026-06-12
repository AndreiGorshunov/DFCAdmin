using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Models;
using DfcEventRegistration.Web.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Pages.Registrants;

public class IndexModel : PageModel
{
    private readonly RegistrantQueryService _svc;
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public IndexModel(RegistrantQueryService svc, AppDbContext db, IConfiguration cfg)
    {
        _svc = svc;
        _db = db;
        _cfg = cfg;
    }

    // --- Фильтры / сортировка ---
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? EventId { get; set; }
    [BindProperty(SupportsGet = true)] public RegistrationStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public ParticipantKind? ParticipantType { get; set; }
    [BindProperty(SupportsGet = true)] public int? SessionId { get; set; }
    [BindProperty(SupportsGet = true)] public int? StartPointId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dir { get; set; }
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;

    // --- Keyset-курсор (дефолтный порядок LastName asc) ---
    [BindProperty(SupportsGet = true)] public string? AfterLastName { get; set; }
    [BindProperty(SupportsGet = true)] public long? AfterId { get; set; }

    // --- Offset (fallback для нестандартной сортировки) ---
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

    // --- Данные для view ---
    public IReadOnlyList<RegistrantRow> Rows { get; private set; } = Array.Empty<RegistrantRow>();
    public IReadOnlyList<EventOption> Events { get; private set; } = Array.Empty<EventOption>();
    public IReadOnlyList<SessionOption> Sessions { get; private set; } = Array.Empty<SessionOption>();
    public IReadOnlyList<StartPointOption> StartPoints { get; private set; } = Array.Empty<StartPointOption>();

    public bool UseKeyset { get; private set; }
    public bool HasNext { get; private set; }
    public bool IsFirstPage => AfterId is null;
    public string? NextLastName { get; private set; }
    public long? NextId { get; private set; }

    public int Total { get; private set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;

    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    /// <summary>
    /// Счёт по требованию (кнопка "Count"), учитывает текущие фильтры (Q/EventId/Status).
    /// Отдельный GET-хендлер -> вызывается AJAX'ом, страница не перезагружается.
    /// COUNT не делается на обычной загрузке намеренно (см. keyset).
    /// </summary>
    public async Task<IActionResult> OnGetCountAsync(CancellationToken ct)
    {
        var count = await _svc.CountAsync(Filter(), ct);
        return new JsonResult(count);
    }

    public async Task<IActionResult> OnGetExportAsync([FromServices] ExcelExportService excel, CancellationToken ct)
    {
        int cap = _cfg.GetValue<int?>("Export:MaxRows") ?? 100_000;

        var data = await _svc.ApplySort(_svc.Query(Filter()), Sort, Dir)
            .Take(cap)
            .ToListAsync(ct);

        var bytes = excel.BuildRegistrants(data);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"registrants_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    /// <summary>
    /// Полная выгрузка ВСЕХ строк (учитывает фильтры) в CSV — потоково, без лимита и без буфера.
    /// Один плоский файл, открывается Excel/Power Query. Рекомендуемый путь для миллионов строк.
    /// </summary>
    public async Task<IActionResult> OnGetExportAllCsvAsync([FromServices] StreamingExportService streamer, CancellationToken ct)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"registrants_all_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv\"";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await streamer.WriteCsvAsync(Filter(), Response.Body, ct);
        return new EmptyResult();
    }

    /// <summary>
    /// Полная выгрузка ВСЕХ строк в один .xlsx с автопереносом на новые листы (~1М/лист,
    /// хард-лимит Excel). Пишется потоково на диск, затем отдаётся файл стримом.
    /// </summary>
    public async Task<IActionResult> OnGetExportAllXlsxAsync([FromServices] StreamingExportService streamer, CancellationToken ct)
    {
        var path = await streamer.WriteXlsxTempAsync(Filter(), ct);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 16, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        return File(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"registrants_all_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    private RegistrantFilter Filter() => new() { Q = Q, EventId = EventId, Status = Status, ParticipantType = ParticipantType, SessionId = SessionId, StartPointId = StartPointId };

    private bool IsDefaultOrder()
    {
        bool desc = string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);
        bool lastNameAsc = string.IsNullOrEmpty(Sort)
            || string.Equals(Sort, "lastname", StringComparison.OrdinalIgnoreCase);
        return lastNameAsc && !desc;
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        PageSize = Math.Clamp(PageSize, 10, 200);

        Events = await _db.Events.AsNoTracking()
            .OrderBy(e => e.StartDate)
            .Select(e => new EventOption(e.Id, e.Name))
            .ToListAsync(ct);

        // Зависимые списки: сессии выбранного события, точки выбранной сессии.
        // Клампим невалидные комбинации (напр. сессия осталась от другого события).
        if (EventId is Guid evId)
        {
            Sessions = await _db.EventSessions.AsNoTracking()
                .Where(s => s.EventId == evId).OrderBy(s => s.Name)
                .Select(s => new SessionOption(s.SessionId, s.Name)).ToListAsync(ct);
        }
        if (SessionId is int sidSel && !Sessions.Any(s => s.Id == sidSel)) SessionId = null;

        if (SessionId is int sid2)
        {
            StartPoints = await _db.EventStartPoints.AsNoTracking()
                .Where(p => p.SessionId == sid2).OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
                .Select(p => new StartPointOption(p.StartPointId, p.Name)).ToListAsync(ct);
        }
        if (StartPointId is int spidSel && !StartPoints.Any(p => p.Id == spidSel)) StartPointId = null;

        var f = Filter();
        var ordered = _svc.ApplySort(_svc.Query(f), Sort, Dir);

        UseKeyset = IsDefaultOrder();

        if (UseKeyset)
        {
            var seeked = _svc.SeekAfter(ordered, AfterLastName, AfterId);
            var rows = await seeked.Take(PageSize + 1).ToListAsync(ct);

            HasNext = rows.Count > PageSize;
            Rows = HasNext ? rows.Take(PageSize).ToList() : rows;

            if (Rows.Count > 0)
            {
                NextLastName = Rows[^1].LastName;
                NextId = Rows[^1].RegistrationId;
            }
        }
        else
        {
            if (PageNo < 1) PageNo = 1;
            Total = await _svc.CountAsync(f, ct);
            Rows = await ordered.Skip((PageNo - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        }
    }

    public string NextDir(string column)
        => (string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase))
            ? "desc" : "asc";

    public string SortIndicator(string column)
    {
        if (string.IsNullOrEmpty(Sort) && string.Equals(column, "lastname", StringComparison.OrdinalIgnoreCase))
            return " ▲";
        if (!string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)) return "";
        return string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? " ▼" : " ▲";
    }
}
