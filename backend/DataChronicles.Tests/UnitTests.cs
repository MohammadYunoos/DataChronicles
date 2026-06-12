using DataChronicles.Api.Models;
using DataChronicles.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using Xunit;

namespace DataChronicles.Tests;

public class TextAnalysisServiceTests
{
    [Theory]
    [InlineData("PROD JOB DOWN: timeout", "High")]
    [InlineData("Critical failure abend", "High")]
    [InlineData("Job completed, waiting for next cycle", "Low")]
    [InlineData("Daily reconciliation processed", "Medium")]
    public void Severity_classifies_by_keywords(string text, string expected)
        => Assert.Equal(expected, TextAnalysisService.Severity(text));

    [Theory]
    [InlineData("fail error problem", "Negative")]
    [InlineData("data issue found", "Neutral")]
    [InlineData("all good here", "Positive")]
    public void Sentiment_classifies_by_negativity(string text, string expected)
        => Assert.Equal(expected, TextAnalysisService.Sentiment(text));
}

public class ZeroShotClassifierServiceTests
{
    private static ZeroShotClassifierService OfflineService()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()) // no token -> offline
            .Build();
        return new ZeroShotClassifierService(new HttpClient(), cfg, NullLogger<ZeroShotClassifierService>.Instance);
    }

    [Fact]
    public async Task Empty_text_returns_default_internal()
    {
        var (cat, conf, src) = await OfflineService().ClassifyAsync("   ");
        Assert.Equal(ZeroShotClassifierService.EngineInternal, src);
        Assert.Equal(0.0, conf);
        Assert.Equal(ZeroShotClassifierService.Labels[0], cat);
    }

    [Fact]
    public async Task Connectivity_keywords_map_to_server_category()
    {
        var (cat, conf, src) = await OfflineService().ClassifyAsync("ftp server connectivity timeout host unreachable");
        Assert.Equal("Server Connectivity issue", cat);
        Assert.Equal(ZeroShotClassifierService.EngineInternal, src);
        Assert.InRange(conf, 0.55, 0.99);
    }

    [Fact]
    public async Task No_keyword_match_falls_back_to_alert_low_confidence()
    {
        var (cat, conf, _) = await OfflineService().ClassifyAsync("zzz qqq xxx");
        Assert.Equal(ZeroShotClassifierService.Labels[0], cat);
        Assert.Equal(0.35, conf);
    }
}

public class TicketProcessingStaticsTests
{
    [Theory]
    [InlineData("Hello, World! 123", "hello world 123")]
    [InlineData("  multiple   spaces  ", "multiple spaces")]
    [InlineData("", "")]
    public void CleanDescription_strips_noise(string input, string expected)
        => Assert.Equal(expected, TicketProcessingService.CleanDescription(input));

    [Fact]
    public void BuildSummary_aggregates_and_orders_by_count()
    {
        var rows = new List<OutputTicket>
        {
            new() { Category = "A" }, new() { Category = "A" }, new() { Category = "A" },
            new() { Category = "B" },
        };
        var summary = TicketProcessingService.BuildSummary(rows);
        Assert.Equal(2, summary.Count);
        Assert.Equal("A", summary[0].Category);
        Assert.Equal(3, summary[0].Count);
        Assert.Equal(75.0, summary[0].Percentage);
    }

    [Fact]
    public void ResolveBatchSource_detects_uniform_and_mixed()
    {
        Assert.Equal("Internal", TicketProcessingService.ResolveBatchSource(new()));
        Assert.Equal("BART", TicketProcessingService.ResolveBatchSource(new()
            { new() { Source = "BART" }, new() { Source = "BART" } }));
        Assert.Equal("Mixed", TicketProcessingService.ResolveBatchSource(new()
            { new() { Source = "BART" }, new() { Source = "Internal" } }));
    }
}

public class ExcelInputReaderTests
{
    private static IFormFile FileFrom(byte[] bytes, string name)
    {
        var ms = new MemoryStream(bytes);
        return new FormFile(ms, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary() };
    }

    [Fact]
    public void Reads_rows_from_a_valid_workbook()
    {
        var bytes = SampleDataService.Generate(7);
        var rows = new ExcelInputReader().Read(FileFrom(bytes, "test_data.xlsx"));
        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
        Assert.All(rows, r => Assert.Equal("Test Application", r.ApplicationName));
    }

    [Fact]
    public void Empty_file_is_rejected()
        => Assert.Throws<InputValidationException>(() =>
            new ExcelInputReader().Read(FileFrom(Array.Empty<byte>(), "x.xlsx")));

    [Fact]
    public void Non_excel_extension_is_rejected()
        => Assert.Throws<InputValidationException>(() =>
            new ExcelInputReader().Read(FileFrom(new byte[] { 1, 2, 3 }, "x.txt")));

    [Fact]
    public void Corrupt_excel_content_is_rejected()
        => Assert.Throws<InputValidationException>(() =>
            new ExcelInputReader().Read(FileFrom(new byte[] { 1, 2, 3, 4 }, "x.xlsx")));
}

public class ExcelOutputWriterTests
{
    [Fact]
    public void Generates_workbook_with_summary_first_and_a_chart()
    {
        var result = new CategorizationResult
        {
            BatchId = "abc123",
            TotalRecords = 3,
            Source = "Internal",
            Tickets = new()
            {
                new() { ApplicationName = "App", Incident = "INC1", JobName = "J1", Category = "Alert", Confidence = 0.9, Severity = "High", Sentiment = "Negative", Source = "Internal" },
                new() { ApplicationName = "App", Incident = "INC2", JobName = "J2", Category = "Alert", Confidence = 0.8, Severity = "Low", Sentiment = "Neutral", Source = "Internal" },
                new() { ApplicationName = "App", Incident = "INC3", JobName = "J3", Category = "Data Issue", Confidence = 0.7, Severity = "Medium", Sentiment = "Neutral", Source = "Internal" },
            }
        };
        result.Summary = TicketProcessingService.BuildSummary(result.Tickets);

        var bytes = new ExcelOutputWriter().Generate(result);
        Assert.NotEmpty(bytes);

        using var pkg = new ExcelPackage(new MemoryStream(bytes));
        Assert.Equal(2, pkg.Workbook.Worksheets.Count);
        Assert.Equal("Summary", pkg.Workbook.Worksheets[0].Name);
        Assert.True(pkg.Workbook.Worksheets[0].Drawings.Count >= 1); // pie chart present
        Assert.Equal("Categorized Data", pkg.Workbook.Worksheets[1].Name);
    }
}

public class SampleDataServiceTests
{
    [Fact]
    public void Generates_a_readable_sample_of_requested_size()
    {
        var bytes = SampleDataService.Generate(12);
        Assert.NotEmpty(bytes);
        var ms = new MemoryStream(bytes);
        var file = new FormFile(ms, 0, bytes.Length, "file", "sample.xlsx") { Headers = new HeaderDictionary() };
        Assert.Equal(12, new ExcelInputReader().Read(file).Count);
    }
}

public class ChatServiceTests
{
    private static DataChroniclesDbContext SeededDb(string batch)
    {
        var opts = new DbContextOptionsBuilder<DataChroniclesDbContext>()
            .UseInMemoryDatabase("chat-" + Guid.NewGuid().ToString("N")).Options;
        var db = new DataChroniclesDbContext(opts);
        db.Tickets.AddRange(
            new OutputTicket { BatchId = batch, Category = "Alert", JobName = "J1", Severity = "High", Sentiment = "Negative", Source = "Internal" },
            new OutputTicket { BatchId = batch, Category = "Alert", JobName = "J1", Severity = "High", Sentiment = "Negative", Source = "Internal" },
            new OutputTicket { BatchId = batch, Category = "Data Issue", JobName = "J2", Severity = "Low", Sentiment = "Neutral", Source = "Internal" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Empty_question_prompts_for_input()
    {
        using var db = SeededDb("b1");
        Assert.Contains("ask a question", await new ChatService(db).AnswerAsync("", "b1"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_data_for_batch_is_reported()
    {
        using var db = SeededDb("b1");
        Assert.Contains("don't have any", await new ChatService(db).AnswerAsync("how many", "missing-batch"));
    }

    [Fact]
    public async Task Answers_totals_top_severity_sentiment_breakdown_and_duplicates()
    {
        using var db = SeededDb("b1");
        var chat = new ChatService(db);

        Assert.Contains("3", await chat.AnswerAsync("how many tickets", "b1"));
        Assert.Contains("Alert", await chat.AnswerAsync("most common category", "b1"));
        Assert.Contains("High", await chat.AnswerAsync("severity breakdown", "b1"));
        Assert.Contains("negative", await chat.AnswerAsync("sentiment", "b1"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alert", await chat.AnswerAsync("category breakdown", "b1"));
        Assert.Contains("J1", await chat.AnswerAsync("any duplicates", "b1")); // J1/Alert appears twice
    }

    [Fact]
    public async Task Default_overview_when_intent_unknown()
    {
        using var db = SeededDb("b1");
        var answer = await new ChatService(db).AnswerAsync("hello there", "b1");
        Assert.Contains("analyzed", answer, StringComparison.OrdinalIgnoreCase);
    }
}
