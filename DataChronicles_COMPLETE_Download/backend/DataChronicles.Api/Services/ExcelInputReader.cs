using ClosedXML.Excel;
using DataChronicles.Api.Models;

namespace DataChronicles.Api.Services;

/// <summary>Thrown when the uploaded file fails the input validation rules.</summary>
public class InputValidationException : Exception
{
    public InputValidationException(string message) : base(message) { }
}

/// <summary>
/// Reads ticket rows from the uploaded Excel file. Detects columns by header name
/// (Description / Application Name / Incident / Job Name) and falls back to position.
/// Enforces Validation-1: file must be a non-empty Excel with at least one column.
/// </summary>
public class ExcelInputReader
{
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xlsm" };

    public List<InputTicket> Read(IFormFile file)
    {
        // Validation-1: must be Excel and not empty.
        if (file == null || file.Length == 0)
            throw new InputValidationException("The uploaded file is empty.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InputValidationException("The input file must be an Excel file (.xlsx).");

        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(file.OpenReadStream());
        }
        catch
        {
            throw new InputValidationException("The file could not be read as a valid Excel workbook.");
        }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault()
                     ?? throw new InputValidationException("The Excel file has no worksheets.");

            var headerRow = ws.FirstRowUsed();
            if (headerRow == null)
                throw new InputValidationException("The Excel file is empty.");

            var headers = headerRow.CellsUsed().ToList();
            if (headers.Count < 1)
                throw new InputValidationException("The Excel file must have at least one column.");

            var map = BuildColumnMap(headers);

            var rows = ws.RowsUsed()
                .Skip(1)
                .Select(r => new InputTicket
                {
                    Description = GetCell(r, map, "description", 1),
                    ApplicationName = GetCell(r, map, "application name", 2),
                    Incident = GetCell(r, map, "incident", 3),
                    JobName = GetCell(r, map, "job name", 4)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Description))
                .ToList();

            if (rows.Count == 0)
                throw new InputValidationException("No ticket rows with a description were found in the file.");

            return rows;
        }
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<IXLCell> headers)
    {
        var map = new Dictionary<string, int>();
        foreach (var cell in headers)
        {
            var name = cell.GetString().Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(name) && !map.ContainsKey(name))
                map[name] = cell.Address.ColumnNumber;
        }
        return map;
    }

    private static string GetCell(IXLRow row, Dictionary<string, int> map, string header, int fallbackColumn)
    {
        var col = map.TryGetValue(header, out var c) ? c : fallbackColumn;
        return row.Cell(col).GetString().Trim();
    }
}
