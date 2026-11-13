using System.Text.Json.Nodes;
using Busynet.Bar.Display;
using Refit;

namespace Busynet.Bar.Http;

internal sealed class BarDisplay(IBusyBarApi api, TimeSpan responseTimeout) : IBarDisplay
{
    private const string DrawPath = "/api/display/draw";

    // Observed on firmware 27.15.0: DELETE /api/display/draw answers 400 with this error when the
    // application has nothing on screen. The desired state already holds, so clearing treats it as success.
    private const string NothingDisplayedError = "this application_name is currently not displaying anything";

    public Task DrawAsync(DisplayElements payload, CancellationToken ct) =>
        SendAsync("POST", ct => api.DrawAsync(payload, ct), ct);

    public Task ClearAsync(CancellationToken ct) =>
        SendAsync("DELETE", ct => api.ClearAsync(BarApplication.Name, ct), ct, IsNothingDisplayed);

    private static bool IsNothingDisplayed(int status, string? error) =>
        status == 400 && string.Equals(error?.Trim(), NothingDisplayedError, StringComparison.OrdinalIgnoreCase);

    private async Task SendAsync(string method, Func<CancellationToken, Task<HttpResponseMessage>> call, CancellationToken ct,
        Func<int, string?, bool>? isBenign = null)
    {
        ct.ThrowIfCancellationRequested();
        // Refit returns once the headers arrive, so HttpClient.Timeout does not bound the body read.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(responseTimeout);
        try
        {
            using var response = await call(deadline.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return;

            var (error, code) = await ReadErrorAsync(response, deadline.Token).ConfigureAwait(false);
            if (isBenign?.Invoke((int)response.StatusCode, error) == true)
                return;

            throw new BarApiException((int)response.StatusCode, method, DrawPath,
                error ?? response.ReasonPhrase ?? "error", code);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ApiRequestException)
        {
            // Refit wraps transport failures and timeouts in ApiRequestException: report the inner type.
            ct.ThrowIfCancellationRequested();
            var cause = e is ApiRequestException { InnerException: { } inner } ? inner : e;
            throw new BarRequestException(method, DrawPath, cause.GetType().Name);
        }
    }

    private static async Task<(string? Error, string? Code)> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (JsonNode.Parse(body) is JsonObject obj
                && obj["error"] is JsonValue error && error.TryGetValue<string>(out var message))
                return (message, obj["error_code"] is JsonValue c && c.TryGetValue<string>(out var code) ? code : null);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or HttpRequestException or InvalidOperationException)
        {
            // Body is not the Error schema; fall back to the reason phrase.
        }

        return (null, null);
    }
}
