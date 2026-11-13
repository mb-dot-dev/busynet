using Busynet.Core;

namespace Busynet.Cli.Infrastructure;

/// <summary>
/// The exit-code contract: usage errors (reported by <see cref="BusynetApp"/>) exit 2, expected failures print their one-line message and exit 1
/// (the full exception with <c>--verbose</c>), anything else prints in full and exits 1. Ctrl+C exits 0.
/// </summary>
public sealed class ErrorHandler(TextWriter stderr, CancellationToken cancellationToken)
{
    public const int Failure = 1;
    public const int UsageError = 2;

    /// <param name="provider">The run's services; null when the failure came before they were built.</param>
    public int Handle(Exception exception, IServiceProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                return 0;
            case BusynetException:
                stderr.WriteLine(IsVerbose(provider) ? exception.ToString() : exception.Message);
                return Failure;
            default:
                stderr.WriteLine(exception.ToString());
                return Failure;
        }
    }

    private static bool IsVerbose(IServiceProvider? provider) =>
        provider?.GetService(typeof(Verbosity)) is Verbosity { IsVerbose: true };
}
