using System.Reactive.Subjects;
using Busynet.Core.Watching;
using Busynet.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Busynet.Core.Tests.Watching;

public class DisplayWatchTests
{
    private sealed record View(string Text);

    private sealed class Transient() : BusynetException("409 conflict"), ITransientFailure
    {
        public TimeSpan? RetryAfter => null;
    }

    private sealed class FatalTest() : Exception("fatal");

    private sealed class Harness : IDisposable
    {
        public Subject<Func<int, int>> Updates { get; } = new();
        public FakeDisplay<View> Display { get; } = new();
        public CancellationTokenSource Cts { get; } = new();
        public Func<int, View?> Render { get; init; } = s => new View(s.ToString());
        public Task Task { get; private set; } = Task.CompletedTask;

        public Harness Start()
        {
            Task = DisplayWatch.RunAsync(0, Updates, Render, Display, NullLogger.Instance, Cts.Token);
            return this;
        }

        public void Push(Func<int, int> f) => Updates.OnNext(f);

        public void Dispose()
        {
            Cts.Cancel();
            Cts.Dispose();
            Updates.Dispose();
        }
    }

    private static View V(int n) => new(n.ToString());

    private static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    [Fact]
    public void Clears_before_first_draw_and_draws_initial_state()
    {
        using var h = new Harness().Start();
        Assert.Equal([FakeDisplay<View>.Clear, V(0)], h.Display.Calls);
    }

    [Fact]
    public void Identical_render_is_not_redrawn()
    {
        using var h = new Harness().Start();
        h.Push(s => s);
        h.Push(s => s);
        Assert.Equal([V(0)], h.Display.Draws);
    }

    [Fact]
    public void Changed_render_is_drawn()
    {
        using var h = new Harness().Start();
        h.Push(s => s + 1);
        Assert.Equal([V(0), V(1)], h.Display.Draws);
    }

    [Fact]
    public void Returning_to_an_earlier_view_is_drawn()
    {
        using var h = new Harness().Start();
        h.Push(s => s + 1);
        h.Push(s => s - 1);
        Assert.Equal([V(0), V(1), V(0)], h.Display.Draws);
    }

    [Fact]
    public void First_draw_rejected_with_409_is_retried_on_next_update()
    {
        using var h = new Harness();
        h.Display.FailNextDraws(1, new Transient());
        h.Start();
        Assert.False(h.Task.IsCompleted);
        h.Push(s => s);
        Assert.Equal([V(0), V(0)], h.Display.Draws);
        h.Push(s => s);
        Assert.Equal([V(0), V(0)], h.Display.Draws);
    }

    [Fact]
    public void Null_render_draws_nothing()
    {
        using var h = new Harness { Render = _ => null }.Start();
        h.Push(s => s + 1);
        Assert.Equal([FakeDisplay<View>.Clear], h.Display.Calls);
    }

    [Fact]
    public async Task Cancellation_clears_and_returns()
    {
        using var h = new Harness().Start();
        h.Cts.Cancel();
        await h.Task;
        Assert.Equal([FakeDisplay<View>.Clear, V(0), FakeDisplay<View>.Clear], h.Display.Calls);
    }

    [Fact]
    public async Task Completion_of_updates_clears_and_returns()
    {
        using var h = new Harness().Start();
        h.Updates.OnCompleted();
        await h.Task;
        Assert.Equal([FakeDisplay<View>.Clear, V(0), FakeDisplay<View>.Clear], h.Display.Calls);
    }

    [Fact]
    public async Task Fatal_update_error_clears_then_rethrows()
    {
        using var h = new Harness().Start();
        h.Updates.OnError(new FatalTest());
        await Assert.ThrowsAsync<FatalTest>(() => h.Task);
        Assert.Equal(FakeDisplay<View>.Clear, h.Display.Calls[^1]);
    }

    [Fact]
    public async Task Fatal_draw_error_clears_then_rethrows()
    {
        using var h = new Harness();
        h.Display.FailNextDraws(1, new FatalTest());
        h.Start();
        await Assert.ThrowsAsync<FatalTest>(() => h.Task);
        Assert.Equal([FakeDisplay<View>.Clear, V(0), FakeDisplay<View>.Clear], h.Display.Calls);
    }

    [Fact]
    public async Task Clear_failure_on_exit_does_not_mask_original_error()
    {
        using var h = new Harness().Start();
        h.Display.FailNextClears(1, new Transient());
        h.Updates.OnError(new FatalTest());
        await Assert.ThrowsAsync<FatalTest>(() => h.Task);
        Assert.Equal(FakeDisplay<View>.Clear, h.Display.Calls[^1]);
    }

    [Fact]
    public async Task Clear_failure_on_exit_after_cancellation_is_swallowed()
    {
        using var h = new Harness().Start();
        h.Display.FailNextClears(1, new FatalTest());
        h.Cts.Cancel();
        await h.Task;
        Assert.Equal(FakeDisplay<View>.Clear, h.Display.Calls[^1]);
    }

    [Fact]
    public async Task Startup_clear_failure_propagates_and_nothing_is_drawn()
    {
        using var h = new Harness();
        h.Display.FailNextClears(1, new Transient());
        h.Start();
        await Assert.ThrowsAsync<Transient>(() => h.Task);
        h.Push(s => s + 1);
        Assert.Empty(h.Display.Draws);
        Assert.False(h.Updates.HasObservers);
    }

    [Fact]
    public async Task Draws_never_overlap()
    {
        using var h = new Harness();
        var firstDraw = new TaskCompletionSource();
        var active = 0;
        var maxActive = 0;
        h.Display.OnDraw = async view =>
        {
            maxActive = Math.Max(maxActive, Interlocked.Increment(ref active));
            if (view == V(0))
            {
                await firstDraw.Task;
            }

            Interlocked.Decrement(ref active);
        };
        h.Start();

        h.Push(s => s + 1);
        h.Push(s => s + 1);
        Assert.Equal([V(0)], h.Display.Draws);

        firstDraw.SetResult();
        await Eventually(() => h.Display.Draws.Count == 3);
        Assert.Equal([V(0), V(1), V(2)], h.Display.Draws);
        Assert.Equal(1, maxActive);
    }
    [Fact]
    public async Task Cancellation_waits_for_in_flight_draw_before_exit_clear()
    {
        using var h = new Harness();
        var draw = new TaskCompletionSource();
        h.Display.OnDraw = _ => draw.Task;
        h.Start();

        h.Cts.Cancel();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal([FakeDisplay<View>.Clear, V(0)], h.Display.Calls);
        Assert.False(h.Task.IsCompleted);

        draw.SetResult();
        await h.Task;
        Assert.Equal([FakeDisplay<View>.Clear, V(0), FakeDisplay<View>.Clear], h.Display.Calls);
    }

    [Fact]
    public async Task Update_error_waits_for_in_flight_draw_before_exit_clear()
    {
        using var h = new Harness();
        var draw = new TaskCompletionSource();
        h.Display.OnDraw = _ => draw.Task;
        h.Start();

        h.Updates.OnError(new FatalTest());
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal([FakeDisplay<View>.Clear, V(0)], h.Display.Calls);
        Assert.False(h.Task.IsCompleted);

        draw.SetResult();
        await Assert.ThrowsAsync<FatalTest>(() => h.Task);
        Assert.Equal([FakeDisplay<View>.Clear, V(0), FakeDisplay<View>.Clear], h.Display.Calls);
    }
}
