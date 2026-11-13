using System.Collections.Concurrent;
using System.Net;
using Busynet.Bar;
using Busynet.Bar.Display;
using Busynet.Claude;
using Busynet.Claude.Credentials;
using Busynet.Claude.Usage;
using Busynet.Cli.Tests.Fakes;
using Busynet.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Busynet.Cli.Tests;

public class ClaudeLimitsCommandTests
{
    private const string AuthMessage = "Claude Code credentials not found. Run `claude` to log in.";

    private static readonly Usage SomeUsage = new(
        new UsageWindow(42, DateTimeOffset.UtcNow.AddHours(3)),
        new UsageWindow(10, DateTimeOffset.UtcNow.AddDays(4)));

    private const string UsageJson = """
        {"five_hour":{"utilization":42,"resets_at":"2099-01-01T00:00:00Z"},
         "seven_day":{"utilization":10,"resets_at":"2099-01-05T00:00:00Z"}}
        """;

    private static (AppHarness App, FakeBarFactory Bar) Create(IClaudeUsage usage)
    {
        var app = new AppHarness();
        var bar = new FakeBarFactory();
        app.Services.AddSingleton<IBarFactory>(bar);
        app.Services.AddSingleton(usage);
        return (app, bar);
    }

    [Fact]
    public async Task Startup_auth_failure_prints_one_line_and_exits_1()
    {
        var (app, bar) = Create(new ScriptedUsage(() => throw new ClaudeAuthException(AuthMessage)));
        using var scope = app;

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(1, exit);
        Assert.Equal(AuthMessage + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Display.Calls);
    }

    [Fact]
    public async Task Startup_transient_failure_exits_1()
    {
        var (app, bar) = Create(new ScriptedUsage(() => throw new ClaudeTransientException("Claude usage API returned HTTP 503")));
        using var scope = app;

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(1, exit);
        Assert.Equal("Claude usage API returned HTTP 503" + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Display.Calls);
    }

    [Fact]
    public async Task Draws_then_clears_on_cancel_and_exits_0()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(0, exit);
        Assert.Equal("", app.Stderr.ToString());
        var calls = bar.Display.Calls;
        Assert.Equal(3, calls.Count);
        Assert.Equal(FakeBarDisplay.Clear, calls[0]);
        var draw = Assert.IsType<DisplayElements>(calls[1]);
        Assert.StartsWith("5h ", draw.Elements.OfType<TextElement>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(FakeBarDisplay.Clear, calls[2]);
    }

    [Fact]
    public async Task Uses_default_host_without_options_or_variables()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        await app.RunAsync("claude", "limits", "--token", "t");

        var connection = Assert.Single(bar.Connections);
        Assert.Equal(new Uri("http://10.0.4.20/"), connection.HttpBaseAddress);
        Assert.Equal("t", connection.Token);
    }

    [Fact]
    public async Task Startup_succeeds_when_first_draw_is_rejected()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;
        bar.Display.FailNextDraw(new BarApiException(409, "POST", "/api/display/draw", "busy", null));
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(0, exit);
        Assert.Equal("", app.Stderr.ToString());
    }

    [Fact]
    public async Task Startup_clear_failure_exits_1()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;
        bar.Display.FailNextClear(new BarRequestException("DELETE", "/api/display/draw", nameof(HttpRequestException)));

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(1, exit);
        Assert.Equal("request error | DELETE /api/display/draw | HttpRequestException" + Environment.NewLine,
            app.Stderr.ToString());
    }

    [Fact]
    public async Task Cancel_during_startup_exits_zero()
    {
        var usage = new BlockingUsage();
        var (app, bar) = Create(usage);
        using var scope = app;
        _ = usage.Started.Task.ContinueWith(_ => app.Cts.Cancel(), TaskScheduler.Default);

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(0, exit);
        Assert.Equal("", app.Stderr.ToString());
        Assert.Empty(bar.Display.Calls);
    }

    [Fact]
    public async Task Cancel_before_start_exits_zero()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;
        await app.Cts.CancelAsync();

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(0, exit);
        Assert.Equal("", app.Stderr.ToString());
        Assert.Empty(bar.Display.Calls);
    }

    [Fact]
    public async Task Interval_below_1_is_usage_error_exit_2()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;

        var exit = await app.RunAsync("claude", "limits", "--interval", "0");

        Assert.Equal(2, exit);
        Assert.NotEqual("", app.Stderr.ToString());
        Assert.Empty(bar.Connections);
    }

    [Fact]
    public async Task Unknown_option_exits_2()
    {
        var (app, _) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;

        var exit = await app.RunAsync("claude", "limits", "--bogus");

        Assert.Equal(2, exit);
        Assert.NotEqual("", app.Stderr.ToString());
    }

    [Fact]
    public async Task Verbose_shows_stack_trace()
    {
        var (app, _) = Create(new ScriptedUsage(() => throw new ClaudeAuthException(AuthMessage)));
        using var scope = app;

        var exit = await app.RunAsync("claude", "limits", "--verbose");

        Assert.Equal(1, exit);
        var stderr = app.Stderr.ToString();
        Assert.Contains(AuthMessage, stderr, StringComparison.Ordinal);
        Assert.Contains("   at ", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verbose_output_never_contains_tokens()
    {
        const string BarSecret = "BARSECRET";
        const string ClaudeSecret = "CLAUDESECRET";

        using var app = new AppHarness();
        var http = new FakeHttpHandler();
        http.Add(HttpMethod.Post, "/api/display/draw", HttpStatusCode.Conflict, """{"error":"busy"}""");
        http.Add(HttpMethod.Delete, "/api/display/draw", HttpStatusCode.OK);
        http.Add(HttpMethod.Get, "/api/oauth/usage", HttpStatusCode.OK, UsageJson);
        var logs = new CapturingLoggerProvider();
        app.Services.AddHttpClient("busybar").ConfigurePrimaryHttpMessageHandler(() => http);
        app.Services.AddHttpClient("claude").ConfigurePrimaryHttpMessageHandler(() => http);
        app.Services.AddSingleton<IClaudeTokenSource>(new FixedTokenSource(ClaudeSecret));
        app.Services.AddSingleton<ILoggerProvider>(logs);

        var run = app.RunAsync("claude", "limits", "--verbose", "--host", "127.0.0.1:9", "--token", BarSecret);
        // One cycle: the first draw was refused and the button socket has failed (connection refused) once.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline
               && !(http.Calls.Any(c => c.Method == HttpMethod.Post)
                    && logs.Lines.Any(l => l.Contains("Button stream", StringComparison.Ordinal))))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        await app.Cts.CancelAsync();
        var exit = await run;

        Assert.Equal(0, exit);
        Assert.Contains(http.Calls, c => c.Method == HttpMethod.Post);
        Assert.Contains(logs.Lines, l => l.Contains("Button stream", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, l => l.Contains("409", StringComparison.Ordinal));
        var everything = string.Join("\n", logs.Lines) + app.Stderr + app.ConsoleOut;
        Assert.DoesNotContain(BarSecret, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaudeSecret, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_verbose_only_warnings_are_logged()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;
        var logs = new CapturingLoggerProvider();
        app.Services.AddSingleton<ILoggerProvider>(logs);
        bar.Display.FailNextDraw(new BarApiException(409, "POST", "/api/display/draw", "busy", null));
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(0, exit);
        Assert.Empty(logs.Lines);
    }

    [Fact]
    public async Task Verbose_logs_debug()
    {
        var (app, bar) = Create(new ScriptedUsage(() => SomeUsage));
        using var scope = app;
        var logs = new CapturingLoggerProvider();
        app.Services.AddSingleton<ILoggerProvider>(logs);
        bar.Display.FailNextDraw(new BarApiException(409, "POST", "/api/display/draw", "busy", null));
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("claude", "limits", "--verbose");

        Assert.Equal(0, exit);
        Assert.Contains(logs.Lines, l => l.StartsWith("Debug ", StringComparison.Ordinal) && l.Contains("409", StringComparison.Ordinal));
    }

    private sealed class FixedTokenSource(string token) : IClaudeTokenSource
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    /// <summary>Every log line, formatted with its exception, from any category at any enabled level.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
