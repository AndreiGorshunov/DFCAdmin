using System.Globalization;
using System.Text;
using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;

namespace DfcEventRegistration.Web.Services;

/// <summary>
/// Потоковая выгрузка ВСЕХ регистраций (миллионы строк) без буферизации в память.
///  * Читаем БД через AsAsyncEnumerable() — серверный курсор, строки по одной.
///  * CSV пишем прямо в Response.Body (нулевая память, без лимита строк).
///  * XLSX пишем на диск через OpenXmlWriter с переносом на новый лист каждые
///    ~1М строк (хард-лимит Excel — 1 048 576 строк на лист), потом отдаём файл стримом.
///  * CommandTimeout = 0 на время выгрузки (см. appsettings Export:CommandTimeoutSeconds).
/// </summary>
public class StreamingExportService
{
    // Жёсткий лимит Excel — 1 048 576 строк/лист (с заголовком). Берём с запасом.
    private const int MaxRowsPerSheet = 1_000_000;

    public static readonly string[] Headers =
    {
        "Email", "First Name", "Last Name", "Mobile", "Group code",
        "Event", "Checked-in", "Kids below 13", "Kids above 13"
    };

    private readonly RegistrantQueryService _svc;
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public StreamingExportService(RegistrantQueryService svc, AppDbContext db, IConfiguration cfg)
    {
        _svc = svc;
        _db = db;
        _cfg = cfg;
    }

    private IAsyncEnumerable<RegistrantRow> Rows(RegistrantFilter f)
    {
        // 0 = без лимита (осознанно для админ-выгрузки).
        _db.Database.SetCommandTimeout(_cfg.GetValue<int?>("Export:CommandTimeoutSeconds") ?? 0);

        // Порядок по кластерному RegistrationId -> без тяжёлой сортировки на 10М строк.
        return _svc.Query(f).OrderBy(r => r.RegistrationId).AsAsyncEnumerable();
    }

    // ------------------------------- CSV -------------------------------

    public async Task WriteCsvAsync(RegistrantFilter f, Stream output, CancellationToken ct)
    {
        // UTF-8 с BOM -> Excel корректно распознаёт кодировку (важно для арабских имён).
        await using var w = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1 << 16, leaveOpen: true);

        await w.WriteLineAsync(string.Join(',', Headers.Select(Csv)));

        await foreach (var r in Rows(f).WithCancellation(ct))
        {
            await w.WriteLineAsync(string.Join(',', new[]
            {
                Csv(r.Email),
                Csv(r.FirstName),
                Csv(r.LastName),
                Csv(r.Mobile ?? ""),
                Csv(r.GroupCode ?? ""),
                Csv(r.EventName),
                r.CheckedIn ? "Yes" : "No",
                r.KidsBelow13.ToString(CultureInfo.InvariantCulture),
                r.KidsAbove13.ToString(CultureInfo.InvariantCulture),
            }));
        }
        await w.FlushAsync();
    }

    private static string Csv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool needsQuotes = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuotes) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    // ------------------------------- XLSX (multi-sheet, streaming) -------------------------------

    public async Task<string> WriteXlsxTempAsync(RegistrantFilter f, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"registrants_{Guid.NewGuid():N}.xlsx");

        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wbPart = doc.AddWorkbookPart();
        var sheets = new Sheets();

        uint sheetId = 1;
        int rowsInSheet = 0;
        OpenXmlWriter? ow = null;
        WorksheetPart? wsPart = null;

        void StartSheet()
        {
            wsPart = wbPart.AddNewPart<WorksheetPart>();
            ow = OpenXmlWriter.Create(wsPart);
            ow.WriteStartElement(new Worksheet());
            ow.WriteStartElement(new SheetData());
            WriteHeaderRow(ow);
            rowsInSheet = 0;
        }

        void EndSheet()
        {
            ow!.WriteEndElement(); // SheetData
            ow.WriteEndElement();  // Worksheet
            ow.Close();
            ow.Dispose();
            sheets.Append(new Sheet
            {
                Name = $"Registrants {sheetId}",
                SheetId = sheetId,
                Id = wbPart.GetIdOfPart(wsPart!)
            });
            sheetId++;
        }

        StartSheet();
        await foreach (var r in Rows(f).WithCancellation(ct))
        {
            if (rowsInSheet >= MaxRowsPerSheet)
            {
                EndSheet();
                StartSheet();
            }
            WriteDataRow(ow!, r);
            rowsInSheet++;
        }
        EndSheet();

        wbPart.Workbook = new Workbook(sheets);
        wbPart.Workbook.Save();

        return path;
    }

    private static void WriteHeaderRow(OpenXmlWriter ow)
    {
        ow.WriteStartElement(new Row());
        foreach (var h in Headers) WriteInlineString(ow, h);
        ow.WriteEndElement();
    }

    private static void WriteDataRow(OpenXmlWriter ow, RegistrantRow r)
    {
        ow.WriteStartElement(new Row());
        WriteInlineString(ow, r.Email);
        WriteInlineString(ow, r.FirstName);
        WriteInlineString(ow, r.LastName);
        WriteInlineString(ow, r.Mobile ?? "");
        WriteInlineString(ow, r.GroupCode ?? "");
        WriteInlineString(ow, r.EventName);
        WriteInlineString(ow, r.CheckedIn ? "Yes" : "No");
        WriteNumber(ow, r.KidsBelow13);
        WriteNumber(ow, r.KidsAbove13);
        ow.WriteEndElement();
    }

    private static void WriteInlineString(OpenXmlWriter ow, string value)
        => ow.WriteElement(new Cell
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(value ?? "")) // SDK сам экранирует XML
        });

    private static void WriteNumber(OpenXmlWriter ow, int n)
        => ow.WriteElement(new Cell
        {
            DataType = CellValues.Number,
            CellValue = new CellValue(n.ToString(CultureInfo.InvariantCulture))
        });
}
