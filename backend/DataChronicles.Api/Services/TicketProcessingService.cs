using System.Text.RegularExpressions;
using DataChronicles.Api.Hubs;
using DataChronicles.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

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
    private readonly AzureAiEmbeddingService? _embed;

    // _embed is optional: when Azure AI embeddings are configured, duplicate detection and
    // grouping are semantic; otherwise a deterministic JobName+Category key is used.
    public TicketProcessingService(
        ZeroShotClassifierService classifier,
        DataChroniclesDbContext db,
        IHubContext<ProgressHub> hub,
        ILogger<TicketProcessingService> log,
        AzureAiEmbeddingService? embed = null)
    {
        _classifier = classifier;
        _db = db;
        _hub = hub;
        _log = log;
        _embed = embed;
    }

    public async Task<CategorizationResult> ProcessAsync(List<InputTicket> input, string? connectionId = null)
    {
        var batchId = Guid.NewGuid().ToString("N")[..8];
        var results = new List<OutputTicket>();
        var total = input.Count;
        var done = 0;

        var cleaned = input.Select(t => CleanDescription(t.Description)).ToList();

        // Existing tickets from prior batches — captured BEFORE adding new rows — so duplicate
        // detection can compare new tickets against history (Requirement: new vs existing).
        var history = await _db.Tickets.ToListAsync();

        // Semantic embeddings when Azure AI is configured; null => deterministic fallback.
        float[][]? embeddings = _embed != null ? await _embed.EmbedAsync(cleaned) : null;

        for (var i = 0; i < input.Count; i++)
        {
            var t = input[i];
            var (category, confidence, source) = await _classifier.ClassifyAsync(cleaned[i]);

            var row = new OutputTicket
            {
                ApplicationName = t.ApplicationName,
                Incident = t.Incident,
                JobName = t.JobName,
                Category = category,
                Confidence = confidence,
                Severity = TextAnalysisService.Severity(t.Description),
                Sentiment = TextAnalysisService.Sentiment(t.Description),
                Source = source,
                Embedding = embeddings != null ? JsonConvert.SerializeObject(embeddings[i]) : null,
                BatchId = batchId,
                CreatedOn = DateTime.UtcNow
            };

            results.Add(row);
            _db.Tickets.Add(row);

            done++;
            await ReportProgress(connectionId, done * 100 / total);
        }

        // Flag duplicates (vs earlier-in-batch + history) and cluster similar issues.
        List<IssueGroup> groups;
        if (embeddings != null)
            groups = ApplySemanticDuplicatesAndGroups(results, embeddings, history, _embed!.SimilarityThreshold);
        else
            groups = ApplyDeterministicDuplicatesAndGroups(results, history);

        await _db.SaveChangesAsync();
        await ReportProgress(connectionId, 100);

        _log.LogInformation("Categorized {Count} tickets in batch {Batch} ({Dupes} duplicates, {Groups} issue groups)",
            total, batchId, results.Count(r => r.IsDuplicate), groups.Count);

        return new CategorizationResult
        {
            BatchId = batchId,
            TotalRecords = total,
            Tickets = results,
            Summary = BuildSummary(results),
            Source = ResolveBatchSource(results),
            DuplicateCount = results.Count(r => r.IsDuplicate),
            Groups = groups,
            FileName = $"test_categories_{batchId}.xlsx"
        };
    }

    /// <summary>Deterministic JobName+Category key used for duplicate/grouping when embeddings are off.</summary>
    private static string DuplicateKey(OutputTicket t) =>
        (t.JobName ?? string.Empty).Trim().ToLowerInvariant() + "|" + t.Category;

    /// <summary>
    /// Deterministic fallback: a ticket duplicates an earlier-in-batch or historical ticket with the
    /// same JobName+Category; groups are same-key clusters with 2+ members. Public for unit testing.
    /// </summary>
    public static List<IssueGroup> ApplyDeterministicDuplicatesAndGroups(List<OutputTicket> rows, List<OutputTicket> history)
    {
        var seen = new Dictionary<string, string>(); // key -> first incident
        foreach (var h in history)
        {
            var hk = DuplicateKey(h);
            if (!seen.ContainsKey(hk)) seen[hk] = h.Incident;
        }
        foreach (var r in rows)
        {
            var k = DuplicateKey(r);
            if (seen.TryGetValue(k, out var inc))
            {
                r.IsDuplicate = true;
                r.DuplicateOf = inc;
            }
            else
            {
                seen[k] = r.Incident;
            }
        }

        return rows
            .GroupBy(DuplicateKey)
            .Where(g => g.Count() >= 2)
            .Select(g => new IssueGroup
            {
                Signature = $"{g.First().Category} / {g.First().JobName}",
                Category = g.First().Category,
                Count = g.Count(),
                RepresentativeIncident = g.First().Incident
            })
            .OrderByDescending(g => g.Count)
            .ToList();
    }

    /// <summary>
    /// Semantic path: a ticket is a duplicate when its embedding is ≥ threshold cosine-similar to an
    /// earlier-in-batch or historical ticket; groups are greedy cosine clusters with 2+ members.
    /// Public for unit testing with hand-crafted vectors.
    /// </summary>
    public static List<IssueGroup> ApplySemanticDuplicatesAndGroups(
        List<OutputTicket> rows, float[][] vectors, List<OutputTicket> history, double threshold)
    {
        var histVecs = history
            .Select(h => (h, v: ParseEmbedding(h.Embedding)))
            .Where(x => x.v != null)
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            double best = 0;
            string? bestIncident = null;
            for (var j = 0; j < i; j++)
            {
                var sim = AzureAiEmbeddingService.Cosine(vectors[i], vectors[j]);
                if (sim > best) { best = sim; bestIncident = rows[j].Incident; }
            }
            foreach (var (h, v) in histVecs)
            {
                var sim = AzureAiEmbeddingService.Cosine(vectors[i], v);
                if (sim > best) { best = sim; bestIncident = h.Incident; }
            }
            if (best >= threshold && bestIncident != null)
            {
                rows[i].IsDuplicate = true;
                rows[i].DuplicateOf = bestIncident;
            }
        }

        // Greedy clustering of the current batch by cosine similarity.
        var reps = new List<int>();
        var members = new Dictionary<int, List<int>>();
        for (var i = 0; i < rows.Count; i++)
        {
            var assigned = -1;
            foreach (var rep in reps)
            {
                if (AzureAiEmbeddingService.Cosine(vectors[i], vectors[rep]) >= threshold) { assigned = rep; break; }
            }
            if (assigned == -1) { reps.Add(i); members[i] = new List<int> { i }; }
            else members[assigned].Add(i);
        }

        return members
            .Where(kv => kv.Value.Count >= 2)
            .Select(kv =>
            {
                var rep = rows[kv.Key];
                var cat = kv.Value.GroupBy(idx => rows[idx].Category)
                    .OrderByDescending(g => g.Count()).First().Key;
                return new IssueGroup
                {
                    Signature = $"{cat} / {rep.JobName}",
                    Category = cat,
                    Count = kv.Value.Count,
                    RepresentativeIncident = rep.Incident
                };
            })
            .OrderByDescending(g => g.Count)
            .ToList();
    }

    private static float[]? ParseEmbedding(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonConvert.DeserializeObject<float[]>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Batch-level engine: "BART", "Internal", or "Mixed" if both were used.</summary>
    public static string ResolveBatchSource(List<OutputTicket> rows)
    {
        if (rows.Count == 0) return ZeroShotClassifierService.EngineInternal;
        var distinct = rows.Select(r => r.Source).Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : "Mixed";
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
