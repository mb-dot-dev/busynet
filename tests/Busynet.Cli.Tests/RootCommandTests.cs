using Busynet.Bar;
using Busynet.Bar.Buttons;
using Busynet.Bar.Http;
using Busynet.Claude.Usage;
using Busynet.Cli.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Cli.Tests;

public class RootCommandTests
{
    private static readonly Usage SomeUsage = new(
        new UsageWindow(42, DateTimeOffset.UtcNow.AddHours(3)),
        new UsageWindow(10, DateTimeOffset.UtcNow.AddDays(4)));

    [Fact]
    public async Task No_args_prints_help_to_stdout_exit_0()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync();

        Assert.Equal(0, exit);
        Assert.Contains("claude", app.ConsoleOut.ToString(), StringComparison.Ordinal);
        Assert.Equal("", app.Stderr.ToString());
    }

    [Fact]
    public async Task Claude_without_subcommand_is_usage_error_exit_2()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync("claude");

        Assert.Equal(2, exit);
        Assert.NotEqual("", app.Stderr.ToString());
        Assert.Equal("", app.ConsoleOut.ToString());
    }

    [Fact]
    public async Task Claude_help_lists_limits_exit_0()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync("claude", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("limits", app.ConsoleOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ctrl_C_while_watching_exits_0_with_empty_stderr()
    {
        using var app = new AppHarness();
        var bar = new FakeBarFactory();
        app.Services.AddSingleton<IBarFactory>(bar);
        app.Services.AddSingleton<IClaudeUsage>(new ScriptedUsage(() => SomeUsage));
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(0, exit);
        Assert.Equal("", app.Stderr.ToString());
    }

    [Fact]
    public async Task Provider_is_disposed_after_a_failed_run()
    {
        using var app = new AppHarness();
        var factory = new DisposableBarFactory();
        app.Services.AddSingleton<IBarFactory>(_ => factory);
        app.Services.AddSingleton<IClaudeUsage>(new ScriptedUsage(() => throw new InvalidOperationException("boom")));

        var exit = await app.RunAsync("claude", "limits");

        Assert.Equal(1, exit);
        Assert.True(factory.Disposed);
    }

    private sealed class DisposableBarFactory : IBarFactory, IDisposable
    {
        private readonly FakeBarFactory _inner = new();

        public bool Disposed { get; private set; }

        public IBarDisplay CreateDisplay(BarConnection connection) => _inner.CreateDisplay(connection);

        public IObservable<ButtonEvent> CreateButtonEvents(BarConnection connection) => _inner.CreateButtonEvents(connection);

        public void Dispose() => Disposed = true;
    }
}
