namespace Busynet.Core.Tests;

public class TransientFailureTests
{
    private sealed class Flaky() : BusynetException("flaky"), ITransientFailure
    {
        public TimeSpan? RetryAfter => TimeSpan.FromSeconds(5);
    }

    [Fact]
    public void IsTransient_reads_retry_after_from_marked_exceptions()
    {
        Assert.True(TransientFailure.IsTransient(new Flaky(), out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(5), retryAfter);

        Assert.False(TransientFailure.IsTransient(new InvalidOperationException(), out var none));
        Assert.Null(none);
    }
}
