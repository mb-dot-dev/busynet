using Busynet.Bar;
using Busynet.Bar.Display;
using Busynet.Bkk;
using Busynet.Bkk.Departures;
using Busynet.Cli.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Cli.Tests;

public class BkkDeparturesCommandTests
{
    private const string Key = "SECRET-KEY";

    private sealed record Setup(AppHarness App, FakeBarFactory Bar, ScriptedDepartures Departures);

    private static StopDepartures Upcoming()
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(17).AddSeconds(30);
        return new StopDepartures("Kötő utca", [new Departure("922", "Astoria M ► Budakeszi", at, at)]);
    }

    private static Setup Create(bool withKey = true, params Func<StopDepartures>[] polls)
    {
        var app = new AppHarness(withKey ? new Dictionary<string, string> { ["BKK_API_KEY"] = Key } : null);
        var bar = new FakeBarFactory();
        var departures = new ScriptedDepartures(polls.Length == 0 ? [Upcoming] : polls);
        app.Services.AddSingleton<IBarFactory>(bar);
        app.Services.AddSingleton<IBkkDepartures>(departures);
        app.Services.AddSingleton(TimeZoneInfo.Utc);
        return new Setup(app, bar, departures);
    }

    [Fact]
    public async Task Clears_draws_then_clears_on_cancel()
    {
        var (app, bar, _) = Create();
        using var scope = app;
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("bkk", "departures");

        Assert.Equal(0, exit);
        Assert.Equal("", app.Stderr.ToString());
        var calls = bar.Display.Calls;
        Assert.Equal(FakeBarDisplay.Clear, calls[0]);
        var draw = Assert.IsType<DisplayElements>(calls[1]);
        var texts = draw.Elements.OfType<TextElement>().Select(t => t.Text).ToList();
        Assert.Matches(@"^1\. 922 1[67]'$", texts[0]);
        Assert.Equal("Astoria M > Budakeszi", texts[1]);
        Assert.Equal(FakeBarDisplay.Clear, calls[^1]);
    }

    [Fact]
    public async Task Defaults_to_kotu_utca()
    {
        var (app, bar, departures) = Create();
        using var scope = app;
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        await app.RunAsync("bkk", "departures");

        Assert.Equal((Key, "BKK_F04720"), departures.Calls[0]);
    }

    [Fact]
    public async Task Stop_id_argument_overrides_the_default()
    {
        var (app, bar, departures) = Create();
        using var scope = app;
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        await app.RunAsync("bkk", "departures", "BKK_F04807");

        Assert.Equal((Key, "BKK_F04807"), departures.Calls[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_api_key_exits_1_before_touching_the_bar(string value)
    {
        var app = new AppHarness(value.Length == 0 ? null : new Dictionary<string, string> { ["BKK_API_KEY"] = value });
        using var scope = app;
        var bar = new FakeBarFactory();
        var departures = new ScriptedDepartures(Upcoming);
        app.Services.AddSingleton<IBarFactory>(bar);
        app.Services.AddSingleton<IBkkDepartures>(departures);

        var exit = await app.RunAsync("bkk", "departures");

        Assert.Equal(1, exit);
        Assert.Equal("BKK_API_KEY is not set. Get a key at https://opendata.bkk.hu." + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Connections);
        Assert.Empty(bar.Display.Calls);
        Assert.Equal(0, departures.Polls);
    }

    [Theory]
    [InlineData("bkk", "departures", " ")]
    [InlineData("bkk", "departures", "--rotate", "0")]
    [InlineData("bkk", "departures", "--interval", "0")]
    [InlineData("bkk", "departures", "BKK_F04720", "extra")]
    [InlineData("bkk")]
    public async Task Bad_command_lines_exit_2(params string[] args)
    {
        var (app, bar, departures) = Create();
        using var scope = app;

        var exit = await app.RunAsync(args);

        Assert.Equal(2, exit);
        Assert.NotEqual("", app.Stderr.ToString());
        Assert.Empty(bar.Connections);
        Assert.Equal(0, departures.Polls);
    }

    [Fact]
    public async Task Unknown_stop_exits_1_with_its_message()
    {
        var (app, bar, _) = Create(true, () => throw new BkkException("Unknown BKK stop id: BKK_NOPE."));
        using var scope = app;

        var exit = await app.RunAsync("bkk", "departures", "BKK_NOPE");

        Assert.Equal(1, exit);
        Assert.Equal("Unknown BKK stop id: BKK_NOPE." + Environment.NewLine, app.Stderr.ToString());
        Assert.Equal(FakeBarDisplay.Clear, bar.Display.Calls[^1]);
    }

    [Fact]
    public async Task Help_lists_the_argument_and_options()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync("bkk", "departures", "--help");

        Assert.Equal(0, exit);
        var help = app.ConsoleOut.ToString();
        Assert.Contains("STOP_ID", help, StringComparison.Ordinal);
        Assert.Contains("BKK_F04720", help, StringComparison.Ordinal);
        Assert.Contains("--interval", help, StringComparison.Ordinal);
        Assert.Contains("--rotate", help, StringComparison.Ordinal);
    }
}
