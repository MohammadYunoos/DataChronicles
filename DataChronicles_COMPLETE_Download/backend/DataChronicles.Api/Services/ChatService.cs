using DataChronicles.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DataChronicles.Api.Services;

/// <summary>
/// AI assistant for the "Ask Our AI Assistant" panel. Answers questions grounded in
/// the categorized tickets stored in the DB (counts, categories, severity, duplicates).
/// This is intentionally rule-based + data-grounded so it works offline; the same
/// surface can be pointed at Azure AI Chat by injecting an LLM client.
/// </summary>
public class ChatService
{
    private readonly DataChroniclesDbContext _db;

    public ChatService(DataChroniclesDbContext db) => _db = db;

    public async Task<string> AnswerAsync(string question, string? batchId = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            return "Please ask a question about your categorized tickets.";

        var query = _db.Tickets.AsQueryable();
        if (!string.IsNullOrWhiteSpace(batchId))
            query = query.Where(t => t.BatchId == batchId);

        var tickets = await query.ToListAsync();
        if (tickets.Count == 0)
            return "I don't have any categorized tickets yet. Please upload and categorize a file first.";

        var q = question.ToLowerInvariant();
        var summary = TicketProcessingService.BuildSummary(tickets);

        // How many tickets were categorized?
        if (q.Contains("how many") || q.Contains("total") || q.Contains("count"))
        {
            // Count for a specific category mentioned in the question?
            var matched = summary.FirstOrDefault(s => q.Contains(s.Category.ToLowerInvariant())
                || q.Contains(s.Category.Split('_').Last().ToLowerInvariant()));
            if (matched != null)
                return $"There are {matched.Count} tickets categorized as '{matched.Category}' " +
                       $"({matched.Percentage}% of the batch).";

            return $"A total of {tickets.Count} tickets were categorized across {summary.Count} categories.";
        }

        // Most common / top category
        if (q.Contains("most") || q.Contains("top") || q.Contains("common") || q.Contains("frequent"))
        {
            var top = summary.First();
            return $"The most common category is '{top.Category}' with {top.Count} tickets ({top.Percentage}%).";
        }

        // Severity / priority
        if (q.Contains("severity") || q.Contains("priorit") || q.Contains("critical") || q.Contains("high"))
        {
            var high = tickets.Count(t => t.Severity == "High");
            var med = tickets.Count(t => t.Severity == "Medium");
            var low = tickets.Count(t => t.Severity == "Low");
            return $"Severity breakdown — High: {high}, Medium: {med}, Low: {low}. " +
                   (high > 0 ? $"{high} ticket(s) are High severity and may need urgent attention." : "No High severity tickets in this batch.");
        }

        // Sentiment
        if (q.Contains("sentiment"))
        {
            var neg = tickets.Count(t => t.Sentiment == "Negative");
            return $"{neg} of {tickets.Count} tickets carry a negative sentiment.";
        }

        // Categories overview / breakdown / summary
        if (q.Contains("categor") || q.Contains("breakdown") || q.Contains("summary") || q.Contains("distribution"))
        {
            var lines = summary.Select(s => $"• {s.Category}: {s.Count} ({s.Percentage}%)");
            return $"Here is the category breakdown for {tickets.Count} tickets:\n" + string.Join("\n", lines);
        }

        // Duplicate detection (same job + category)
        if (q.Contains("duplicate") || q.Contains("repeat") || q.Contains("recurr"))
        {
            var dupes = tickets
                .GroupBy(t => new { t.JobName, t.Category })
                .Where(g => g.Count() > 1)
                .Select(g => $"• {g.Key.JobName} / {g.Key.Category}: {g.Count()} occurrences")
                .ToList();
            return dupes.Count == 0
                ? "No recurring (duplicate) job+category combinations were detected."
                : "Potential recurring issues:\n" + string.Join("\n", dupes);
        }

        // Default: summary
        var overview = summary.Select(s => $"{s.Category} ({s.Count})");
        return $"I analyzed {tickets.Count} categorized tickets. Categories present: {string.Join(", ", overview)}. " +
               "You can ask about totals, the most common category, severity, sentiment, or duplicates.";
    }
}
