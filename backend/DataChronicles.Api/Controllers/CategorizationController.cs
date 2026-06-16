using DataChronicles.Api.Models;
using DataChronicles.Api.Services;
using Microsoft.AspNetCore.Mvc;

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
    private readonly ILogger<CategorizationController> _log;

    public CategorizationController(
        ExcelInputReader reader,
        TicketProcessingService processor,
        ExcelOutputWriter writer,
        BlobStorageService blob,
        GeneratedFileStore store,
        ILogger<CategorizationController> log)
    {
        _reader = reader;
        _processor = processor;
        _writer = writer;
        _blob = blob;
        _store = store;
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
