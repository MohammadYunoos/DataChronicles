using DataChronicles.Api.Models;
using DataChronicles.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DataChronicles.Api.Controllers;

[ApiController]
[Route("api/categorize")]
public class CategorizationController : ControllerBase
{
    private readonly ExcelInputReader _reader;
    private readonly TicketProcessingService _processor;
    private readonly ExcelOutputWriter _writer;
    private readonly BlobStorageService _blob;
    private readonly GeneratedFileStore _store;
    private readonly EmailService _email;
    private readonly DataChroniclesDbContext _db;
    private readonly ILogger<CategorizationController> _log;

    public CategorizationController(
        ExcelInputReader reader,
        TicketProcessingService processor,
        ExcelOutputWriter writer,
        BlobStorageService blob,
        GeneratedFileStore store,
        EmailService email,
        DataChroniclesDbContext db,
        ILogger<CategorizationController> log)
    {
        _reader = reader;
        _processor = processor;
        _writer = writer;
        _blob = blob;
        _store = store;
        _email = email;
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Upload an Excel file, categorize every ticket, persist + archive the output,
    /// and return the full result (records + summary) as JSON for the UI.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] string? connectionId)
    {
        List<InputTicket> input;
        try
        {
            input = _reader.Read(file);
        }
        catch (InputValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var result = await _processor.ProcessAsync(input, connectionId);

        // Validation-2: input record count must equal output record count.
        if (result.TotalRecords != result.Tickets.Count)
            return StatusCode(500, new { error = "Record count mismatch between input and output." });

        var excel = _writer.Generate(result);
        _store.Save(result.BatchId, excel, result.FileName);
        await _blob.UploadAsync(result.FileName, excel);

        return Ok(result);
    }

    /// <summary>Download the generated workbook for a previously processed batch.</summary>
    [HttpGet("download/{batchId}")]
    public IActionResult Download(string batchId)
    {
        var file = _store.Get(batchId);
        if (file == null)
            return NotFound(new { error = "No generated file found for this batch." });

        return File(file.Value.Data,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.Value.FileName);
    }

    public record EmailRequest(string BatchId, string To);

    /// <summary>
    /// Emails the generated workbook for a batch (as an attachment) plus an HTML summary.
    /// Returns { success, message } so the UI can show a friendly result; returns 404 when the
    /// batch is unknown. Low-confidence review is enforced in the UI before this is called.
    /// </summary>
    [HttpPost("email")]
    public async Task<IActionResult> Email([FromBody] EmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.To) || !request.To.Contains('@', StringComparison.Ordinal))
            return BadRequest(new { error = "A valid recipient email address is required." });

        var file = _store.Get(request.BatchId);
        if (file == null)
            return NotFound(new { error = "No generated file found for this batch." });

        var tickets = await _db.Tickets
            .Where(t => t.BatchId == request.BatchId)
            .ToListAsync();

        var html = EmailService.BuildSummaryHtml(request.BatchId, tickets);
        var subject = $"Data Chronicles categorization report — batch {request.BatchId}";

        var (ok, message) = await _email.SendAsync(
            request.To, subject, html, file.Value.Data, file.Value.FileName);

        return Ok(new { success = ok, message });
    }

    /// <summary>Download a ready-made sample input file to try the workflow.</summary>
    [HttpGet("sample")]
    public IActionResult Sample()
    {
        var data = SampleDataService.Generate(50);
        return File(data,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "test_data_50.xlsx");
    }
}
