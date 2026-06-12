using System.Net;
using System.Net.Http.Json;
using DataChronicles.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DataChronicles.Tests;

/// <summary>
/// Boots the real app in-memory and exercises the HTTP surface end-to-end.
/// Forces the offline classifier + in-memory DB + no auth for determinism.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HuggingFace:Token"] = "",        // force offline classifier
                ["Auth:Enabled"] = "false",        // no Entra ID in tests
                ["ConnectionStrings:Sql"] = "",     // in-memory EF provider
            });
        });
    }
}

public class IntegrationTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public IntegrationTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await _client.GetAsync("/api/health");
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Sample_endpoint_returns_an_excel_file()
    {
        var res = await _client.GetAsync("/api/categorize/sample");
        res.EnsureSuccessStatusCode();
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Upload_categorizes_then_download_and_chat_work()
    {
        // Build a sample file and upload it.
        var sample = SampleDataService.Generate(15);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(sample);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "test_data.xlsx");

        var upload = await _client.PostAsync("/api/categorize/upload", form);
        upload.EnsureSuccessStatusCode();

        var result = await upload.Content.ReadFromJsonAsync<ResultDto>();
        Assert.NotNull(result);
        Assert.Equal(15, result!.totalRecords);
        Assert.Equal(15, result.tickets.Count);
        Assert.Equal("Internal", result.source);
        Assert.NotEmpty(result.summary);

        // Download the generated workbook.
        var download = await _client.GetAsync($"/api/categorize/download/{result.batchId}");
        download.EnsureSuccessStatusCode();
        Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());

        // Chat grounded in the just-uploaded batch.
        var chat = await _client.PostAsJsonAsync("/api/chat", new { question = "how many tickets", batchId = result.batchId });
        chat.EnsureSuccessStatusCode();
        var chatDto = await chat.Content.ReadFromJsonAsync<ChatDto>();
        Assert.Contains("15", chatDto!.answer);
    }

    [Fact]
    public async Task Download_unknown_batch_returns_404()
    {
        var res = await _client.GetAsync("/api/categorize/download/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Upload_non_excel_returns_400()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "bad.txt");
        var res = await _client.PostAsync("/api/categorize/upload", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private record ResultDto(string batchId, int totalRecords, List<TicketDto> tickets, List<SummaryDto> summary, string source);
    private record TicketDto(string category, string source);
    private record SummaryDto(string category, int count);
    private record ChatDto(string answer);
}
