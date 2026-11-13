using Busynet.Claude.Usage;

namespace Busynet.Cli.Tests.Fakes;

/// <summary>Scripted fetch results in order, the last one repeating; each runs asynchronously so throws carry a stack.</summary>
public sealed class ScriptedUsage(params Func<Usage>[] results) : IClaudeUsage
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public async Task<Usage> GetUsageAsync(CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _calls);
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return results[Math.Min(n, results.Length) - 1]();
    }
}

/// <summary>Never answers: waits until cancelled, as a slow API would.</summary>
public sealed class BlockingUsage : IClaudeUsage
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Usage> GetUsageAsync(CancellationToken ct)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    }
}
