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

/// <summary>Full result of a categorization run, returned to the UI as JSON.</summary>
public class CategorizationResult
{
    public string BatchId { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public List<OutputTicket> Tickets { get; set; } = new();
    public List<CategorySummary> Summary { get; set; } = new();
    public string FileName { get; set; } = "categorized_output.xlsx";
}
