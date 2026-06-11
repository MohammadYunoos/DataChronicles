using System.Text.RegularExpressions;
using DataChronicles.Api.Hubs;
using DataChronicles.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace DataChronicles.Api.Services;

/// <summary>
/// Orchestrates the categorization pipeline: clean -> classify (zero-shot) ->
/// analyze (severity/sentiment) -> persist -> aggregate summary, while pushing
/// real-time progress to connected clients via SignalR.
/// </summary>
public class TicketProcessingService
{
    private readonly ZeroShotClassifierService _classifier;
    private readonly DataChroniclesDbContext _db;
    private readonly IHubContext<ProgressHub> _hub;
    private readonly ILogger<TicketProcessingService> _log;

    public TicketProcessingService(
        ZeroShotClassifierService classifier,
        DataChroniclesDbContext db,
        IHubContext<ProgressHub> hub,
        ILogger<TicketProcessingService> log)
    {
        _classifier = classifier;
        _db = db;
        _hub = hub;
        _log = log;
    }

    public async Task<CategorizationResult> ProcessAsync(List<InputTicket> input, string? connectionId = null)
    {
        var batchId = Guid.NewGuid().ToString("N")[..8];
        var results = new List<OutputTicket>();
        var total = input.Count;
        var done = 0;

        foreach (var t in input)
        {
            var clean = CleanDescription(t.Description);
            var (category, confidence) = await _classifier.ClassifyAsync(clean);

            var row = new OutputTicket
            {
                ApplicationName = t.ApplicationName,
                Incident = t.Incident,
                JobName = t.JobName,
                Category = category,
                Confidence = confidence,
                Severity = TextAnalysisService.Severity(t.Description),
                Sentiment = TextAnalysisService.Sentiment(t.Description),
                BatchId = batchId,
                CreatedOn = DateTime.UtcNow
            };

            results.Add(row);
            _db.Tickets.Add(row);

            done++;
            await ReportProgress(connectionId, done * 100 / total);
        }

        await _db.SaveChangesAsync();
        await ReportProgress(connectionId, 100);

        _log.LogInformation("Categorized {Count} tickets in batch {Batch}", total, batchId);

        return new CategorizationResult
        {
            BatchId = batchId,
            TotalRecords = total,
            Tickets = results,
            Summary = BuildSummary(results),
            FileName = $"test_categories_{batchId}.xlsx"
        };
    }

    public static List<CategorySummary> BuildSummary(List<OutputTicket> rows)
    {
        var total = rows.Count == 0 ? 1 : rows.Count;
        return rows
            .GroupBy(r => r.Category)
            .Select(g => new CategorySummary
            {
                Category = g.Key,
                Count = g.Count(),
                Percentage = Math.Round(100.0 * g.Count() / total, 1)
            })
            .OrderByDescending(s => s.Count)
            .ToList();
    }

    /// <summary>Removes noise / non-alphanumeric characters as per the ML preprocessing requirement.</summary>
    public static string CleanDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;
        var cleaned = Regex.Replace(description, "[^a-zA-Z0-9 ]", " ");
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        return cleaned.ToLowerInvariant();
    }

    private async Task ReportProgress(string? connectionId, int percent)
    {
        if (!string.IsNullOrEmpty(connectionId))
            await _hub.Clients.Client(connectionId).SendAsync("progress", percent);
        else
            await _hub.Clients.All.SendAsync("progress", percent);
    }
}
