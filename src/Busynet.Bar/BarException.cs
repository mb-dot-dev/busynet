using Busynet.Core;

namespace Busynet.Bar;

/// <summary>Any failure talking to the bar; transient while watching.</summary>
public abstract class BarException(string message) : BusynetException(message), ITransientFailure
{
    public TimeSpan? RetryAfter => null;
}

/// <summary>The bar answered with a non-success status.</summary>
public sealed class BarApiException(int statusCode, string method, string path, string error, string? errorCode)
    : BarException(Format(statusCode, method, path, error, errorCode))
{
    public int StatusCode { get; } = statusCode;
    public string? ErrorCode { get; } = errorCode;

    private static string Format(int statusCode, string method, string path, string error, string? errorCode) =>
        $"HTTP {statusCode} | {method} {path} | {OneLine(error)}" + (errorCode is null ? "" : $" | code={OneLine(errorCode)}");

    private static string OneLine(string text) =>
        text.ReplaceLineEndings(" ");
}

/// <summary>The request never produced a response (transport failure or timeout).</summary>
/// <param name="detail">The exception type name only: messages can carry URLs and tokens.</param>
public sealed class BarRequestException(string method, string path, string detail)
    : BarException($"request error | {method} {path} | {detail}");
