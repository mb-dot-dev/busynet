using Busynet.Bar.Buttons;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Bar.Tests.Buttons;

public sealed class StatusSocketTests
{
    private static BarConnection Connection(string? token) =>
        BarConnection.Resolve("bar.local", token, _ => null);

    [Fact]
    public void StatusSocket_uri_escapes_token()
    {
        var uri = StatusSocket.BuildUri(Connection("a b&c"));

        Assert.Equal("ws", uri.Scheme);
        Assert.Equal("/api/status/ws", uri.AbsolutePath);
        Assert.Equal("?x-api-token=a%20b%26c", uri.Query);
        Assert.Equal("ws://bar.local/api/status/ws?x-api-token=a%20b%26c", uri.AbsoluteUri);
    }

    [Fact]
    public void StatusSocket_uri_without_token_has_no_query()
    {
        var uri = StatusSocket.BuildUri(Connection(null));

        Assert.Equal(string.Empty, uri.Query);
        Assert.Equal("ws://bar.local/api/status/ws", uri.AbsoluteUri);
    }

    [Fact]
    public void Factory_creates_button_events_without_connecting()
    {
        using var services = new ServiceCollection().AddBusyBar().BuildServiceProvider();

        // Nothing subscribes, so nothing connects to the (unresolvable) host.
        var events = services.GetRequiredService<IBarFactory>().CreateButtonEvents(Connection("t"));

        Assert.NotNull(events);
    }
}
