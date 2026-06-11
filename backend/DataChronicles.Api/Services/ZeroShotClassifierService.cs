using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataChronicles.Api.Services;

/// <summary>
/// Zero-shot ticket classifier.
/// Uses the Facebook BART-large-MNLI model on Hugging Face when a token is configured
/// (HuggingFace:Token). When no token is present it falls back to a deterministic
/// keyword-weighted classifier so the application works fully offline / out-of-the-box.
/// </summary>
public class ZeroShotClassifierService
{
    private readonly HttpClient _http;
    private readonly ILogger<ZeroShotClassifierService> _log;
    private readonly string? _token;
    private readonly string _model;

    // Process-wide circuit breaker: once a live HF call fails (e.g. blocked by a
    // corporate proxy / no connectivity), stop retrying for the rest of the run
    // and serve the offline classifier so a batch isn't slowed by repeated failures.
    private static volatile bool _hfUnavailable;

    // Candidate labels derived from the reference dataset (test_data_50 / test_categories).
    public static readonly string[] Labels =
    {
        "Test_Job_Alert",
        "Test_Job_Data Issue",
        "Test_Job_File/DB contention",
        "Test_Job_Vendor/Upstream feed unavailability",
        "Test_Job_FTP/Server Connectivity issue"
    };

    // Keyword cues per label for the offline fallback classifier.
    private static readonly Dictionary<string, string[]> Keywords = new()
    {
        ["Test_Job_Alert"] = new[] { "alert", "hold", "waiting", "cond", "warning", "delayed", "pending" },
        ["Test_Job_Data Issue"] = new[] { "data", "load", "record", "missing", "duplicate", "mismatch", "invalid", "abend", "quarterly" },
        ["Test_Job_File/DB contention"] = new[] { "db", "database", "lock", "contention", "deadlock", "file", "table", "resource", "busy" },
        ["Test_Job_Vendor/Upstream feed unavailability"] = new[] { "vendor", "upstream", "feed", "unavailable", "unavailability", "source", "external", "third" },
        ["Test_Job_FTP/Server Connectivity issue"] = new[] { "ftp", "server", "connectivity", "connection", "network", "timeout", "host", "down", "unreachable" }
    };

    public ZeroShotClassifierService(HttpClient http, IConfiguration config, ILogger<ZeroShotClassifierService> log)
    {
        _http = http;
        _log = log;
        _token = config["HuggingFace:Token"];
        _model = config["HuggingFace:Model"] ?? "facebook/bart-large-mnli";

        if (!string.IsNullOrWhiteSpace(_token) && !_token.StartsWith("YOUR_") && _token != "<HF_TOKEN>")
        {
            _http.BaseAddress = new Uri($"https://router.huggingface.co/hf-inference/models/{_model}");
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        }
    }

    private bool HfEnabled =>
        !string.IsNullOrWhiteSpace(_token) && !_token!.StartsWith("YOUR_") && _token != "<HF_TOKEN>";

    public async Task<(string Category, double Confidence)> ClassifyAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (Labels[0], 0.0);

        if (HfEnabled && !_hfUnavailable)
        {
            try
            {
                return await ClassifyWithHuggingFaceAsync(text);
            }
            catch (Exception ex)
            {
                _hfUnavailable = true;
                _log.LogWarning(ex,
                    "Hugging Face inference unavailable (token/network/proxy). " +
                    "Switching to the offline classifier for the remainder of this run.");
            }
        }

        return ClassifyOffline(text);
    }

    private async Task<(string, double)> ClassifyWithHuggingFaceAsync(string text)
    {
        var payload = new
        {
            inputs = text,
            parameters = new { candidate_labels = Labels, multi_label = false },
            options = new { wait_for_model = true }
        };
        var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        var res = await _http.PostAsync("", content);
        res.EnsureSuccessStatusCode();

        var json = JObject.Parse(await res.Content.ReadAsStringAsync());
        var label = json["labels"]?[0]?.ToString() ?? Labels[0];
        var score = json["scores"]?[0]?.Value<double>() ?? 0.0;
        return (label, Math.Round(score, 4));
    }

    /// <summary>Deterministic keyword-weighted scoring used when no HF token is available.</summary>
    private static (string, double) ClassifyOffline(string text)
    {
        var lower = text.ToLowerInvariant();
        var scores = new Dictionary<string, int>();

        foreach (var (label, cues) in Keywords)
            scores[label] = cues.Count(c => lower.Contains(c));

        var best = scores.OrderByDescending(kv => kv.Value).First();
        var totalHits = scores.Values.Sum();

        if (best.Value == 0)
            return (Labels[0], 0.35); // default to Alert with low confidence

        var confidence = Math.Round(0.55 + 0.4 * best.Value / Math.Max(totalHits, best.Value), 4);
        return (best.Key, Math.Min(confidence, 0.99));
    }
}
