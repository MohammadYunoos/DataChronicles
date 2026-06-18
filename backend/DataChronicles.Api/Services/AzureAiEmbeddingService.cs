using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataChronicles.Api.Services;

/// <summary>
/// Azure OpenAI embeddings client used for semantic duplicate detection and similar-issue
/// grouping. Enabled only when the <c>AzureAI</c> config (Endpoint, ApiKey) plus an embeddings
/// deployment (<c>AzureAI:EmbeddingDeploymentName</c>) are present; otherwise
/// <see cref="TicketProcessingService"/> falls back to a deterministic key, so the feature
/// works fully offline.
///
/// Mirrors <see cref="AzureAiChatService"/>'s endpoint handling:
///   • Foundry "OpenAI v1" (…/openai/v1) → POST {endpoint}/embeddings  with "model" in the body
///   • Classic Azure OpenAI            → POST {endpoint}/openai/deployments/{deployment}/embeddings?api-version=
/// </summary>
public class AzureAiEmbeddingService
{
    private readonly HttpClient _http;
    private readonly ILogger<AzureAiEmbeddingService> _log;
    private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string? _deployment;
    private readonly string _apiVersion;

    public AzureAiEmbeddingService(HttpClient http, IConfiguration config, ILogger<AzureAiEmbeddingService> log)
    {
        _http = http;
        _log = log;
        _endpoint = config["AzureAI:Endpoint"]?.TrimEnd('/');
        _apiKey = config["AzureAI:ApiKey"];
        _deployment = config["AzureAI:EmbeddingDeploymentName"];
        var ver = config["AzureAI:ApiVersion"];
        _apiVersion = string.IsNullOrWhiteSpace(ver) ? "2024-10-21" : ver;
        SimilarityThreshold = double.TryParse(config["AzureAI:SimilarityThreshold"], out var t) ? t : 0.6;

        if (Enabled)
            _http.DefaultRequestHeaders.Add("api-key", _apiKey);
    }

    /// <summary>Cosine score at/above which two tickets are treated as duplicate/similar.</summary>
    public double SimilarityThreshold { get; }

    /// <summary>True only when Endpoint, ApiKey and an embeddings deployment are all configured.</summary>
    public bool Enabled => IsSet(_endpoint) && IsSet(_apiKey) && IsSet(_deployment);

    private static bool IsSet(string? v) =>
        !string.IsNullOrWhiteSpace(v) && !v.StartsWith("YOUR_", StringComparison.Ordinal);

    /// <summary>
    /// Returns an embedding vector per input text (same order), or <c>null</c> when disabled or
    /// on any failure so the caller can fall back to the deterministic matcher.
    /// </summary>
    public async Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts)
    {
        if (!Enabled || texts.Count == 0)
            return null;

        var isV1 = _endpoint!.Contains("/openai/v1", StringComparison.OrdinalIgnoreCase)
                   || _endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase);

        var url = isV1
            ? $"{_endpoint}/embeddings"
            : $"{_endpoint}/openai/deployments/{_deployment}/embeddings?api-version={_apiVersion}";

        var payload = new
        {
            model = isV1 ? _deployment : null,
            input = texts
        };

        try
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                Encoding.UTF8, "application/json");

            var res = await _http.PostAsync(url, content);
            res.EnsureSuccessStatusCode();

            var raw = await res.Content.ReadAsStringAsync();
            var data = JObject.Parse(raw)["data"] as JArray;
            if (data == null || data.Count == 0)
                return null;

            // Preserve request order via the "index" field when present.
            var vectors = new float[texts.Count][];
            for (var i = 0; i < data.Count; i++)
            {
                var item = data[i];
                var idx = (int?)item["index"] ?? i;
                var arr = item["embedding"] as JArray;
                if (arr == null) return null;
                var vec = new float[arr.Count];
                for (var j = 0; j < arr.Count; j++)
                    vec[j] = (float)arr[j]!;
                if (idx >= 0 && idx < vectors.Length)
                    vectors[idx] = vec;
            }
            return vectors.Any(v => v == null) ? null : vectors;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Azure AI embeddings call failed; falling back to deterministic duplicate/grouping for this batch.");
            return null;
        }
    }

    /// <summary>Cosine similarity of two equal-length vectors (0 when either is missing/empty).</summary>
    public static double Cosine(float[]? a, float[]? b)
    {
        if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
            return 0;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0)
            return 0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
