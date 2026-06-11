using ClosedXML.Excel;
using DataChronicles.Api.Models;

namespace DataChronicles.Api.Services;

/// <summary>
/// Generates the downloadable categorized Excel file. The Summary sheet is the first
/// (default) view when the workbook opens, followed by the full categorized data.
/// </summary>
public class ExcelOutputWriter
{
    public byte[] Generate(CategorizationResult result)
    {
        using var wb = new XLWorkbook();

        BuildSummarySheet(wb, result);
        BuildDataSheet(wb, result.Tickets);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildSummarySheet(XLWorkbook wb, CategorizationResult result)
    {
        var ws = wb.AddWorksheet("Summary");

        ws.Cell(1, 1).Value = "Data Chronicles — Categorization Summary";
        ws.Range(1, 1, 1, 3).Merge().Style.Font.SetBold().Font.FontSize = 14;

        ws.Cell(2, 1).Value = "Batch Id";
        ws.Cell(2, 2).Value = result.BatchId;
        ws.Cell(3, 1).Value = "Total Records";
        ws.Cell(3, 2).Value = result.TotalRecords;

        var headerRow = 5;
        ws.Cell(headerRow, 1).Value = "Category";
        ws.Cell(headerRow, 2).Value = "Count";
        ws.Cell(headerRow, 3).Value = "Percentage";
        ws.Range(headerRow, 1, headerRow, 3).Style.Font.SetBold();
        ws.Range(headerRow, 1, headerRow, 3).Style.Fill.BackgroundColor = XLColor.LightBlue;

        var r = headerRow + 1;
        foreach (var s in result.Summary)
        {
            ws.Cell(r, 1).Value = s.Category;
            ws.Cell(r, 2).Value = s.Count;
            ws.Cell(r, 3).Value = $"{s.Percentage}%";
            r++;
        }

        ws.Columns().AdjustToContents();
        ws.SetTabActive(); // Summary is the first view on open.
    }

    private static void BuildDataSheet(XLWorkbook wb, List<OutputTicket> rows)
    {
        var ws = wb.AddWorksheet("Categorized Data");

        var view = rows.Select(t => new
        {
            t.ApplicationName,
            t.Incident,
            t.JobName,
            t.Category,
            t.Confidence,
            t.Severity,
            t.Sentiment
        }).ToList();

        ws.Cell(1, 1).InsertTable(view, "Tickets");
        ws.Columns().AdjustToContents();
    }
}
