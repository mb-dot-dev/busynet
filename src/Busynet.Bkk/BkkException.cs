using Busynet.Core;

namespace Busynet.Bkk;

/// <summary>A failure talking to BKK FUTÁR. Messages never contain the API key or a request URL.</summary>
public class BkkException(string message) : BusynetException(message);

/// <summary>A failure worth retrying: 5xx, rate limiting, transport failures and malformed bodies.</summary>
public sealed class BkkTransientException(string message, TimeSpan? retryAfter = null)
    : BkkException(message), ITransientFailure
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
