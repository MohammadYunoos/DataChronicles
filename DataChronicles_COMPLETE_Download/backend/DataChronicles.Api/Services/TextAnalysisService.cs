namespace DataChronicles.Api.Services;

/// <summary>
/// Lightweight heuristics for ticket prioritization (severity) and sentiment,
/// satisfying the "prioritize by severity" and "analyze sentiment" requirements
/// without an external dependency. Swappable for an LLM/Azure AI call later.
/// </summary>
public static class TextAnalysisService
{
    private static readonly string[] HighSeverity =
        { "down", "outage", "critical", "failed", "failure", "abend", "deadlock", "unavailable", "urgent", "stuck", "timeout" };

    private static readonly string[] LowSeverity =
        { "info", "informational", "completed", "success", "scheduled", "waiting", "hold" };

    private static readonly string[] NegativeWords =
        { "fail", "error", "issue", "problem", "down", "unable", "cannot", "missing", "delayed", "contention", "unavailable" };

    public static string Severity(string text)
    {
        var t = text.ToLowerInvariant();
        if (HighSeverity.Any(t.Contains)) return "High";
        if (LowSeverity.Any(t.Contains)) return "Low";
        return "Medium";
    }

    public static string Sentiment(string text)
    {
        var t = text.ToLowerInvariant();
        var negatives = NegativeWords.Count(t.Contains);
        if (negatives >= 2) return "Negative";
        if (negatives == 1) return "Neutral";
        return "Positive";
    }
}
