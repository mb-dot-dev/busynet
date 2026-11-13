using System.Text;

namespace Busynet.Bar;

/// <summary>Where the bar is, and the optional API token used to talk to it.</summary>
public sealed record BarConnection(Uri HttpBaseAddress, Uri StatusSocketUri, string? Token)
{
    public const string DefaultHost = "10.0.4.20";
    public const string HostVariable = "BUSYBOY_HOST";
    public const string TokenVariable = "BUSYBOY_TOKEN";

    private const string StatusSocketPath = "/api/status/ws";

    public static BarConnection Resolve(string? hostOption, string? tokenOption, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var host = FirstNonBlank(hostOption, environment(HostVariable)) ?? DefaultHost;
        var token = FirstNonBlank(tokenOption, environment(TokenVariable));

        var text = host.Contains("://", StringComparison.Ordinal) ? host : $"http://{host}";
        var http = new UriBuilder(text) { Path = "/", Query = string.Empty, Fragment = string.Empty };
        var socket = new UriBuilder(http.Uri)
        {
            Scheme = http.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = StatusSocketPath,
        };

        return new BarConnection(http.Uri, socket.Uri, token);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("HttpBaseAddress = ").Append(HttpBaseAddress)
            .Append(", StatusSocketUri = ").Append(StatusSocketUri)
            .Append(", Token = ").Append(Token is null ? "null" : "***");
        return true;
    }
}
