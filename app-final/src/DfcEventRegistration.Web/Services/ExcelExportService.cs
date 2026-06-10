using ClosedXML.Excel;
using DfcEventRegistration.Web.Models;

namespace DfcEventRegistration.Web.Services;

public class ExcelExportService
{
    public byte[] BuildRegistrants(IReadOnlyList<RegistrantRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Registrants");

        string[] headers =
        {
            "Email", "First Name", "Last Name", "Mobile number", "Group code",
            "Event", "Bib collected / Checked-in", "Slot/Route/Session",
            "Total kids below 13", "Total kids above 13"
        };

        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var x in rows)
        {
            ws.Cell(row, 1).Value = x.Email;
            ws.Cell(row, 2).Value = x.FirstName;
            ws.Cell(row, 3).Value = x.LastName;
            ws.Cell(row, 4).Value = x.Mobile ?? "";
            ws.Cell(row, 5).Value = x.GroupCode ?? "";
            ws.Cell(row, 6).Value = x.EventName;
            ws.Cell(row, 7).Value = x.CheckedIn ? "Yes" : "No";
            ws.Cell(row, 8).Value = "";              // Slot/Route/Session — нет в схеме
            ws.Cell(row, 9).Value = x.KidsBelow13;
            ws.Cell(row, 10).Value = x.KidsAbove13;
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    public byte[] BuildTshirtReport(IReadOnlyList<TshirtReportRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("T-shirts");

        string[] headers = { "Size", "Requested", "Collected", "Outstanding", "Stock", "Stock after collection" };
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Size;
            ws.Cell(row, 2).Value = r.Requested;
            ws.Cell(row, 3).Value = r.Collected;
            ws.Cell(row, 4).Value = r.OutstandingToCollect;
            ws.Cell(row, 5).Value = r.Stock.HasValue ? (XLCellValue)r.Stock.Value : Blank.Value;
            ws.Cell(row, 6).Value = r.StockAfterCollection.HasValue ? (XLCellValue)r.StockAfterCollection.Value : Blank.Value;
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
