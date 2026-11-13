using Busynet.Bar;
using Busynet.Claude;
using Busynet.Claude.Usage;
using Busynet.Cli.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Cli.Tests;

public class UsageErrorTests
{
    private static FakeBarFactory UseFakes(AppHarness app, Func<Usage>? usage = null)
    {
        var bar = new FakeBarFactory();
        app.Services.AddSingleton<IBarFactory>(bar);
        app.Services.AddSingleton<IClaudeUsage>(
            new ScriptedUsage(usage ?? (() => throw new InvalidOperationException("never fetched"))));
        return bar;
    }

    [Fact]
    public async Task Interval_zero_exits_2_with_exact_message_on_stderr_and_empty_stdout()
    {
        using var app = new AppHarness();
        UseFakes(app);

        var exit = await app.RunAsync("claude", "limits", "--interval", "0");

        Assert.Equal(2, exit);
        Assert.Equal("--interval must be at least 1 second." + Environment.NewLine, app.Stderr.ToString());
        Assert.Equal("", app.ConsoleOut.ToString());
    }

    [Fact]
    public async Task Interval_not_a_number_exits_2_one_line_stderr()
    {
        using var app = new AppHarness();
        UseFakes(app);

        var exit = await app.RunAsync("claude", "limits", "--interval", "abc");

        Assert.Equal(2, exit);
        Assert.Single(app.Stderr.ToString().TrimEnd().Split('\n'));
    }

    [Fact]
    public async Task Unknown_option_exits_2()
    {
        using var app = new AppHarness();
        UseFakes(app);

        Assert.Equal(2, await app.RunAsync("claude", "limits", "--bogus"));
        Assert.NotEqual("", app.Stderr.ToString());
    }

    [Fact]
    public async Task Unknown_command_exits_2()
    {
        using var app = new AppHarness();
        UseFakes(app);

        Assert.Equal(2, await app.RunAsync("bogus"));
        Assert.NotEqual("", app.Stderr.ToString());
    }

    [Fact]
    public async Task Host_option_without_value_exits_2_and_never_touches_bar()
    {
        using var app = new AppHarness();
        var bar = UseFakes(app);

        var exit = await app.RunAsync("claude", "limits", "--host");

        Assert.Equal(2, exit);
        Assert.Empty(bar.Connections);
    }

    [Fact]
    public async Task Token_equals_form_starting_with_dash_reaches_connection()
    {
        using var app = new AppHarness();
        var bar = UseFakes(app, () => throw new ClaudeTransientException("stop here"));

        var exit = await app.RunAsync("claude", "limits", "--token=-abc");

        Assert.Equal(1, exit);
        Assert.Equal("-abc", bar.Connections.Single().Token);
    }

    [Fact]
    public async Task Short_version_alias_prints_version()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync("-v");

        Assert.Equal(0, exit);
        Assert.Matches(@"\A\d+\.\d+\.\d+\S*\r?\n\z", app.ConsoleOut.ToString());
    }

    [Fact]
    public async Task Verbose_failure_prints_full_exception_and_plain_run_one_line()
    {
        using var verbose = new AppHarness();
        UseFakes(verbose, () => throw new ClaudeTransientException("boom"));
        using var plain = new AppHarness();
        UseFakes(plain, () => throw new ClaudeTransientException("boom"));

        Assert.Equal(1, await verbose.RunAsync("claude", "limits", "--verbose"));
        Assert.Equal(1, await plain.RunAsync("claude", "limits"));

        Assert.Contains("ClaudeTransientException", verbose.Stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("boom", verbose.Stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("boom" + Environment.NewLine, plain.Stderr.ToString());
    }

    [Fact]
    public async Task Unexpected_exception_prints_in_full_and_exits_1()
    {
        using var app = new AppHarness();
        UseFakes(app);

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(1, exit);
        Assert.Contains("InvalidOperationException", app.Stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("never fetched", app.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_starting_with_at_sign_is_a_value_not_a_response_file()
    {
        using var app = new AppHarness();
        var bar = UseFakes(app, () => throw new ClaudeTransientException("stop here"));

        var exit = await app.RunAsync("claude", "limits", "--token", "@SECRET123");

        Assert.Equal(1, exit);
        Assert.Equal("@SECRET123", bar.Connections.Single().Token);
        Assert.DoesNotContain("SECRET123", app.Stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--token", "SECRET123", "claude", "limits")]
    [InlineData("claude", "limits", "--tokn", "SECRET123")]
    [InlineData("claude", "limits", "--TOKEN", "SECRET123")]
    public async Task Unmatched_value_after_a_misplaced_or_misspelled_option_is_not_echoed(params string[] args)
    {
        using var app = new AppHarness();
        UseFakes(app);

        var exit = await app.RunAsync(args);

        Assert.Equal(2, exit);
        Assert.NotEqual("", app.Stderr.ToString());
        Assert.DoesNotContain("SECRET123", app.Stderr.ToString(), StringComparison.Ordinal);
    }
}
