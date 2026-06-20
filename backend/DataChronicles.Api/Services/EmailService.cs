using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using DataChronicles.Api.Models;

namespace DataChronicles.Api.Services;

/// <summary>
/// Optional SMTP email delivery of the generated categorization report.
/// If SMTP is not configured the service is disabled and returns a friendly failure
/// (never throws), so the app runs locally without a mail server.
/// Uses the built-in <see cref="SmtpClient"/> — no external NuGet dependency.
/// Gmail example: SmtpServer=smtp.gmail.com, Port=587, UseSsl=true, and an App Password
/// (requires 2-Step Verification) for the password.
/// </summary>
public class EmailService
{
    private readonly ILogger<EmailService> _log;
    private readonly bool _flagEnabled;
    private readonly string? _host;
    private readonly int _port;
    private readonly string? _from;
    private readonly string? _user;
    private readonly string? _password;
    private readonly bool _useSsl;

    /// <summary>Confidence below this is treated as "needs review" in the summary body.</summary>
    public const double LowConfidence = 0.6;

    public EmailService(IConfiguration config, ILogger<EmailService> log)
    {
        _log = log;
        _flagEnabled = config.GetValue("Email:Enabled", false);
        _host = config["Email:SmtpServer"];
        _port = config.GetValue("Email:Port", 587);
        _from = config["Email:From"];
        _user = config["Email:Username"];
        _password = config["Email:Password"];
        _useSsl = config.GetValue("Email:UseSsl", true);
    }

    /// <summary>True only when enabled in config and the SMTP server + From address are set (not placeholders).</summary>
    public bool Enabled => _flagEnabled && IsSet(_host) && IsSet(_from);

    private static bool IsSet(string? v) =>
        !string.IsNullOrWhiteSpace(v) && !v.StartsWith("YOUR_", StringComparison.Ordinal);

    /// <summary>
    /// Sends an HTML email with an optional file attachment. Returns (false, message) when the
    /// service is disabled or SMTP fails — it never throws so callers can surface the message.
    /// </summary>
    public async Task<(bool Ok, string Message)> SendAsync(
        string to, string subject, string htmlBody, byte[]? attachment, string? attachmentName)
    {
        if (!Enabled)
            return (false, "Email is not configured on the server.");

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_from!),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);

            Attachment? att = null;
            if (attachment is { Length: > 0 })
            {
                att = new Attachment(
                    new MemoryStream(attachment),
                    attachmentName ?? "report.xlsx",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                message.Attachments.Add(att);
            }

            using var client = new SmtpClient(_host!, _port) { EnableSsl = _useSsl };
            if (IsSet(_user))
                client.Credentials = new NetworkCredential(_user, _password);

            await client.SendMailAsync(message);
            att?.Dispose();

            if (_log.IsEnabled(LogLevel.Information))
                _log.LogInformation("Categorization report emailed to {Recipient}.", to);
            return (true, $"Report sent to {to}.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send categorization report to {Recipient}.", to);
            return (false, $"Could not send the email: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the HTML summary body from a batch's tickets. Pure (no I/O) so it is unit-testable.
    /// Includes totals, per-category counts, duplicate count and the low-confidence (review) count.
    /// </summary>
    public static string BuildSummaryHtml(string batchId, IReadOnlyList<OutputTicket> tickets)
    {
        var total = tickets.Count;
        var duplicates = tickets.Count(t => t.IsDuplicate);
        var lowConfidence = tickets.Count(t => t.Confidence < LowConfidence);

        var byCategory = tickets
            .GroupBy(t => t.Category)
            .Select(g => (Category: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(ci, $"<h2>Data Chronicles — Categorization Report</h2>");
        sb.Append(ci, $"<p>Batch <strong>{batchId}</strong></p>");
        sb.Append("<ul>");
        sb.Append(ci, $"<li><strong>Total tickets:</strong> {total}</li>");
        sb.Append(ci, $"<li><strong>Duplicates flagged:</strong> {duplicates}</li>");
        sb.Append(ci, $"<li><strong>Low-confidence (below {LowConfidence * 100:0}% — review recommended):</strong> {lowConfidence}</li>");
        sb.Append("</ul>");

        sb.Append("<h3>By category</h3>");
        sb.Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
        sb.Append("<tr><th align=\"left\">Category</th><th align=\"left\">Count</th></tr>");
        foreach (var (category, count) in byCategory)
            sb.Append(ci, $"<tr><td>{category}</td><td>{count}</td></tr>");
        sb.Append("</table>");

        sb.Append("<p>The full categorized workbook is attached.</p>");
        return sb.ToString();
    }
}
