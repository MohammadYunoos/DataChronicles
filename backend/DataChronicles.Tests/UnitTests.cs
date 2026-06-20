using System.Net;
using System.Text;
using DataChronicles.Api.Models;
using DataChronicles.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
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
        // "BART (cached)" folds into BART lineage, so an all-BART(+cached) batch is not "Mixed".
        Assert.Equal("BART", TicketProcessingService.ResolveBatchSource(new()
            { new() { Source = "BART" }, new() { Source = "BART (cached)" } }));
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
        Assert.Equal(3, pkg.Workbook.Worksheets.Count);
        Assert.Equal("Summary", pkg.Workbook.Worksheets[0].Name);
        Assert.True(pkg.Workbook.Worksheets[0].Drawings.Count >= 1); // pie chart present
        Assert.Equal("Categorized Data", pkg.Workbook.Worksheets[1].Name);
        Assert.Equal("Issue Groups", pkg.Workbook.Worksheets[2].Name);
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

/// <summary>Stub transport so the Azure AI client can be tested without a live endpoint.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Chat(string content) =>
        Json(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}");
}

public class AzureAiChatServiceTests
{
    private static AzureAiChatService Service(IDictionary<string, string?> cfg, StubHttpHandler handler) =>
        new(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(cfg).Build(),
            NullLogger<AzureAiChatService>.Instance);

    private static Dictionary<string, string?> V1Config() => new()
    {
        ["AzureAI:Endpoint"] = "https://res.services.ai.azure.com/openai/v1",
        ["AzureAI:ApiKey"] = "secret-key",
        ["AzureAI:DeploymentName"] = "gpt-4o-mini"
    };

    [Fact]
    public void Disabled_when_unconfigured()
    {
        var svc = Service(new Dictionary<string, string?>(), new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.False(svc.Enabled);
    }

    [Fact]
    public async Task Disabled_complete_returns_null()
    {
        var svc = Service(new Dictionary<string, string?>(), new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Null(await svc.CompleteAsync("sys", "q"));
    }

    [Fact]
    public async Task Success_returns_message_content()
    {
        var svc = Service(V1Config(), new StubHttpHandler(_ => StubHttpHandler.Chat("LLM grounded answer")));
        Assert.True(svc.Enabled);
        Assert.Equal("LLM grounded answer", await svc.CompleteAsync("sys", "q"));
    }

    [Fact]
    public async Task Http_error_returns_null()
    {
        var svc = Service(V1Config(), new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Assert.Null(await svc.CompleteAsync("sys", "q"));
    }

    [Fact]
    public async Task V1_endpoint_posts_chat_completions_with_model_and_api_key()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var svc = Service(V1Config(), new StubHttpHandler(req =>
        {
            captured = req;
            body = req.Content!.ReadAsStringAsync().Result;
            return StubHttpHandler.Chat("ok");
        }));

        await svc.CompleteAsync("sys", "q");

        Assert.EndsWith("/openai/v1/chat/completions", captured!.RequestUri!.AbsoluteUri);
        Assert.Contains("\"model\":\"gpt-4o-mini\"", body);
        Assert.True(captured.Headers.Contains("api-key"));
    }

    [Fact]
    public async Task Classic_endpoint_uses_deployments_path_with_api_version()
    {
        HttpRequestMessage? captured = null;
        var cfg = new Dictionary<string, string?>
        {
            ["AzureAI:Endpoint"] = "https://res.openai.azure.com",
            ["AzureAI:ApiKey"] = "secret-key",
            ["AzureAI:DeploymentName"] = "gpt-4o-mini",
            ["AzureAI:ApiVersion"] = "2024-10-21"
        };
        var svc = Service(cfg, new StubHttpHandler(req => { captured = req; return StubHttpHandler.Chat("ok"); }));

        await svc.CompleteAsync("sys", "q");

        Assert.Contains("/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-10-21",
            captured!.RequestUri!.AbsoluteUri);
    }
}

public class ChatServiceWithAiTests
{
    private static DataChroniclesDbContext SeededDb(string batch)
    {
        var opts = new DbContextOptionsBuilder<DataChroniclesDbContext>()
            .UseInMemoryDatabase("chat-ai-" + Guid.NewGuid().ToString("N")).Options;
        var db = new DataChroniclesDbContext(opts);
        db.Tickets.AddRange(
            new OutputTicket { BatchId = batch, Category = "Alert", JobName = "J1", Severity = "High", Sentiment = "Negative", Source = "Internal" },
            new OutputTicket { BatchId = batch, Category = "Data Issue", JobName = "J2", Severity = "Low", Sentiment = "Neutral", Source = "Internal" });
        db.SaveChanges();
        return db;
    }

    private static AzureAiChatService Ai(StubHttpHandler handler) =>
        new(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAI:Endpoint"] = "https://res.services.ai.azure.com/openai/v1",
                ["AzureAI:ApiKey"] = "secret-key",
                ["AzureAI:DeploymentName"] = "gpt-4o-mini"
            }).Build(),
            NullLogger<AzureAiChatService>.Instance);

    [Fact]
    public async Task Uses_llm_answer_when_configured()
    {
        using var db = SeededDb("b1");
        var chat = new ChatService(db, Ai(new StubHttpHandler(_ => StubHttpHandler.Chat("Prioritize the High-severity Alert tickets."))));
        var answer = await chat.AnswerAsync("what should I prioritize", "b1");
        Assert.Contains("Prioritize the High-severity", answer);
    }

    [Fact]
    public async Task Falls_back_to_rules_when_llm_fails()
    {
        using var db = SeededDb("b1");
        var chat = new ChatService(db, Ai(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        // 2 tickets seeded -> rule-based "how many" answer contains the count.
        Assert.Contains("2", await chat.AnswerAsync("how many tickets", "b1"));
    }
}

public class DuplicateAndGroupingTests
{
    private static OutputTicket T(string incident, string job, string category) =>
        new() { Incident = incident, JobName = job, Category = category };

    [Fact]
    public void Deterministic_flags_in_batch_and_cross_batch_duplicates()
    {
        var history = new List<OutputTicket> { T("OLD1", "J1", "Alert") };
        var rows = new List<OutputTicket>
        {
            T("INC1", "J1", "Alert"),        // matches history -> duplicate of OLD1
            T("INC2", "J2", "Data Issue"),   // unique
            T("INC3", "J2", "Data Issue"),   // matches INC2 in-batch -> duplicate of INC2
        };

        var groups = TicketProcessingService.ApplyDeterministicDuplicatesAndGroups(rows, history);

        Assert.True(rows[0].IsDuplicate);
        Assert.Equal("OLD1", rows[0].DuplicateOf);
        Assert.False(rows[1].IsDuplicate);
        Assert.True(rows[2].IsDuplicate);
        Assert.Equal("INC2", rows[2].DuplicateOf);
        Assert.Contains(groups, g => g.Count == 2 && g.Category == "Data Issue");
    }

    [Fact]
    public void Semantic_flags_duplicates_and_clusters_by_cosine()
    {
        var rows = new List<OutputTicket> { T("A", "J1", "Alert"), T("B", "J2", "Alert"), T("C", "J3", "Data Issue") };
        var vectors = new[]
        {
            new[] { 1f, 0f },
            new[] { 1f, 0f }, // identical to row 0 -> duplicate + same cluster
            new[] { 0f, 1f }, // orthogonal -> unique
        };

        var groups = TicketProcessingService.ApplySemanticDuplicatesAndGroups(rows, vectors, new List<OutputTicket>(), 0.85);

        Assert.False(rows[0].IsDuplicate);
        Assert.True(rows[1].IsDuplicate);
        Assert.Equal("A", rows[1].DuplicateOf);
        Assert.False(rows[2].IsDuplicate);
        Assert.Contains(groups, g => g.Count == 2);
    }

    [Fact]
    public void Cosine_is_one_for_parallel_and_zero_for_orthogonal_or_missing()
    {
        Assert.Equal(1.0, AzureAiEmbeddingService.Cosine(new[] { 1f, 1f }, new[] { 2f, 2f }), 3);
        Assert.Equal(0.0, AzureAiEmbeddingService.Cosine(new[] { 1f, 0f }, new[] { 0f, 1f }), 3);
        Assert.Equal(0.0, AzureAiEmbeddingService.Cosine(null, new[] { 1f }), 3);
    }
}

public class ReusableBartMatchTests
{
    private static OutputTicket Hist(string incident, string job, string category, string source, float[]? vec = null) =>
        new()
        {
            Incident = incident, JobName = job, Category = category, Source = source,
            Confidence = 0.91, Embedding = vec == null ? null : JsonConvert.SerializeObject(vec)
        };

    // ---- Semantic (vectors != null) ----

    [Fact]
    public void Semantic_reuses_bart_history_match_above_threshold()
    {
        var history = new List<OutputTicket> { Hist("OLD1", "J1", "Server Connectivity issue", "BART", new[] { 1f, 0f }) };
        var vectors = new[] { new[] { 1f, 0f } }; // current row 0, identical direction
        var match = TicketProcessingService.FindReusableBartMatch(
            0, "J1", new List<OutputTicket>(), vectors, history, 0.85);
        Assert.NotNull(match);
        Assert.Equal("OLD1", match!.Incident);
        Assert.Equal("Server Connectivity issue", match.Category);
    }

    [Fact]
    public void Semantic_ignores_internal_lineage_even_if_identical()
    {
        var history = new List<OutputTicket> { Hist("OLD1", "J1", "Alert", "Internal", new[] { 1f, 0f }) };
        var vectors = new[] { new[] { 1f, 0f } };
        var match = TicketProcessingService.FindReusableBartMatch(
            0, "J1", new List<OutputTicket>(), vectors, history, 0.85);
        Assert.Null(match);
    }

    [Fact]
    public void Semantic_reuses_earlier_in_batch_bart_row()
    {
        var batchSoFar = new List<OutputTicket> { Hist("A", "J1", "Alert", "BART") };
        var vectors = new[] { new[] { 1f, 0f }, new[] { 1f, 0f } }; // row1 matches row0
        var match = TicketProcessingService.FindReusableBartMatch(
            1, "J9", batchSoFar, vectors, new List<OutputTicket>(), 0.85);
        Assert.NotNull(match);
        Assert.Equal("A", match!.Incident);
    }

    [Fact]
    public void Semantic_returns_null_below_threshold()
    {
        var history = new List<OutputTicket> { Hist("OLD1", "J1", "Alert", "BART", new[] { 0f, 1f }) };
        var vectors = new[] { new[] { 1f, 0f } }; // orthogonal -> cosine 0
        var match = TicketProcessingService.FindReusableBartMatch(
            0, "J1", new List<OutputTicket>(), vectors, history, 0.85);
        Assert.Null(match);
    }

    // ---- Deterministic (vectors == null) ----

    [Fact]
    public void Deterministic_reuses_bart_history_by_jobname()
    {
        var history = new List<OutputTicket> { Hist("OLD1", "NightlyBatch", "Data Issue", "BART") };
        var match = TicketProcessingService.FindReusableBartMatch(
            0, "  nightlybatch ", new List<OutputTicket>(), null, history, 0);
        Assert.NotNull(match);
        Assert.Equal("Data Issue", match!.Category);
    }

    [Fact]
    public void Deterministic_ignores_internal_jobname_match()
    {
        var history = new List<OutputTicket> { Hist("OLD1", "J1", "Alert", "Internal") };
        var match = TicketProcessingService.FindReusableBartMatch(
            0, "J1", new List<OutputTicket>(), null, history, 0);
        Assert.Null(match);
    }

    [Fact]
    public void Deterministic_returns_null_when_no_jobname_match()
    {
        var history = new List<OutputTicket> { Hist("OLD1", "J1", "Alert", "BART") };
        var match = TicketProcessingService.FindReusableBartMatch(
            0, "J2", new List<OutputTicket>(), null, history, 0);
        Assert.Null(match);
    }
}

public class AzureAiEmbeddingServiceTests
{
    private static Dictionary<string, string?> Cfg() => new()
    {
        ["AzureAI:Endpoint"] = "https://res.services.ai.azure.com/openai/v1",
        ["AzureAI:ApiKey"] = "secret-key",
        ["AzureAI:EmbeddingDeploymentName"] = "text-embedding-3-small"
    };

    private static AzureAiEmbeddingService Service(IDictionary<string, string?> cfg, StubHttpHandler handler) =>
        new(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(cfg).Build(),
            NullLogger<AzureAiEmbeddingService>.Instance);

    [Fact]
    public void Disabled_without_embedding_deployment()
    {
        var cfg = Cfg(); cfg.Remove("AzureAI:EmbeddingDeploymentName");
        var svc = Service(cfg, new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.False(svc.Enabled);
        Assert.Equal(0.6, svc.SimilarityThreshold, 3);
    }

    [Fact]
    public async Task Disabled_embed_returns_null()
    {
        var svc = Service(new Dictionary<string, string?>(), new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Null(await svc.EmbedAsync(new[] { "x" }));
    }

    [Fact]
    public async Task Parses_embedding_vectors_in_order()
    {
        var json = "{\"data\":[{\"index\":0,\"embedding\":[0.1,0.2]},{\"index\":1,\"embedding\":[0.3,0.4]}]}";
        var svc = Service(Cfg(), new StubHttpHandler(_ => StubHttpHandler.Json(HttpStatusCode.OK, json)));
        var vecs = await svc.EmbedAsync(new[] { "a", "b" });
        Assert.NotNull(vecs);
        Assert.Equal(2, vecs!.Length);
        Assert.True(Math.Abs(vecs[1][0] - 0.3f) < 0.001);
    }

    [Fact]
    public async Task Http_error_returns_null()
    {
        var svc = Service(Cfg(), new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Assert.Null(await svc.EmbedAsync(new[] { "a" }));
    }
}

public class EmailServiceTests
{
    private static EmailService Service(IDictionary<string, string?> cfg) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(cfg).Build(),
            NullLogger<EmailService>.Instance);

    private static Dictionary<string, string?> SmtpConfig() => new()
    {
        ["Email:Enabled"] = "true",
        ["Email:SmtpServer"] = "smtp.gmail.com",
        ["Email:Port"] = "587",
        ["Email:From"] = "sender@example.com"
    };

    [Fact]
    public void Disabled_when_unconfigured()
    {
        var svc = Service(new Dictionary<string, string?>());
        Assert.False(svc.Enabled);
    }

    [Fact]
    public void Disabled_when_flag_off_even_if_server_set()
    {
        var cfg = SmtpConfig();
        cfg["Email:Enabled"] = "false";
        Assert.False(Service(cfg).Enabled);
    }

    [Fact]
    public void Disabled_with_placeholder_values()
    {
        var cfg = SmtpConfig();
        cfg["Email:SmtpServer"] = "YOUR_SMTP_SERVER";
        Assert.False(Service(cfg).Enabled);
    }

    [Fact]
    public void Enabled_when_server_and_from_set()
        => Assert.True(Service(SmtpConfig()).Enabled);

    [Fact]
    public async Task SendAsync_returns_failure_when_disabled()
    {
        var (ok, message) = await Service(new Dictionary<string, string?>())
            .SendAsync("to@example.com", "subj", "<p>body</p>", null, null);
        Assert.False(ok);
        Assert.Contains("not configured", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummaryHtml_includes_totals_categories_and_low_confidence_count()
    {
        var tickets = new List<OutputTicket>
        {
            new() { Category = "Alert", Confidence = 0.95, IsDuplicate = false },
            new() { Category = "Alert", Confidence = 0.40, IsDuplicate = true },  // low confidence + duplicate
            new() { Category = "Data Issue", Confidence = 0.30 },                 // low confidence
        };

        var html = EmailService.BuildSummaryHtml("batch42", tickets);

        Assert.Contains("batch42", html);
        Assert.Contains("Total tickets:</strong> 3", html);
        Assert.Contains("Duplicates flagged:</strong> 1", html);
        Assert.Contains("review recommended", html);
        Assert.Contains("Alert", html);
        Assert.Contains("Data Issue", html);
        // Two tickets below the 0.6 low-confidence threshold.
        Assert.Matches(@"review recommended\):</strong> 2", html);
    }
}
