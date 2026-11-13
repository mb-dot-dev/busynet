using System.Net;
using System.Text;

namespace Busynet.Testing;

/// <summary>One request seen by <see cref="FakeHttpHandler"/>; headers include content headers.</summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);

/// <summary>
/// Fake HTTP transport routed by method + <see cref="Uri.AbsolutePath"/> (host and query ignored).
/// A route registered several times responds in order, the last registration repeating.
/// An unregistered route throws <see cref="InvalidOperationException"/>. Thread-safe.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Method, string Path), List<Func<HttpRequestMessage, HttpResponseMessage>>> _routes = [];
    private readonly Dictionary<(string Method, string Path), int> _served = [];
    private readonly List<RecordedRequest> _calls = [];

    public IReadOnlyList<RecordedRequest> Calls
    {
        get
        {
            lock (_gate)
                return [.. _calls];
        }
    }

    public void Add(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        lock (_gate)
        {
            var key = (method.Method, path);
            if (!_routes.TryGetValue(key, out var list))
                _routes[key] = list = [];
            list.Add(respond);
        }
    }

    public void Add(HttpMethod method, string path, HttpStatusCode status, string? json = null,
        IDictionary<string, string>? headers = null) =>
        Add(method, path, _ =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (headers is not null)
                foreach (var (name, value) in headers)
                    response.Headers.TryAddWithoutValidation(name, value);
            return response;
        });

    public void Throw(HttpMethod method, string path, Exception exception) =>
        Add(method, path, _ => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request has no URI.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers)
            headers[h.Key] = string.Join(", ", h.Value);
        string? body = null;
        if (request.Content is { } content)
        {
            foreach (var h in content.Headers)
                headers[h.Key] = string.Join(", ", h.Value);
            body = await content.ReadAsStringAsync(cancellationToken);
        }

        Func<HttpRequestMessage, HttpResponseMessage> respond;
        lock (_gate)
        {
            _calls.Add(new RecordedRequest(request.Method, uri, headers, body));
            var key = (request.Method.Method, uri.AbsolutePath);
            if (!_routes.TryGetValue(key, out var list))
                throw new InvalidOperationException($"Unregistered route: {request.Method} {uri.AbsolutePath}");
            _served.TryGetValue(key, out var n);
            respond = list[Math.Min(n, list.Count - 1)];
            _served[key] = n + 1;
        }
        return respond(request);
    }
}
