namespace Busynet.Core;

/// <summary>Marks a failure worth retrying, optionally carrying a server-suggested delay.</summary>
public interface ITransientFailure
{
    TimeSpan? RetryAfter { get; }
}

public static class TransientFailure
{
    public static bool IsTransient(Exception e, out TimeSpan? retryAfter)
    {
        if (e is ITransientFailure transient)
        {
            retryAfter = transient.RetryAfter;
            return true;
        }

        retryAfter = null;
        return false;
    }
}
