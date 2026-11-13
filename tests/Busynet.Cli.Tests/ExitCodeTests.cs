using System.Text.RegularExpressions;
using Busynet.Bar;
using Busynet.Claude.Usage;
using Busynet.Cli.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Cli.Tests;

public partial class ExitCodeTests
{
    [Fact]
    public async Task Version_prints_version_exit_0()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync("--version");

        Assert.Equal(0, exit);
        Assert.Matches(VersionLine(), app.ConsoleOut.ToString());
        Assert.DoesNotContain("+", app.ConsoleOut.ToString(), StringComparison.Ordinal);
        Assert.Equal("", app.Stderr.ToString());
    }

    [Fact]
    public async Task Help_exits_0()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync("claude", "limits", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("--interval", app.ConsoleOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_host_option_is_usage_error_exit_2_without_echoing_it()
    {
        using var app = new AppHarness();
        var bar = UseFakes(app);

        var exit = await app.RunAsync("claude", "limits", "--host", "bad host[");

        Assert.Equal(2, exit);
        Assert.Equal("Invalid --host value." + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Connections);
    }

    [Fact]
    public async Task Invalid_token_option_is_usage_error_exit_2_without_echoing_it()
    {
        using var app = new AppHarness();
        UseFakes(app);

        var exit = await app.RunAsync("claude", "limits", "--token", "SECRET\nx");

        Assert.Equal(2, exit);
        Assert.Equal("Invalid --token value." + Environment.NewLine, app.Stderr.ToString());
    }

    [Fact]
    public async Task Invalid_host_variable_exits_1_with_one_line()
    {
        using var app = new AppHarness(new Dictionary<string, string> { [BarConnection.HostVariable] = "bad host[" });
        var bar = UseFakes(app);

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(1, exit);
        Assert.Equal("BUSYBOY_HOST is not a valid host." + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Connections);
    }

    [Fact]
    public async Task Invalid_token_variable_exits_1_with_one_line()
    {
        // The real factory: the token is rejected when it becomes the X-API-Token header.
        using var app = new AppHarness(new Dictionary<string, string> { [BarConnection.TokenVariable] = "SECRET\nx" });
        app.Services.AddSingleton<IClaudeUsage>(new ScriptedUsage(() => throw new InvalidOperationException("never fetched")));

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(1, exit);
        Assert.Equal("BUSYBOY_TOKEN is not a valid token." + Environment.NewLine, app.Stderr.ToString());
    }

    private static FakeBarFactory UseFakes(AppHarness app)
    {
        var bar = new FakeBarFactory();
        app.Services.AddSingleton<IBarFactory>(bar);
        app.Services.AddSingleton<IClaudeUsage>(new ScriptedUsage(() => throw new InvalidOperationException("never fetched")));
        return bar;
    }

    [GeneratedRegex(@"\A\d+\.\d+\.\d+\S*\r?\n\z")]
    private static partial Regex VersionLine();
}
