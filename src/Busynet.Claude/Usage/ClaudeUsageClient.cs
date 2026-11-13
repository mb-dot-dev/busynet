using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Busynet.Claude.Credentials;
using Refit;

namespace Busynet.Claude.Usage;

/// <summary>Classifies every failure as fatal (auth) or transient; no message ever includes the token or body.</summary>
internal sealed partial class ClaudeUsageClient(IClaudeTokenSource tokens, IClaudeUsageApi api) : IClaudeUsage
{
    private const string Rejected = "Anthropic rejected the Claude Code token. Run `claude` to log in again.";
    private const string Unexpected = "Claude usage API returned an unexpected response";
    private const double MaxRetryAfterSeconds = 3600;

    public async Task<Usage> GetUsageAsync(CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(ct).ConfigureAwait(false);
        try
        {
            using var response = await api.GetUsageAsync(token, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
                throw new ClaudeAuthException(Rejected);
            if (status == 429)
                throw new ClaudeTransientException("Claude usage API rate limited the request", ParseRetryAfter(response));
            if (status >= 500)
                throw new ClaudeTransientException($"Claude usage API returned HTTP {status}");
            if (status >= 400)
                throw new ClaudeException($"Claude usage API returned HTTP {status}");

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(body);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ApiRequestException)
        {
            // Refit wraps transport failures and timeouts in ApiRequestException: report the inner type.
            ct.ThrowIfCancellationRequested();
            var cause = e is ApiRequestException { InnerException: { } inner } ? inner : e;
            throw new ClaudeTransientException($"Claude usage request failed: {cause.GetType().Name}");
        }
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
            return null;
        var raw = values.FirstOrDefault()?.Trim();
        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
            return null;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, MaxRetryAfterSeconds));
    }

    private static Usage Parse(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject root
                && ParseWindow(root["five_hour"]) is { } session
                && ParseWindow(root["seven_day"]) is { } weekly)
                return new Usage(session, weekly);
        }
        catch (JsonException)
        {
            // Deliberately not forwarded: the message may quote the body.
        }

        throw new ClaudeTransientException(Unexpected);
    }

    private static UsageWindow? ParseWindow(JsonNode? node)
    {
        if (node is not JsonObject obj
            || obj["utilization"] is not JsonValue u || u.GetValueKind() != JsonValueKind.Number
            || !u.TryGetValue<double>(out var utilization) || !double.IsFinite(utilization))
            return null;

        switch (obj["resets_at"])
        {
            case null:
                return new UsageWindow(utilization, null);
            case JsonValue r when r.GetValueKind() == JsonValueKind.String
                && r.TryGetValue<string>(out var text)
                && ExplicitOffset().IsMatch(text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var resetsAt):
                return new UsageWindow(utilization, resetsAt);
            default:
                return null;
        }
    }

    // A timestamp without an offset is ambiguous; require Z or +hh:mm.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T.*(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ExplicitOffset();
}
