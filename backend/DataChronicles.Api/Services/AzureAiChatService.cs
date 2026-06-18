using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataChronicles.Api.Services;

/// <summary>
/// Thin client for Azure AI Chat (Azure OpenAI chat-completions). Enabled only when the
/// <c>AzureAI</c> configuration is filled in; otherwise <see cref="ChatService"/> keeps using
/// its deterministic, data-grounded rule-based answers so the app works fully offline.
///
/// Supports both endpoint shapes:
///   • Azure AI Foundry "OpenAI v1" — https://&lt;res&gt;.services.ai.azure.com/openai/v1
///     → POST {endpoint}/chat/completions with the deployment name sent as "model".
///   • Classic Azure OpenAI — https://&lt;res&gt;.openai.azure.com
///     → POST {endpoint}/openai/deployments/{deployment}/chat/completions?api-version=...
/// Both authenticate with the "api-key" header.
/// </summary>
public class AzureAiChatService
{
    private readonly HttpClient _http;
    private readonly ILogger<AzureAiChatService> _log;
    private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string? _deployment;
    private readonly string _apiVersion;

    public AzureAiChatService(HttpClient http, IConfiguration config, ILogger<AzureAiChatService> log)
    {
        _http = http;
        _log = log;
        _endpoint = config["AzureAI:Endpoint"]?.TrimEnd('/');
        _apiKey = config["AzureAI:ApiKey"];
        _deployment = config["AzureAI:DeploymentName"];
        var ver = config["AzureAI:ApiVersion"];
        _apiVersion = string.IsNullOrWhiteSpace(ver) ? "2024-10-21" : ver;

        if (Enabled)
            _http.DefaultRequestHeaders.Add("api-key", _apiKey);
    }

    /// <summary>True only when Endpoint, ApiKey and DeploymentName are all configured (not placeholders).</summary>
    public bool Enabled =>
        IsSet(_endpoint) && IsSet(_apiKey) && IsSet(_deployment);

    private static bool IsSet(string? v) =>
        !string.IsNullOrWhiteSpace(v) && !v.StartsWith("YOUR_", StringComparison.Ordinal);

    /// <summary>
    /// Sends a system + user message to the chat model. Returns the assistant text, or
    /// <c>null</c> when disabled or on any failure (so the caller can fall back gracefully).
    /// </summary>
    public async Task<string?> CompleteAsync(string systemPrompt, string userMessage)
    {
        if (!Enabled)
            return null;

        // Foundry "OpenAI v1" endpoints carry the version in the path and take "model" in the body;
        // classic Azure OpenAI endpoints put the deployment in the path + an api-version query.
        var isV1 = _endpoint!.Contains("/openai/v1", StringComparison.OrdinalIgnoreCase)
                   || _endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase);

        var url = isV1
            ? $"{_endpoint}/chat/completions"
            : $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version={_apiVersion}";

        var payload = new
        {
            model = isV1 ? _deployment : null,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.2,
            max_tokens = 500
        };

        try
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                Encoding.UTF8, "application/json");

            var res = await _http.PostAsync(url, content);
            res.EnsureSuccessStatusCode();

            var raw = await res.Content.ReadAsStringAsync();
            var answer = JObject.Parse(raw)["choices"]?[0]?["message"]?["content"]?.ToString();
            return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Azure AI Chat call failed; falling back to the rule-based assistant for this question.");
            return null;
        }
    }
}
