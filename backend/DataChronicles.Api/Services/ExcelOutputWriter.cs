using DataChronicles.Api.Models;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;

namespace DataChronicles.Api.Services;

/// <summary>
/// Generates the downloadable categorized Excel file using EPPlus.
/// Sheet 1 (Summary) is the first/active view on open and contains a native pie
/// chart of the category distribution; Sheet 2 holds the full categorized data.
/// </summary>
public class ExcelOutputWriter
{
    public byte[] Generate(CategorizationResult result)
    {
        using var pkg = new ExcelPackage();

        BuildSummarySheet(pkg, result);
        BuildDataSheet(pkg, result.Tickets);
        BuildGroupsSheet(pkg, result.Groups);

        // Make the Summary sheet the first view when the workbook opens.
        pkg.Workbook.View.ActiveTab = 0;
        pkg.Workbook.Worksheets[0].View.TabSelected = true;

        return pkg.GetAsByteArray();
    }

    private static void BuildSummarySheet(ExcelPackage pkg, CategorizationResult result)
    {
        var ws = pkg.Workbook.Worksheets.Add("Summary");

        ws.Cells[1, 1].Value = "Data Chronicles — Categorization Summary";
        using (var title = ws.Cells[1, 1, 1, 3])
        {
            title.Merge = true;
            title.Style.Font.Bold = true;
            title.Style.Font.Size = 14;
        }

        ws.Cells[2, 1].Value = "Batch Id";
        ws.Cells[2, 2].Value = result.BatchId;
        ws.Cells[3, 1].Value = "Total Records";
        ws.Cells[3, 2].Value = result.TotalRecords;
        ws.Cells[4, 1].Value = "Categorization Engine";
        ws.Cells[4, 2].Value = result.Source; // BART / Internal / Mixed
        ws.Cells[5, 1].Value = "Duplicates Flagged";
        ws.Cells[5, 2].Value = result.DuplicateCount;

        // Category table
        const int headerRow = 7;
        ws.Cells[headerRow, 1].Value = "Category";
        ws.Cells[headerRow, 2].Value = "Count";
        ws.Cells[headerRow, 3].Value = "Percentage";
        using (var h = ws.Cells[headerRow, 1, headerRow, 3])
        {
            h.Style.Font.Bold = true;
            h.Style.Fill.PatternType = ExcelFillStyle.Solid;
            h.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
        }

        var row = headerRow + 1;
        foreach (var s in result.Summary)
        {
            ws.Cells[row, 1].Value = s.Category;
            ws.Cells[row, 2].Value = s.Count;
            ws.Cells[row, 3].Value = s.Percentage / 100.0;
            ws.Cells[row, 3].Style.Numberformat.Format = "0.0%";
            row++;
        }

        var lastRow = row - 1;

        // Native pie chart bound to the category/count range.
        if (result.Summary.Count > 0)
        {
            var pie = ws.Drawings.AddChart("CategoryPie", eChartType.Pie) as ExcelPieChart;
            pie!.Title.Text = "Tickets by Category";
            pie.Series.Add(
                ws.Cells[headerRow + 1, 2, lastRow, 2],   // values (Count)
                ws.Cells[headerRow + 1, 1, lastRow, 1]);  // categories (Category)
            pie.DataLabel.ShowPercent = true;
            pie.Legend.Position = eLegendPosition.Right;
            pie.SetPosition(1, 0, 4, 10);   // top-right of the sheet
            pie.SetSize(520, 360);
        }

        // Explicit widths (AutoFitColumns uses GDI+ and is not Linux-safe).
        ws.Column(1).Width = 38;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 14;
    }

    private static void BuildDataSheet(ExcelPackage pkg, List<OutputTicket> rows)
    {
        var ws = pkg.Workbook.Worksheets.Add("Categorized Data");

        var headers = new[]
        {
            "Application Name", "Incident", "Job Name", "Category",
            "Confidence", "Severity", "Sentiment", "Source", "Duplicate", "Duplicate Of"
        };
        for (var c = 0; c < headers.Length; c++)
            ws.Cells[1, c + 1].Value = headers[c];
        using (var h = ws.Cells[1, 1, 1, headers.Length])
        {
            h.Style.Font.Bold = true;
            h.Style.Fill.PatternType = ExcelFillStyle.Solid;
            h.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
        }

        var r = 2;
        foreach (var t in rows)
        {
            ws.Cells[r, 1].Value = t.ApplicationName;
            ws.Cells[r, 2].Value = t.Incident;
            ws.Cells[r, 3].Value = t.JobName;
            ws.Cells[r, 4].Value = t.Category;
            ws.Cells[r, 5].Value = t.Confidence;
            ws.Cells[r, 5].Style.Numberformat.Format = "0%";
            ws.Cells[r, 6].Value = t.Severity;
            ws.Cells[r, 7].Value = t.Sentiment;
            ws.Cells[r, 8].Value = t.Source;
            ws.Cells[r, 9].Value = t.IsDuplicate ? "Yes" : "No";
            ws.Cells[r, 10].Value = t.DuplicateOf;
            r++;
        }

        // Explicit widths (AutoFitColumns uses GDI+ and is not Linux-safe).
        var widths = new[] { 22, 14, 16, 32, 12, 12, 12, 12, 11, 16 };
        for (var c = 0; c < widths.Length; c++)
            ws.Column(c + 1).Width = widths[c];
    }

    private static void BuildGroupsSheet(ExcelPackage pkg, List<IssueGroup> groups)
    {
        var ws = pkg.Workbook.Worksheets.Add("Issue Groups");

        ws.Cells[1, 1].Value = "Similar-issue groups (recurring problems to address at the root)";
        using (var title = ws.Cells[1, 1, 1, 4])
        {
            title.Merge = true;
            title.Style.Font.Bold = true;
            title.Style.Font.Size = 12;
        }

        const int headerRow = 3;
        var headers = new[] { "Issue Group", "Category", "Count", "Representative Incident" };
        for (var c = 0; c < headers.Length; c++)
            ws.Cells[headerRow, c + 1].Value = headers[c];
        using (var h = ws.Cells[headerRow, 1, headerRow, headers.Length])
        {
            h.Style.Font.Bold = true;
            h.Style.Fill.PatternType = ExcelFillStyle.Solid;
            h.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
        }

        var r = headerRow + 1;
        foreach (var g in groups)
        {
            ws.Cells[r, 1].Value = g.Signature;
            ws.Cells[r, 2].Value = g.Category;
            ws.Cells[r, 3].Value = g.Count;
            ws.Cells[r, 4].Value = g.RepresentativeIncident;
            r++;
        }
        if (groups.Count == 0)
            ws.Cells[headerRow + 1, 1].Value = "No recurring issue groups detected in this batch.";

        var widths = new[] { 40, 22, 8, 22 };
        for (var c = 0; c < widths.Length; c++)
            ws.Column(c + 1).Width = widths[c];
    }
}
