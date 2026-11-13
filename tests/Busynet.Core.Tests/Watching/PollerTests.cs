using System.Reactive.Concurrency;
using Busynet.Core.Watching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

namespace Busynet.Core.Tests.Watching;

public class PollerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(180);

    private sealed class Transient(TimeSpan? retryAfter) : BusynetException("transient"), ITransientFailure
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    private sealed class Fatal() : BusynetException("fatal");

    private sealed class Harness
    {
        private readonly Func<int, int> _behaviour;
        public TestScheduler Scheduler { get; } = new();
        public List<TimeSpan> Calls { get; } = [];
        public List<int> Values { get; } = [];
        public List<Exception> Errors { get; } = [];
        public bool Completed { get; private set; }

        public Harness(Func<int, int> behaviour, TimeSpan initialDelay)
        {
            _behaviour = behaviour;
            Poller.Poll(Fetch, Interval, initialDelay, Scheduler, NullLogger.Instance)
                .Subscribe(Values.Add, Errors.Add, () => Completed = true);
        }

        private Task<int> Fetch(CancellationToken _)
        {
            // TestScheduler runs zero-delay work one tick late; truncate to whole seconds.
            Calls.Add(TimeSpan.FromSeconds(Scheduler.Clock / TimeSpan.TicksPerSecond));
            return Task.FromResult(_behaviour(Calls.Count));
        }

        // Slack absorbs the one-tick-per-timer lag of the virtual clock.
        public void RunTo(TimeSpan t) => Scheduler.AdvanceTo(t.Ticks + 10);
    }

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void First_fetch_happens_after_initial_delay()
    {
        var h = new Harness(n => n, S(180));
        h.RunTo(S(179));
        Assert.Empty(h.Calls);
        h.RunTo(S(180));
        Assert.Equal([S(180)], h.Calls);
    }

    [Fact]
    public void Fetches_repeat_every_interval()
    {
        var h = new Harness(n => n, TimeSpan.Zero);
        h.RunTo(S(360));
        Assert.Equal([S(0), S(180), S(360)], h.Calls);
        Assert.Equal([1, 2, 3], h.Values);
        Assert.False(h.Completed);
    }

    [Fact]
    public void Transient_failure_is_swallowed_and_polling_continues()
    {
        var h = new Harness(n => n == 2 ? throw new Transient(null) : n, TimeSpan.Zero);
        h.RunTo(S(360));
        Assert.Equal([S(0), S(180), S(360)], h.Calls);
        Assert.Equal([1, 3], h.Values);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void Retry_after_longer_than_interval_extends_the_wait()
    {
        var h = new Harness(n => n == 1 ? throw new Transient(S(600)) : n, TimeSpan.Zero);
        h.RunTo(S(599));
        Assert.Equal([S(0)], h.Calls);
        h.RunTo(S(600));
        Assert.Equal([S(0), S(600)], h.Calls);
    }

    [Fact]
    public void Retry_after_is_capped_at_one_hour()
    {
        var h = new Harness(n => n == 1 ? throw new Transient(TimeSpan.FromHours(10)) : n, TimeSpan.Zero);
        h.RunTo(S(3599));
        Assert.Equal([S(0)], h.Calls);
        h.RunTo(S(3600));
        Assert.Equal([S(0), S(3600)], h.Calls);
    }

    [Fact]
    public void Retry_after_shorter_than_interval_is_ignored()
    {
        var h = new Harness(n => n == 1 ? throw new Transient(S(10)) : n, TimeSpan.Zero);
        h.RunTo(S(179));
        Assert.Equal([S(0)], h.Calls);
        h.RunTo(S(180));
        Assert.Equal([S(0), S(180)], h.Calls);
    }

    [Fact]
    public void Fatal_failure_errors_the_sequence()
    {
        var fatal = new Fatal();
        var h = new Harness(n => n == 2 ? throw fatal : n, TimeSpan.Zero);
        h.RunTo(S(1000));
        Assert.Equal([S(0), S(180)], h.Calls);
        Assert.Same(fatal, Assert.Single(h.Errors));
    }
}
