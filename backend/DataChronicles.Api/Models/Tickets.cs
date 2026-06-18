namespace DataChronicles.Api.Models;

/// <summary>A single ticket row read from the uploaded Excel file.</summary>
public class InputTicket
{
    public string Description { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string Incident { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
}

/// <summary>A categorized ticket persisted to the DB and written to the output Excel.</summary>
public class OutputTicket
{
    public int Id { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string Incident { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Severity { get; set; } = "Medium";
    public string Sentiment { get; set; } = "Neutral";
    /// <summary>Which engine produced the category: "BART" (Hugging Face) or "Internal".</summary>
    public string Source { get; set; } = "Internal";
    /// <summary>True when this ticket matches an earlier one (in this batch or a prior persisted batch).</summary>
    public bool IsDuplicate { get; set; }
    /// <summary>Incident of the existing ticket this one duplicates (null when not a duplicate).</summary>
    public string? DuplicateOf { get; set; }
    /// <summary>JSON-serialized embedding vector, persisted so historical comparison needn't re-embed.</summary>
    public string? Embedding { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}

/// <summary>Per-category aggregate used by the Summary sheet and the UI pie chart.</summary>
public class CategorySummary
{
    public string Category { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

/// <summary>A cluster of similar/recurring issues, to focus teams on fundamental problems.</summary>
public class IssueGroup
{
    public string Signature { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Count { get; set; }
    public string RepresentativeIncident { get; set; } = string.Empty;
}

/// <summary>Full result of a categorization run, returned to the UI as JSON.</summary>
public class CategorizationResult
{
    public string BatchId { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public List<OutputTicket> Tickets { get; set; } = new();
    public List<CategorySummary> Summary { get; set; } = new();
    /// <summary>Engine used for the batch: "BART", "Internal", or "Mixed".</summary>
    public string Source { get; set; } = "Internal";
    /// <summary>Number of tickets in this batch flagged as duplicates of an existing ticket.</summary>
    public int DuplicateCount { get; set; }
    /// <summary>Clusters of similar issues (largest first) to highlight recurring/fundamental problems.</summary>
    public List<IssueGroup> Groups { get; set; } = new();
    public string FileName { get; set; } = "categorized_output.xlsx";
}
