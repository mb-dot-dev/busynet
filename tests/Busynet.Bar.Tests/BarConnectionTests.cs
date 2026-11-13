namespace Busynet.Bar.Tests;

public sealed class BarConnectionTests
{
    private static Func<string, string?> Env(string? host = null, string? token = null) =>
        name => name switch
        {
            BarConnection.HostVariable => host,
            BarConnection.TokenVariable => token,
            _ => null,
        };

    [Fact]
    public void Option_beats_environment_beats_default()
    {
        Assert.Equal("10.0.4.20", BarConnection.Resolve(null, null, Env()).HttpBaseAddress.Host);
        Assert.Equal("env.host", BarConnection.Resolve(null, null, Env("env.host")).HttpBaseAddress.Host);
        Assert.Equal("opt.host", BarConnection.Resolve("opt.host", null, Env("env.host")).HttpBaseAddress.Host);

        Assert.Null(BarConnection.Resolve(null, null, Env()).Token);
        Assert.Equal("envtok", BarConnection.Resolve(null, null, Env(token: "envtok")).Token);
        Assert.Equal("opttok", BarConnection.Resolve(null, "opttok", Env(token: "envtok")).Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_token_is_treated_as_absent(string blank)
    {
        Assert.Null(BarConnection.Resolve(null, blank, Env()).Token);
        Assert.Null(BarConnection.Resolve(null, null, Env(token: blank)).Token);
    }

    [Theory]
    [InlineData("10.0.4.20", "http://10.0.4.20/", "ws://10.0.4.20/api/status/ws")]
    [InlineData("10.0.4.20:8080", "http://10.0.4.20:8080/", "ws://10.0.4.20:8080/api/status/ws")]
    [InlineData("http://bar.local", "http://bar.local/", "ws://bar.local/api/status/ws")]
    [InlineData("https://bar.example", "https://bar.example/", "wss://bar.example/api/status/ws")]
    public void Host_forms_map_to_http_and_ws_uris(string host, string http, string ws)
    {
        var connection = BarConnection.Resolve(host, null, Env());

        Assert.Equal(http, connection.HttpBaseAddress.AbsoluteUri);
        Assert.Equal(ws, connection.StatusSocketUri.AbsoluteUri);
    }

    [Fact]
    public void ToString_never_contains_token()
    {
        var connection = BarConnection.Resolve(null, "s3cret", Env());

        Assert.DoesNotContain("s3cret", connection.ToString());
        Assert.Contains("***", connection.ToString());
        Assert.DoesNotContain("s3cret", connection.StatusSocketUri.AbsoluteUri);
    }
}
